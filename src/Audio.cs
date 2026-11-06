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
    // Ağdan gelen paketler düzensizdir: ses önce küçük bir tampona yazılır, tampon çok dolarsa
    // eski ses atılır (gecikme büyümesin), boşalırsa sessizlik çalınır ve tampon yeniden doldurulur.
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
        const int Buffers = 4;

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
        readonly int target, max;           // hedef ve en fazla tampon (örnek)
        readonly object sync = new object();
        readonly Thread thread;
        volatile bool stop;
        public volatile float Volume = 1f;
        public int Underruns, Dropped;

        public Player(int deviceId, int rate)
        {
            Rate = rate;
            frame = rate / 100;
            target = rate * 60 / 1000;      // 60 ms: Wi-Fi'nin olağan dalgalanmasını karşılar
            max = rate * 200 / 1000;        // 200 ms'den fazla birikirse eskisi atılır
            ring = new short[rate];         // 1 saniye

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
            thread.Priority = ThreadPriority.AboveNormal;
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
                if (count > max)
                {
                    // gecikme büyüdü: hedef seviyeye kadar eski ses atılır
                    int drop = count - target;
                    head = (head + drop) % ring.Length;
                    count -= drop;
                    Dropped += drop;
                }
            }
        }

        public int BufferedMs { get { lock (sync) return count * 1000 / Rate; } }

        void Run()
        {
            var tmp = new short[frame];
            // başta bütün tamponlar sessizlikle kuyruğa girer
            for (int i = 0; i < Buffers; i++) Submit(i, tmp, true);
            int hsize = Marshal.SizeOf(typeof(WAVEHDR));
            while (!stop)
            {
                done.WaitOne(50);
                for (int i = 0; i < Buffers && !stop; i++)
                {
                    var h = (WAVEHDR)Marshal.PtrToStructure(hdrs[i], typeof(WAVEHDR));
                    if ((h.dwFlags & WHDR_DONE) == 0) continue;
                    bool silent;
                    lock (sync)
                    {
                        if (filling && count >= target) filling = false;
                        if (!filling && count < frame) { filling = true; Underruns++; }
                        silent = filling;
                        if (!silent)
                            for (int k = 0; k < frame; k++) { tmp[k] = ring[head]; head = (head + 1) % ring.Length; count--; }
                    }
                    Submit(i, tmp, silent);
                }
            }
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
