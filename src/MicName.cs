using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace BrokenMicrophone
{
    /// <summary>
    /// Sanal mikrofonun (VB-CABLE'ın kayıt ucu) Windows'taki adını okur ve değiştirir. Programlar iki ad gösterir:
    /// Discord'da büyük satır = arayüz adı ("VB-Audio Virtual Cable"), küçük satır = cihaz adı ("CABLE Output").
    /// Büyük satır kullanıcının verdiği ad olur (varsayılan "Broken Mic"), küçük satır "Mikrofon".
    /// Sürücüye ya da sese dokunmaz; yalnızca Windows'un sakladığı adlar değişir.
    /// </summary>
    static class MicName
    {
        const string CaptureKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture";
        const string DescFmt = "a45c254e-df1c-4efd-8020-67d146a850e0";   // ,2  cihaz adı (küçük satır)
        const string IfFmt = "b3f8fa53-0004-438e-9003-51a46e139bfc";     // ,6  arayüz adı (büyük satır)
        const string DescProp = "{" + DescFmt + "},2", IfProp = "{" + IfFmt + "},6";
        const string ServiceProp = "{a8b865dd-2e3d-4094-ad97-e593a70c75d6},8";   // sürücü servisi (ad değişse de sabit)
        const string InfProp = "{83da6326-97a6-4088-9453-a1923f573b29},3";
        public const string Default = "Broken Mic";

        public class Endpoint
        {
            public string Guid, Name, Desc;   // Name: programlarda görünen ad (büyük satır)
            public string Full { get { return Desc + " (" + Name + ")"; } }
            public bool IsFactoryName { get { return Name.ToLowerInvariant().Contains("vb-audio virtual cable"); } }
        }

        static string Small { get { return L.T("Mikrofon", "Microphone"); } }

        static bool IsOurs(Microsoft.Win32.RegistryKey p)
        {
            string svc = (p.GetValue(ServiceProp) as string ?? "").ToLowerInvariant();
            string inf = (p.GetValue(InfProp) as string ?? "").ToLowerInvariant();
            string name = (p.GetValue(IfProp) as string ?? "").ToLowerInvariant();
            return svc == "vbaudiovacwdm" || inf.Contains("vbcableinst") || name.Contains("vb-audio virtual cable") || name.Contains("broken microphone");
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
                            if (!IsOurs(p)) continue;
                            var e = new Endpoint();
                            e.Guid = id;
                            e.Name = p.GetValue(IfProp) as string ?? "";
                            e.Desc = p.GetValue(DescProp) as string ?? "";
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

        /// <summary>Adı değiştirir. Windows izin vermezse (ve izin verildiyse) yönetici izni isteyip kendini yeniden çalıştırır. Hata yoksa null.</summary>
        public static string Rename(Endpoint e, string name, bool mayElevate = true)
        {
            name = Clean(name);
            if (name == "") return L.T("Ad boş olamaz.", "The name can't be empty.");
            int hr = WriteBoth(e.Guid, name, Small);
            if (hr == 0) return null;
            if (hr != unchecked((int)0x80070005) || !mayElevate) return L.T("Ad değiştirilemedi (kod 0x", "Couldn't rename (code 0x") + hr.ToString("X8") + ").";
            try
            {
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(name)), s64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Small));
                var psi = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, "--rename-mic " + e.Guid + " " + b64 + " " + s64);
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
        public static int RunElevated(string guid, string b64, string s64)
        {
            try
            {
                if (guid.Length != 38 || guid[0] != '{') return 87;
                string small = s64 == null ? "Mikrofon" : Clean(Encoding.UTF8.GetString(Convert.FromBase64String(s64)));
                return WriteBoth(guid, Clean(Encoding.UTF8.GetString(Convert.FromBase64String(b64))), small);
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

        static int WriteBoth(string guid, string name, string small)
        {
            int hr = Write(guid, IfFmt, 6, name);
            if (hr != 0) return hr;
            if (small != "") Write(guid, DescFmt, 2, small);   // küçük satır: olmazsa da önemli değil
            return 0;
        }

        static int Write(string guid, string fmt, int pid, string name)
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
                var key = new PROPERTYKEY { fmtid = new Guid(fmt), pid = pid };
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
