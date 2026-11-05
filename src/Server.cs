using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace BrokenMicrophone
{
    // Bağlı bir telefon/tablet
    class Client
    {
        static int nextId;
        public readonly int Id = Interlocked.Increment(ref nextId);
        public string Name = "";          // "iPhone · Safari"
        public string Mic = "";           // seçilen mikrofonun adı (ör. AirPods)
        public int Rate;                  // örnekleme hızı
        public bool Muted;                // telefonda sessize alındı
        public volatile float Peak;       // son paketin en yüksek seviyesi (0..1)
        public DateTime LastAudio = DateTime.MinValue;
        public string Address = "";
        internal Stream Stream;
        internal readonly object WriteLock = new object();

        public bool Sending { get { return !Muted && (DateTime.Now - LastAudio).TotalMilliseconds < 1000; } }

        // Telefona metin mesajı (WebSocket metin çerçevesi)
        public void Send(string text)
        {
            var p = Encoding.UTF8.GetBytes(text);
            var f = new List<byte>(p.Length + 4);
            f.Add(0x81);
            if (p.Length < 126) f.Add((byte)p.Length);
            else { f.Add(126); f.Add((byte)(p.Length >> 8)); f.Add((byte)p.Length); }
            f.AddRange(p);
            try { lock (WriteLock) { Stream.Write(f.ToArray(), 0, f.Count); Stream.Flush(); } }
            catch { }
        }

        public void Close()
        {
            try { lock (WriteLock) Stream.Write(new byte[] { 0x88, 0 }, 0, 2); } catch { }
            try { Stream.Close(); } catch { }
        }
    }

    // Telefonun bağlandığı yerel sunucu. Tek port: https sayfa + wss ses. Düz http ile gelenler https'e yönlendirilir.
    class Server
    {
        readonly X509Certificate2 cert;
        readonly string key;
        TcpListener listener;
        volatile bool running;
        readonly List<Client> clients = new List<Client>();

        public event Action<Client> Connected, Disconnected, Changed;
        public event Action<Client, byte[], int, int> Audio;
        public int Port { get; private set; }
        public string Error;

        public Server(X509Certificate2 cert, string key) { this.cert = cert; this.key = key; }

        public List<Client> Clients { get { lock (clients) return new List<Client>(clients); } }

        public bool Start(int port)
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
                listener.Start();
            }
            catch (Exception e) { Error = e.Message; return false; }
            Port = port;
            running = true;
            var t = new Thread(AcceptLoop);
            t.IsBackground = true;
            t.Name = "Accept";
            t.Start();
            return true;
        }

        public void Stop()
        {
            running = false;
            try { listener.Stop(); } catch { }
            foreach (var c in Clients) c.Close();
        }

        void AcceptLoop()
        {
            while (running)
            {
                TcpClient tc;
                try { tc = listener.AcceptTcpClient(); }
                catch { if (!running) return; Thread.Sleep(100); continue; }
                var t = new Thread(delegate () { Handle(tc); });
                t.IsBackground = true;
                t.Start();
            }
        }

        void Handle(TcpClient tc)
        {
            Client client = null;
            try
            {
                tc.NoDelay = true;
                tc.ReceiveTimeout = 15000;
                var sock = tc.Client;
                var first = new byte[1];
                if (sock.Receive(first, SocketFlags.Peek) < 1) return;
                Stream s = tc.GetStream();
                bool tls = first[0] == 0x16;    // TLS el sıkışmasının ilk baytı
                if (tls)
                {
                    var ssl = new SslStream(s, false);
                    ssl.AuthenticateAsServer(cert, false, SslProtocols.Tls12, false);
                    s = ssl;
                }

                var req = ReadRequest(s);
                if (req == null) return;
                string path = req.Path;

                if (!tls)
                {
                    // telefon adresi https olmadan yazdıysa
                    string host = req.Header("host");
                    if (host == "") host = ((IPEndPoint)sock.LocalEndPoint).Address + ":" + Port;
                    Respond(s, "301 Moved Permanently", "text/plain", Encoding.UTF8.GetBytes("https://" + host + path), "Location: https://" + host + path + "\r\n");
                    return;
                }

                int q = path.IndexOf('?');
                string route = q < 0 ? path : path.Substring(0, q);
                string query = q < 0 ? "" : path.Substring(q + 1);

                if (route == "/ws" && req.Header("upgrade").ToLowerInvariant() == "websocket")
                {
                    if (Query(query, "k") != key) { Respond(s, "403 Forbidden", "text/plain", Encoding.UTF8.GetBytes("bad key"), ""); return; }
                    string accept = Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(req.Header("sec-websocket-key") + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                    var hs = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                    s.Write(hs, 0, hs.Length);
                    s.Flush();
                    tc.ReceiveTimeout = 20000;  // telefon her 5 sn'de en az bir mesaj gönderir
                    client = new Client();
                    client.Stream = s;
                    client.Address = ((IPEndPoint)sock.RemoteEndPoint).Address.ToString();
                    client.Name = DeviceName(req.Header("user-agent"));
                    lock (clients) clients.Add(client);
                    Fire(Connected, client);
                    WebSocketLoop(client, s);
                    return;
                }

                // sayfa dosyaları
                string file = route == "/" ? "index.html" : route.TrimStart('/');
                string type;
                switch (Path.GetExtension(file))
                {
                    case ".html": type = "text/html; charset=utf-8"; break;
                    case ".js": type = "text/javascript; charset=utf-8"; break;
                    case ".json": type = "application/manifest+json"; break;
                    case ".png": type = "image/png"; break;
                    default: type = null; break;
                }
                byte[] body = type == null || file.Contains("/") ? null : Resource("web." + file);
                if (file == "icon.png") body = Resource("logo.png");
                if (body == null) { Respond(s, "404 Not Found", "text/plain", Encoding.UTF8.GetBytes("not found"), ""); return; }
                Respond(s, "200 OK", type, body, "Cache-Control: no-cache\r\n");
            }
            catch { }
            finally
            {
                if (client != null)
                {
                    lock (clients) clients.Remove(client);
                    Fire(Disconnected, client);
                }
                try { tc.Close(); } catch { }
            }
        }

        void Fire(Action<Client> e, Client c) { if (e != null) try { e(c); } catch { } }

        // ------------------------------------------------------------------ WebSocket
        void WebSocketLoop(Client c, Stream s)
        {
            var head = new byte[14];
            byte[] payload = new byte[65536];
            var json = new JavaScriptSerializer();
            while (running)
            {
                if (!ReadFull(s, head, 0, 2)) return;
                int op = head[0] & 0x0F;
                bool masked = (head[1] & 0x80) != 0;
                long len = head[1] & 0x7F;
                if (len == 126) { if (!ReadFull(s, head, 2, 2)) return; len = (head[2] << 8) | head[3]; }
                else if (len == 127)
                {
                    if (!ReadFull(s, head, 2, 8)) return;
                    len = 0;
                    for (int i = 0; i < 8; i++) len = (len << 8) | head[2 + i];
                }
                if (len > 1 << 20) return;      // 1 MB'tan büyük mesaj beklenmez
                var mask = new byte[4];
                if (masked && !ReadFull(s, mask, 0, 4)) return;
                if (payload.Length < len) payload = new byte[len];
                int n = (int)len;
                if (!ReadFull(s, payload, 0, n)) return;
                if (masked) for (int i = 0; i < n; i++) payload[i] ^= mask[i & 3];

                switch (op)
                {
                    case 2:     // ses: 16 bit mono PCM
                        c.LastAudio = DateTime.Now;
                        int peak = 0;
                        for (int i = 0; i + 1 < n; i += 2)
                        {
                            int v = (short)(payload[i] | (payload[i + 1] << 8));
                            if (v < 0) v = -v;
                            if (v > peak) peak = v;
                        }
                        c.Peak = peak / 32768f;
                        var a = Audio;
                        if (a != null && c.Rate > 0) a(c, payload, 0, n);
                        break;
                    case 1:     // durum mesajı (JSON)
                        try
                        {
                            var m = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(payload, 0, n));
                            object v;
                            if (m.TryGetValue("rate", out v)) c.Rate = Math.Max(8000, Math.Min(96000, Convert.ToInt32(v)));
                            if (m.TryGetValue("mic", out v) && v != null) c.Mic = Clip(v.ToString(), 60);
                            if (m.TryGetValue("name", out v) && v != null && v.ToString() != "") c.Name = Clip(v.ToString(), 60);
                            if (m.TryGetValue("muted", out v)) c.Muted = Convert.ToBoolean(v);
                            Fire(Changed, c);
                        }
                        catch { }
                        break;
                    case 8:     // kapat
                        c.Close();
                        return;
                    case 9:     // ping -> pong
                        lock (c.WriteLock)
                        {
                            var f = new byte[2 + n];
                            f[0] = 0x8A; f[1] = (byte)n;
                            Array.Copy(payload, 0, f, 2, n);
                            if (n < 126) s.Write(f, 0, f.Length);
                        }
                        break;
                }
            }
        }

        static string Clip(string s, int max) { s = s.Trim(); return s.Length > max ? s.Substring(0, max) : s; }

        static bool ReadFull(Stream s, byte[] b, int off, int n)
        {
            while (n > 0)
            {
                int r = s.Read(b, off, n);
                if (r <= 0) return false;
                off += r; n -= r;
            }
            return true;
        }

        // ------------------------------------------------------------------ HTTP
        class Request
        {
            public string Method = "", Path = "/";
            public readonly Dictionary<string, string> Headers = new Dictionary<string, string>();
            public string Header(string k) { string v; return Headers.TryGetValue(k, out v) ? v : ""; }
        }

        static Request ReadRequest(Stream s)
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            // başlıklar 8 KB'ı geçmez; baytlar tek tek okunur ki WebSocket verisine taşılmasın
            while (sb.Length < 8192)
            {
                if (s.Read(one, 0, 1) <= 0) return null;
                sb.Append((char)one[0]);
                if (sb.Length >= 4 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\r' && sb[sb.Length - 3] == '\n' && sb[sb.Length - 4] == '\r') break;
            }
            var lines = sb.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return null;
            var parts = lines[0].Split(' ');
            if (parts.Length < 2) return null;
            var r = new Request();
            r.Method = parts[0];
            r.Path = parts[1];
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c > 0) r.Headers[lines[i].Substring(0, c).Trim().ToLowerInvariant()] = lines[i].Substring(c + 1).Trim();
            }
            return r;
        }

        static void Respond(Stream s, string status, string type, byte[] body, string extra)
        {
            var h = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
                "\r\n" + extra + "X-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n");
            s.Write(h, 0, h.Length);
            s.Write(body, 0, body.Length);
            s.Flush();
        }

        static string Query(string q, string name)
        {
            foreach (var p in q.Split('&'))
            {
                int i = p.IndexOf('=');
                if (i > 0 && p.Substring(0, i) == name) return Uri.UnescapeDataString(p.Substring(i + 1));
            }
            return "";
        }

        static readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();
        static byte[] Resource(string name)
        {
            lock (cache)
            {
                byte[] b;
                if (cache.TryGetValue(name, out b)) return b;
                using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("BrokenMicrophone." + name))
                {
                    if (st == null) return null;
                    var ms = new MemoryStream();
                    st.CopyTo(ms);
                    b = ms.ToArray();
                }
                cache[name] = b;
                return b;
            }
        }

        // Tarayıcı kimliğinden kısa cihaz adı
        static string DeviceName(string ua)
        {
            string dev = "Telefon";
            if (ua.Contains("iPhone")) dev = "iPhone";
            else if (ua.Contains("iPad") || (ua.Contains("Macintosh") && ua.Contains("Mobile"))) dev = "iPad";
            else if (ua.Contains("Android"))
            {
                // "Android 14; SM-S918B)" -> model
                int i = ua.IndexOf("Android");
                int semi = ua.IndexOf(';', i);
                int close = ua.IndexOf(')', i);
                if (semi > 0 && close > semi)
                {
                    string model = ua.Substring(semi + 1, close - semi - 1).Trim();
                    dev = model == "" || model == "K" ? "Android" : model;
                }
                else dev = "Android";
            }
            else if (ua.Contains("Windows")) dev = "Windows";
            else if (ua.Contains("Mac OS")) dev = "Mac";
            string br = ua.Contains("Edg") ? "Edge" : ua.Contains("SamsungBrowser") ? "Samsung Internet" : ua.Contains("Firefox") || ua.Contains("FxiOS") ? "Firefox"
                      : ua.Contains("CriOS") || ua.Contains("Chrome") ? "Chrome" : ua.Contains("Safari") ? "Safari" : "";
            return br == "" ? dev : dev + " · " + br;
        }
    }
}
