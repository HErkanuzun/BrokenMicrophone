using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace BrokenMicrophone
{
    class OutputDevice
    {
        public int Id;          // waveOut cihaz numarası; -1 = Windows'un varsayılanı
        public string Name;
        public bool Virtual;    // sanal mikrofonun girişi (sesi buraya verince başka programlarda mikrofon olarak görünür)
        public override string ToString() { return Virtual ? L.T("Sanal mikrofon · ", "Virtual microphone · ") + Name : Name; }
    }

    // Telefondan gelen 16 bit mono sesi bir çıkış cihazına verir (winmm waveOut).
    // Ağdan gelen paketler düzensiz gelir ve telefonun saati bilgisayarınkinden biraz hızlı/yavaş işler. Bunun için:
    // - ses önce bir tampona yazılır; tampon hedefi Wi-Fi ne kadar dalgalanıyorsa o kadar büyür, sakinleşince küçülür;
    // - tampon hedefin üstüne çıkarsa ses fark edilmeyecek kadar (%0,4-1) hızlı, altına inerse yavaş çalınır,
    //   böylece saat farkı kopma ya da atlama olmadan düzelir;
    // - ses yine de biterse son parça yumuşakça kısılır, geri gelince yumuşakça açılır (çıt sesi olmaz).
    class Player : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WAVEOUTCAPS { public ushort wMid, wPid; public uint vDriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname; public uint dwFormats; public ushort wChannels, wReserved1; public uint dwSupport; }

        [DllImport("winmm.dll")] static extern int waveOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int waveOutGetDevCaps(IntPtr id, out WAVEOUTCAPS caps, int size);
        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, IntPtr id, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

        const int CALLBACK_EVENT = 0x50000, WHDR_DONE = 1, WAVE_MAPPER = -1;
        const int Buffers = 6;   // kartta bekleyen 60 ms: iş parçacığı bir an geç kalsa da ses kopmaz

        public static List<OutputDevice> Devices()
        {
            var list = new List<OutputDevice>();
            int n = waveOutGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                WAVEOUTCAPS c;
                if (waveOutGetDevCaps(new IntPtr(i), out c, Marshal.SizeOf(typeof(WAVEOUTCAPS))) != 0) continue;
                var d = new OutputDevice();
                d.Id = i;
                d.Name = c.szPname;
                d.Virtual = IsVirtualMicInput(d.Name);
                list.Add(d);
            }
            return list;
        }

        // Sanal mikrofon sürücülerinin "hoparlör" ucu: buraya çalınan ses, sürücünün mikrofon ucundan çıkar
        public static bool IsVirtualMicInput(string name)
        {
            string s = name.ToLowerInvariant();
            return s.Contains("broken microphone") || s.Contains("cable input") || s.Contains("vb-audio virtual");
        }

        public readonly int Rate;
        readonly IntPtr wave;
        readonly AutoResetEvent done = new AutoResetEvent(false);
        readonly IntPtr[] hdrs = new IntPtr[Buffers];
        readonly IntPtr[] data = new IntPtr[Buffers];
        readonly int frame;                 // bir tampondaki örnek sayısı (10 ms)
        readonly short[] ring;              // gelen ses
        int head, count;                    // ring: okuma yeri ve dolu örnek sayısı
        bool filling = true;                // tampon yeniden doluyor (çalmadan önce hedef seviyeye ulaşsın)
        bool fadeIn;                        // dolduktan sonraki ilk parça yumuşak açılır
        int target;                         // hedef tampon (örnek); kesinti olunca büyür, sakinken küçülür
        readonly int minTarget, maxTarget, hardMax;
        DateTime calmSince = DateTime.Now;
        readonly object sync = new object();
        readonly Thread thread;
        volatile bool stop;
        public volatile float Volume = 1f;
        public int Underruns, Dropped;

        public Player(int deviceId, int rate)
        {
            Rate = rate;
            frame = rate / 100;
            minTarget = rate * 60 / 1000;   // 60 ms: Wi-Fi'nin olağan dalgalanması
            maxTarget = rate * 250 / 1000;
            hardMax = rate * 600 / 1000;    // bundan fazla birikirse (ör. telefon bir an donduysa) eskisi atılır
            target = rate * 80 / 1000;
            ring = new short[rate * 2];     // 2 saniye

            var fmt = new WAVEFORMATEX();
            fmt.wFormatTag = 1;
            fmt.nChannels = 1;
            fmt.nSamplesPerSec = (uint)rate;
            fmt.wBitsPerSample = 16;
            fmt.nBlockAlign = 2;
            fmt.nAvgBytesPerSec = (uint)rate * 2;
            int r = waveOutOpen(out wave, new IntPtr(deviceId < 0 ? WAVE_MAPPER : deviceId), ref fmt, done.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
            if (r != 0) throw new InvalidOperationException(L.T("Ses çıkışı açılamadı (hata ", "Couldn't open the audio output (error ") + r + ").");

            int hsize = Marshal.SizeOf(typeof(WAVEHDR));
            for (int i = 0; i < Buffers; i++)
            {
                data[i] = Marshal.AllocHGlobal(frame * 2);
                hdrs[i] = Marshal.AllocHGlobal(hsize);
                var h = new WAVEHDR();
                h.lpData = data[i];
                h.dwBufferLength = (uint)(frame * 2);
                Marshal.StructureToPtr(h, hdrs[i], false);
                waveOutPrepareHeader(wave, hdrs[i], hsize);
            }
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.Highest;
            thread.Name = "Player";
            thread.Start();
        }

        // Ağ iş parçacığından çağrılır: little-endian 16 bit örnekler
        public void Write(byte[] buf, int offset, int bytes)
        {
            int n = bytes / 2;
            lock (sync)
            {
                for (int i = 0; i < n; i++)
                {
                    short s = (short)(buf[offset + i * 2] | (buf[offset + i * 2 + 1] << 8));
                    if (count == ring.Length) { head = (head + 1) % ring.Length; count--; }
                    ring[(head + count) % ring.Length] = s;
                    count++;
                }
                if (count > hardMax)
                {
                    int drop = count - target;
                    head = (head + drop) % ring.Length;
                    count -= drop;
                    Dropped += drop;
                }
            }
        }

        public int BufferedMs { get { lock (sync) return count * 1000 / Rate; } }
        public int TargetMs { get { lock (sync) return target * 1000 / Rate; } }

        void Run()
        {
            var tmp = new short[frame];
            var last = new short[frame];
            // başta bütün tamponlar sessizlikle kuyruğa girer
            for (int i = 0; i < Buffers; i++) Submit(i, tmp, true);
            while (!stop)
            {
                done.WaitOne(20);
                for (int i = 0; i < Buffers && !stop; i++)
                {
                    var h = (WAVEHDR)Marshal.PtrToStructure(hdrs[i], typeof(WAVEHDR));
                    if ((h.dwFlags & WHDR_DONE) == 0) continue;
                    int mode;   // 0 sessiz, 1 ses, 2 son parçayı kısarak bitir
                    lock (sync) mode = Next(tmp);
                    if (mode == 2)
                        for (int k = 0; k < frame; k++) tmp[k] = (short)(last[k] * (frame - k) / frame);
                    if (mode == 1) Array.Copy(tmp, last, frame);
                    else Array.Clear(last, 0, frame);
                    Submit(i, tmp, mode == 0);
                }
            }
        }

        // sync kilidi altında: bir sonraki 10 ms'yi tmp'ye hazırlar
        int Next(short[] tmp)
        {
            var now = DateTime.Now;
            if (filling)
            {
                if (count < target) return 0;
                filling = false;
                fadeIn = true;
            }
            if (count < frame)
            {
                // ses bitti: Wi-Fi dalgalanıyor, hedef büyür
                filling = true;
                Underruns++;
                target = Math.Min(maxTarget, target + Rate * 30 / 1000);
                calmSince = now;
                return 2;
            }
            // sakin geçen her 20 saniyede hedef biraz küçülür (gecikme gereksiz büyük kalmasın)
            if ((now - calmSince).TotalSeconds > 20)
            {
                target = Math.Max(minTarget, target - Rate * 10 / 1000);
                calmSince = now;
            }
            // saat farkı: tampon hedeften uzaksa biraz hızlı ya da yavaş çal
            int n = frame;
            int over = count - target, ms = Rate / 1000;
            if (over > 80 * ms) n = frame + Math.Max(2, frame * 3 / 200);       // %1,5
            else if (over > 40 * ms) n = frame + Math.Max(1, frame / 125);      // %0,8
            else if (over > 15 * ms) n = frame + Math.Max(1, frame / 250);      // %0,4
            else if (over < -25 * ms) n = frame - Math.Max(1, frame / 250);
            n = Math.Min(n, count);
            if (n == frame)
                for (int k = 0; k < frame; k++) { tmp[k] = ring[head]; head = (head + 1) % ring.Length; }
            else
            {
                // n örneği frame örneğe doğrusal olarak yeniden örnekle
                for (int k = 0; k < frame; k++)
                {
                    double pos = k * (n - 1) / (double)Math.Max(1, frame - 1);
                    int i0 = (int)pos;
                    int i1 = Math.Min(n - 1, i0 + 1);
                    double f = pos - i0;
                    int a = ring[(head + i0) % ring.Length], b = ring[(head + i1) % ring.Length];
                    tmp[k] = (short)(a + (b - a) * f);
                }
                head = (head + n) % ring.Length;
            }
            count -= n;
            if (fadeIn)
            {
                for (int k = 0; k < frame; k++) tmp[k] = (short)(tmp[k] * k / frame);
                fadeIn = false;
            }
            return 1;
        }

        void Submit(int i, short[] samples, bool silent)
        {
            float v = Volume;
            if (silent || v <= 0f) { for (int k = 0; k < frame; k++) Marshal.WriteInt16(data[i], k * 2, 0); }
            else if (v == 1f) Marshal.Copy(samples, 0, data[i], frame);
            else
                for (int k = 0; k < frame; k++)
                {
                    int s = (int)(samples[k] * v);
                    Marshal.WriteInt16(data[i], k * 2, (short)(s > 32767 ? 32767 : s < -32768 ? -32768 : s));
                }
            waveOutWrite(wave, hdrs[i], Marshal.SizeOf(typeof(WAVEHDR)));
        }

        public void Dispose()
        {
            if (stop) return;
            stop = true;
            done.Set();
            thread.Join(1000);
            waveOutReset(wave);
            int hsize = Marshal.SizeOf(typeof(WAVEHDR));
            for (int i = 0; i < Buffers; i++)
            {
                waveOutUnprepareHeader(wave, hdrs[i], hsize);
                Marshal.FreeHGlobal(hdrs[i]);
                Marshal.FreeHGlobal(data[i]);
            }
            waveOutClose(wave);
            done.Close();
        }
    }
}
