using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BrokenMicrophone
{
    // "Windows ile başlat": kullanıcının kendi Run anahtarı (yönetici izni gerekmez).
    // Windows açılınca "--autostart" ile, pencere açmadan tepside başlar.
    static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "BrokenMicrophone";
        public const string Arg = "--autostart";

        static string Command { get { return "\"" + Application.ExecutablePath + "\" " + Arg; } }

        public static bool Enabled
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                        return k != null && k.GetValue(ValueName) != null;
                }
                catch { return false; }
            }
        }

        public static bool Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(ValueName, Command);
                    else k.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch { return false; }
        }

        // Program başka bir klasöre taşındıysa / güncellendiyse kayıttaki yol düzeltilir
        public static void Refresh()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    var v = k.GetValue(ValueName) as string;
                    if (v != null && v != Command) k.SetValue(ValueName, Command);
                }
            }
            catch { }
        }
    }
}
