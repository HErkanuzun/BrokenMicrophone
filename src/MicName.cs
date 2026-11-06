using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace BrokenMicrophone
{
    /// <summary>
    /// Sanal mikrofonun (ör. VB-CABLE'ın "CABLE Output") Windows'taki adını okur ve değiştirir. Discord, Zoom gibi
    /// programlar bu adı gösterir: "Broken Mic (VB-Audio Virtual Cable)". Windows'un Ses ayarlarındaki
    /// "Yeniden adlandır" ile aynı şeyi yapar; sürücüye ya da sese dokunmaz.
    /// </summary>
    static class MicName
    {
        const string CaptureKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture";
        const string NameProp = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";     // PKEY_Device_DeviceDesc (değiştirilebilen ad)
        const string DriverProp = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";   // sürücünün adı (parantez içindeki kısım)
        public const string Default = "Broken Mic";

        public class Endpoint { public string Guid, Name, Driver; public string Full { get { return Name + " (" + Driver + ")"; } } }

        static bool IsOurs(string driver)
        {
            string d = (driver ?? "").ToLowerInvariant();
            return d.Contains("vb-audio virtual cable") || d.Contains("broken microphone");
        }

        /// <summary>Sanal mikrofonun kayıt ucu (yoksa null). Yönetici izni gerekmez.</summary>
        public static Endpoint Find()
        {
            try
            {
                using (var root = Registry.LocalMachine.OpenSubKey(CaptureKey))
                {
                    if (root == null) return null;
                    foreach (var id in root.GetSubKeyNames())
                        using (var k = root.OpenSubKey(id))
                        using (var p = root.OpenSubKey(id + @"\Properties"))
                        {
                            if (k == null || p == null) continue;
                            object state = k.GetValue("DeviceState");
                            if (state is int && ((int)state & 0xF) != 1) continue;   // sadece etkin cihaz
                            string drv = p.GetValue(DriverProp) as string;
                            if (!IsOurs(drv)) continue;
                            var e = new Endpoint();
                            e.Guid = id;
                            e.Driver = drv;
                            e.Name = p.GetValue(NameProp) as string ?? "";
                            return e;
                        }
                }
            }
            catch { }
            return null;
        }

        public static string Clean(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim()) if (!char.IsControl(c) && c != '"') sb.Append(c);
            string s = sb.ToString();
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        /// <summary>Adı değiştirir. Windows izin vermezse yönetici izni isteyip kendini yeniden çalıştırır. Hata yoksa null.</summary>
        public static string Rename(Endpoint e, string name)
        {
            name = Clean(name);
            if (name == "") return L.T("Ad boş olamaz.", "The name can't be empty.");
            int hr = Write(e.Guid, name);
            if (hr == 0) return null;
            if (hr != unchecked((int)0x80070005)) return L.T("Ad değiştirilemedi (kod 0x", "Couldn't rename (code 0x") + hr.ToString("X8") + ").";
            try
            {
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(name));
                var psi = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, "--rename-mic " + e.Guid + " " + b64);
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(30000)) return L.T("Zaman aşımı.", "Timed out.");
                    return p.ExitCode == 0 ? null : L.T("Ad değiştirilemedi (kod 0x", "Couldn't rename (code 0x") + p.ExitCode.ToString("X8") + ").";
                }
            }
            catch (System.ComponentModel.Win32Exception) { return L.T("Yönetici izni verilmedi.", "Administrator permission was not given."); }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Program.cs: yönetici olarak açılan yardımcı kopya. Çıkış kodu = HRESULT.</summary>
        public static int RunElevated(string guid, string b64)
        {
            try
            {
                if (guid.Length != 38 || guid[0] != '{') return 87;
                return Write(guid, Clean(Encoding.UTF8.GetString(Convert.FromBase64String(b64))));
            }
            catch { return 87; }
        }

        // ------------------------------------------------------------------ Windows ses aygıtı API'si
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IntPtr device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, out IntPtr o);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int i, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, IntPtr value);
            [PreserveSig] int SetValue(ref PROPERTYKEY key, IntPtr value);
            [PreserveSig] int Commit();
        }

        [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }

        static int Write(string guid, string name)
        {
            IntPtr pv = IntPtr.Zero, str = IntPtr.Zero;
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                IMMDevice dev;
                int hr = en.GetDevice("{0.0.1.00000000}." + guid, out dev);   // 1 = kayıt (mikrofon) ucu
                if (hr != 0) return hr;
                IPropertyStore ps;
                hr = dev.OpenPropertyStore(2, out ps);                         // STGM_READWRITE
                if (hr != 0) return hr;
                var key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 2 };
                // PROPVARIANT: VT_LPWSTR (31) + metin göstergesi (24 bayt yeter)
                pv = Marshal.AllocCoTaskMem(24);
                for (int i = 0; i < 24; i++) Marshal.WriteByte(pv, i, 0);
                str = Marshal.StringToCoTaskMemUni(name);
                Marshal.WriteInt16(pv, 0, 31);
                Marshal.WriteIntPtr(pv, 8, str);
                hr = ps.SetValue(ref key, pv);
                if (hr != 0) return hr;
                return ps.Commit();
            }
            catch (UnauthorizedAccessException) { return unchecked((int)0x80070005); }
            catch (COMException ex) { return ex.ErrorCode; }
            catch (Exception) { return unchecked((int)0x80004005); }
            finally
            {
                if (str != IntPtr.Zero) Marshal.FreeCoTaskMem(str);
                if (pv != IntPtr.Zero) Marshal.FreeCoTaskMem(pv);
            }
        }
    }
}
