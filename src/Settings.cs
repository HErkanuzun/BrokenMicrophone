using System;
using System.IO;
using System.Security.Cryptography;

namespace BrokenMicrophone
{
    // Basit ayar dosyası: %AppData%\BrokenMicrophone\settings.txt (anahtar=değer)
    class Settings
    {
        public string Lang = "";
        public string Window = "";      // x,y,w,h,maximized
        public int Port = 47820;        // telefonun bağlandığı yerel ağ portu
        public string Key = "";         // QR koddaki erişim anahtarı; ağdaki başkaları bağlanamasın diye
        public string Output = "";      // sesin verildiği çıkış cihazı (sanal mikrofonun girişi)
        public string Address = "";     // birden fazla ağ varsa seçilen IP
        public bool Monitor = false;    // gelen sesi hoparlörden de dinle
        public bool MicNamed = false;   // sanal mikrofona bir kez "Broken Mic" adı verildi
        public bool TrayTipShown = false;   // "arka planda çalışıyor" bildirimi bir kez gösterildi

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BrokenMicrophone"); }
        }

        static string FilePath
        {
            get
            {
                Directory.CreateDirectory(Folder);
                return Path.Combine(Folder, "settings.txt");
            }
        }

        public static string NewKey()
        {
            const string abc = "abcdefghijkmnpqrstuvwxyz23456789";
            var b = new byte[10];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            var c = new char[b.Length];
            for (int i = 0; i < b.Length; i++) c[i] = abc[b[i] % abc.Length];
            return new string(c);
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                        int n;
                        switch (k)
                        {
                            case "lang": s.Lang = v; break;
                            case "window": s.Window = v; break;
                            case "port": if (int.TryParse(v, out n) && n > 1023 && n < 65536) s.Port = n; break;
                            case "key": s.Key = v; break;
                            case "output": s.Output = v; break;
                            case "address": s.Address = v; break;
                            case "monitor": s.Monitor = v == "1"; break;
                            case "micnamed": s.MicNamed = v == "1"; break;
                            case "traytip": s.TrayTipShown = v == "1"; break;
                        }
                    }
            }
            catch { }
            if (s.Key.Length < 8) { s.Key = NewKey(); s.Save(); }
            return s;
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new[] {
                    "lang=" + Lang, "window=" + Window, "port=" + Port, "key=" + Key,
                    "output=" + Output, "address=" + Address, "monitor=" + (Monitor ? "1" : "0"),
                    "micnamed=" + (MicNamed ? "1" : "0"), "traytip=" + (TrayTipShown ? "1" : "0") });
            }
            catch { }
        }
    }
}
