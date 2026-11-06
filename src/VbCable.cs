using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BrokenMicrophone
{
    /// <summary>
    /// VB-CABLE'ı (VB-Audio'nun ücretsiz sanal ses sürücüsü) güvenli kurar: kullanıcı bir siteye yönlendirilmez.
    /// Paket programın içine yazılı resmi adresten iner; ya SHA-256 özeti bilinen sürümle aynı olmalı ya da kurulum
    /// programı VB-Audio'nun (BUREL VINCENT) geçerli Windows imzasını taşımalı. İkisi de tutmazsa hiçbir şey çalıştırılmaz.
    /// VB-CABLE'ın lisansı yeniden dağıtıma izin vermediği için paketi biz taşımıyoruz; kullanıcının bilgisayarına
    /// doğrudan VB-Audio'dan iner.
    /// </summary>
    static class VbCable
    {
        public const string Url = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip";
        // Bilinen paketler (indirilip imzaları denetlendi)
        static readonly string[] KnownSha256 = { "B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB" };
        const string Signer = "BUREL VINCENT";
        const long MaxSize = 30L * 1024 * 1024;

        /// <summary>İndirir, doğrular, açar. Dönen klasörde doğrulanmış kurulum programı vardır.</summary>
        public static string Prepare(Action<int> progress)
        {
            string dir = Path.Combine(Path.GetTempPath(), "BrokenMicrophone-vbcable-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string zip = Path.Combine(dir, "pack.zip");

            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            var req = (HttpWebRequest)WebRequest.Create(Url);
            req.UserAgent = "BrokenMicrophone/" + AppInfo.Version;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                // yönlendirme olsa bile son adres VB-Audio'nun alanında olmalı
                string host = resp.ResponseUri.Host.ToLowerInvariant();
                if (resp.ResponseUri.Scheme != "https" || !(host == "vb-audio.com" || host.EndsWith(".vb-audio.com")))
                    throw new InvalidOperationException(L.T("İndirme beklenmeyen bir adrese yönlendi: ", "The download was redirected to an unexpected address: ") + host);
                long total = resp.ContentLength;
                using (var src = resp.GetResponseStream())
                using (var dst = File.Create(zip))
                {
                    var buf = new byte[65536];
                    long got = 0;
                    int n, last = -1;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        got += n;
                        if (got > MaxSize) throw new InvalidOperationException(L.T("İndirilen dosya beklenenden büyük.", "The download is larger than expected."));
                        dst.Write(buf, 0, n);
                        int pct = total > 0 ? (int)(got * 100 / total) : -1;
                        if (pct != last) { last = pct; progress(pct); }
                    }
                }
            }

            string sha;
            using (var f = File.OpenRead(zip))
            using (var h = SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(f)).Replace("-", "");
            bool knownPack = Array.IndexOf(KnownSha256, sha) >= 0;

            // sadece düz dosyalar (klasör yolu içeren girişler atlanır)
            string x = Path.Combine(dir, "x");
            Directory.CreateDirectory(x);
            using (var a = ZipFile.OpenRead(zip))
                foreach (var e in a.Entries)
                {
                    string name = e.FullName;
                    if (name.Length == 0 || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                    e.ExtractToFile(Path.Combine(x, name), true);
                }

            string setup = SetupPath(x);
            if (!File.Exists(setup)) throw new InvalidOperationException(L.T("Pakette kurulum programı yok.", "The package has no setup program."));
            string why;
            if (!Trusted(setup, out why))
                throw new InvalidOperationException(L.T("Kurulum programının imzası doğrulanamadı; çalıştırılmadı. ", "The setup program's signature couldn't be verified; it wasn't run. ") + why);
            if (!knownPack) Trace.WriteLine("VB-CABLE: yeni paket sürümü, imza ile doğrulandı " + sha);
            return x;
        }

        public static string SetupPath(string dir)
        {
            return Path.Combine(dir, Environment.Is64BitOperatingSystem ? "VBCABLE_Setup_x64.exe" : "VBCABLE_Setup.exe");
        }

        /// <summary>Yönetici izniyle kurulumu açar (kullanıcı "Install Driver"a basar). Çalıştırmadan hemen önce imza yeniden denetlenir.</summary>
        public static Process Run(string dir)
        {
            string setup = SetupPath(dir), why;
            if (!Trusted(setup, out why)) throw new InvalidOperationException(why);
            var psi = new ProcessStartInfo(setup);
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WorkingDirectory = dir;
            return Process.Start(psi);
        }

        public static void Cleanup(string dir)
        {
            try { if (dir != null) Directory.Delete(Path.GetDirectoryName(dir), true); } catch { }
        }

        // ------------------------------------------------------------------ Authenticode
        public static bool Trusted(string file, out string why)
        {
            int r = Verify(file);
            if (r != 0) { why = L.T("Windows imza denetimi başarısız (0x", "Windows signature check failed (0x") + r.ToString("X8") + ")."; return false; }
            try
            {
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
                if (cert.Subject.IndexOf(Signer, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    why = L.T("İmza VB-Audio'ya ait değil: ", "The signature isn't VB-Audio's: ") + cert.Subject;
                    return false;
                }
            }
            catch (Exception ex) { why = ex.Message; return false; }
            why = null;
            return true;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO
        {
            public int cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_DATA
        {
            public int cbStruct;
            public IntPtr pPolicyCallbackData, pSIPClientData;
            public int dwUIChoice, fdwRevocationChecks, dwUnionChoice;
            public IntPtr pFile;
            public int dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public int dwProvFlags, dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

        static int Verify(string file)
        {
            var fi = new WINTRUST_FILE_INFO { cbStruct = Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = file };
            IntPtr pfi = Marshal.AllocHGlobal(fi.cbStruct);
            try
            {
                Marshal.StructureToPtr(fi, pfi, false);
                var d = new WINTRUST_DATA
                {
                    cbStruct = Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = 2,              // WTD_UI_NONE
                    fdwRevocationChecks = 0,     // WTD_REVOKE_NONE
                    dwUnionChoice = 1,           // WTD_CHOICE_FILE
                    pFile = pfi,
                    dwStateAction = 0,
                    dwProvFlags = 0x1000         // WTD_CACHE_ONLY_URL_RETRIEVAL: ağa çıkmadan
                };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
                return WinVerifyTrust(IntPtr.Zero, ref action, ref d);
            }
            finally
            {
                Marshal.DestroyStructure(pfi, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(pfi);
            }
        }
    }
}
