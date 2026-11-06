using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace BrokenMicrophone
{
    enum FwState { Unknown, Open, Blocked }

    /// <summary>
    /// Windows güvenlik duvarı: telefonun bu programa bağlanıp bağlanamayacağını okur (yönetici izni gerekmez)
    /// ve tek tıkla izin verir (Windows yönetici izni sorar). İzin sadece bu programa ve sadece yerel ağdaki
    /// cihazlara verilir; ağ "Ortak" olarak ayarlı olsa da çalışır.
    /// </summary>
    static class Firewall
    {
        const string RuleName = "Broken Microphone";
        const BindingFlags Get = BindingFlags.GetProperty;

        public static string ProfileName = "";   // şu anki ağ türü (Özel / Ortak / Etki alanı)

        static object Prop(object o, string name, params object[] args)
        {
            return o.GetType().InvokeMember(name, Get, null, o, args.Length == 0 ? null : args);
        }

        public static FwState Check(int port)
        {
            try
            {
                var t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (t == null) return FwState.Unknown;
                object pol = Activator.CreateInstance(t);
                int current = Convert.ToInt32(Prop(pol, "CurrentProfileTypes"));
                ProfileName = (current & 4) != 0 ? L.T("Ortak", "Public") : (current & 2) != 0 ? L.T("Özel", "Private") : (current & 1) != 0 ? L.T("Etki alanı", "Domain") : "";
                string exe = Path.GetFullPath(Application.ExecutablePath);

                int allow = 0, block = 0, active = 0;
                foreach (int p in new[] { 1, 2, 4 })
                    if ((current & p) != 0 && Convert.ToBoolean(Prop(pol, "FirewallEnabled", p))) active |= p;
                if (active == 0) return FwState.Open;

                foreach (object r in (IEnumerable)Prop(pol, "Rules"))
                {
                    try
                    {
                        if (Convert.ToInt32(Prop(r, "Direction")) != 1 || !Convert.ToBoolean(Prop(r, "Enabled"))) continue;
                        int profiles = Convert.ToInt32(Prop(r, "Profiles"));
                        if ((profiles & active) == 0) continue;
                        string app = Prop(r, "ApplicationName") as string;
                        bool ours;
                        if (!string.IsNullOrEmpty(app))
                        {
                            try { ours = string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(app)), exe, StringComparison.OrdinalIgnoreCase); }
                            catch { ours = false; }
                        }
                        else
                        {
                            // programa bağlı olmayan, portu açan kural
                            int proto = Convert.ToInt32(Prop(r, "Protocol"));
                            string ports = Prop(r, "LocalPorts") as string ?? "";
                            ours = (proto == 6 || proto == 256) && PortMatches(ports, port);
                        }
                        if (!ours) continue;
                        if (Convert.ToInt32(Prop(r, "Action")) == 1) allow |= profiles & active;
                        else if (!string.IsNullOrEmpty(app)) block |= profiles & active;
                    }
                    catch { }
                }
                // engelleme kuralı izni ezer
                return block == 0 && (allow & active) == active ? FwState.Open : FwState.Blocked;
            }
            catch { return FwState.Unknown; }
        }

        static bool PortMatches(string ports, int port)
        {
            if (ports == "*") return true;
            foreach (var part in ports.Split(','))
            {
                var s = part.Trim();
                int dash = s.IndexOf('-');
                int a, b;
                if (dash > 0 && int.TryParse(s.Substring(0, dash), out a) && int.TryParse(s.Substring(dash + 1), out b)) { if (port >= a && port <= b) return true; }
                else if (int.TryParse(s, out a) && a == port) return true;
            }
            return false;
        }

        /// <summary>
        /// Bu programın eski gelen bağlantı kurallarını (Windows'un kendi eklediği engelleme kuralları dahil) silip
        /// tek bir izin kuralı ekler. Hata yoksa null, varsa açıklama döndürür.
        /// </summary>
        public static string Allow(int port)
        {
            string exe = Path.GetFullPath(Application.ExecutablePath);
            if (exe.IndexOf('"') >= 0) return "path";
            string args = "/c netsh advfirewall firewall delete rule name=all dir=in program=\"" + exe + "\" >nul 2>&1 & " +
                          "netsh advfirewall firewall delete rule name=\"" + RuleName + "\" >nul 2>&1 & " +
                          "netsh advfirewall firewall add rule name=\"" + RuleName + "\" dir=in action=allow program=\"" + exe +
                          "\" enable=yes profile=any remoteip=localsubnet protocol=TCP localport=" + port;
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", args);
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.UseShellExecute = true;
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(30000)) return L.T("Zaman aşımı.", "Timed out.");
                    return p.ExitCode == 0 ? null : L.T("Kural eklenemedi (kod ", "Couldn't add the rule (code ") + p.ExitCode + ").";
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return L.T("Yönetici izni verilmedi.", "Administrator permission was not given.");
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
