using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BrokenMicrophone
{
    class LocalAddress
    {
        public IPAddress Ip;
        public string Adapter;      // "Wi-Fi", "Ethernet"…
        public bool Likely;         // ağ geçidi olan gerçek bir bağdaştırıcı (telefonla aynı ağda olması en olası)
        public override string ToString() { return Ip + "  ·  " + Adapter; }
    }

    static class Net
    {
        // Telefonun ulaşabileceği IPv4 adresleri; en olası olan başta
        public static List<LocalAddress> Addresses()
        {
            var list = new List<LocalAddress>();
            NetworkInterface[] nics;
            try { nics = NetworkInterface.GetAllNetworkInterfaces(); } catch { return list; }
            foreach (var n in nics)
            {
                if (n.OperationalStatus != OperationalStatus.Up) continue;
                if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                IPInterfaceProperties p;
                try { p = n.GetIPProperties(); } catch { continue; }
                bool gateway = false;
                foreach (var g in p.GatewayAddresses)
                    if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) gateway = true;
                string d = (n.Description + " " + n.Name).ToLowerInvariant();
                bool virt = d.Contains("hyper-v") || d.Contains("vethernet") || d.Contains("virtualbox") || d.Contains("vmware")
                         || d.Contains("wsl") || d.Contains("bluetooth") || d.Contains("tap-") || d.Contains("wireguard")
                         || d.Contains("zerotier") || d.Contains("tailscale") || d.Contains("npcap");
                foreach (var a in p.UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = a.Address.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue;   // APIPA: DHCP yok, telefon ulaşamaz
                    var la = new LocalAddress();
                    la.Ip = a.Address;
                    la.Adapter = n.Name;
                    la.Likely = gateway && !virt;
                    list.Add(la);
                }
            }
            list.Sort(delegate (LocalAddress x, LocalAddress y)
            {
                if (x.Likely != y.Likely) return x.Likely ? -1 : 1;
                bool xw = IsWireless(x.Adapter), yw = IsWireless(y.Adapter);
                if (xw != yw) return xw ? -1 : 1;
                return string.Compare(x.Adapter, y.Adapter, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        static bool IsWireless(string name)
        {
            string s = name.ToLowerInvariant();
            return s.Contains("wi-fi") || s.Contains("wifi") || s.Contains("wlan") || s.Contains("kablosuz") || s.Contains("wireless");
        }

        // ------------------------------------------------------------------ sertifika
        // Telefon tarayıcısı mikrofona sadece https sayfalarında izin verir. Program kendi sertifikasını bir kez
        // üretir ve saklar; telefon ilk açılışta "güvenli değil" uyarısı gösterir, kullanıcı bir kez kabul eder.
        public static X509Certificate2 Certificate()
        {
            string path = Path.Combine(Settings.Folder, "cert.pfx");
            const string pw = "broken-microphone";
            try
            {
                if (File.Exists(path))
                {
                    var c = new X509Certificate2(File.ReadAllBytes(path), pw, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
                    if (c.HasPrivateKey && c.NotAfter > DateTime.Now.AddDays(30)) return c;
                }
            }
            catch { }

            using (var rsa = RSA.Create())
            {
                rsa.KeySize = 2048;
                var req = new CertificateRequest("CN=Broken Microphone, O=HErkanuzun", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var san = new SubjectAlternativeNameBuilder();
                san.AddDnsName("localhost");
                san.AddIpAddress(IPAddress.Loopback);
                foreach (var a in Addresses()) san.AddIpAddress(a.Ip);
                req.CertificateExtensions.Add(san.Build());
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
                var eku = new OidCollection();
                eku.Add(new Oid("1.3.6.1.5.5.7.3.1"));   // sunucu kimlik doğrulaması
                req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, false));
                // Apple 825 günden uzun sertifikaları reddediyor
                using (var c = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(800)))
                {
                    var pfx = c.Export(X509ContentType.Pfx, pw);
                    try { File.WriteAllBytes(path, pfx); } catch { }
                    return new X509Certificate2(pfx, pw, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
                }
            }
        }
    }
}
