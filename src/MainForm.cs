using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace BrokenMicrophone
{
    class MainForm : Form
    {
        readonly Settings cfg;
        readonly BufferedPanel page = new BufferedPanel();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        public bool ExitRequested;

        Server server;
        List<LocalAddress> addresses = new List<LocalAddress>();
        List<OutputDevice> outputs = new List<OutputDevice>();

        // ses yolu (ağ iş parçacıkları da kullanır)
        readonly object audioLock = new object();
        Player player, monitor;
        int activeId;                   // sesi verilen telefon
        OutputDevice output;            // seçili çıkış
        string playerError;

        PictureBox logo;
        Label lblTitle, lblSub, secPhone, secOut, secDevices, lblSteps, lblOutHint, lblFoot;
        Seg segLang;
        FlatButton btnAbout, btnCopy, btnHelp, btnDriver;
        StatusBox info;
        QrView qr;
        DarkCombo cmbAddress, cmbOutput;
        LevelMeter meter;
        ClientList list;
        Toggle tglMonitor;

        public MainForm(Settings cfg)
        {
            this.cfg = cfg;
            Text = AppInfo.Name + " " + AppInfo.Version;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            AutoScaleMode = AutoScaleMode.None;
            Icon = Theme.AppIcon(32);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            using (var g = CreateGraphics()) Theme.S = Math.Max(1f, g.DpiX / 96f);

            page.Dock = DockStyle.Fill;
            page.BackColor = Theme.Bg;
            page.AutoScroll = true;
            page.Resize += delegate { DoLayout(); };
            Controls.Add(page);

            addresses = Net.Addresses();
            outputs = Player.Devices();
            output = PickOutput();

            StartServer();
            BuildUi();
            InitialSize();

            tick.Interval = 80;
            tick.Tick += delegate { OnTick(); };
            tick.Start();
        }

        static int P(float v) { return Theme.P(v); }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(this); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ExitRequested = true;
            SavePlacement();
            tick.Stop();
            if (server != null) server.Stop();
            lock (audioLock)
            {
                if (player != null) { player.Dispose(); player = null; }
                if (monitor != null) { monitor.Dispose(); monitor = null; }
            }
            base.OnFormClosing(e);
        }

        public void RequestExit() { ExitRequested = true; Close(); }

        public void BringToFront2()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            BringToFront();
        }

        void InitialSize()
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            var frame = SizeFromClientSize(Size.Empty);
            MinimumSize = SizeFromClientSize(new Size(P(720), P(560)));
            ClientSize = new Size(Math.Min(P(940), wa.Width - frame.Width), Math.Min(P(740), wa.Height - frame.Height));
            try
            {
                var p = cfg.Window.Split(',');
                if (p.Length == 5)
                {
                    var r = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
                    bool visible = false;
                    foreach (var sc in Screen.AllScreens)
                        if (sc.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, Math.Max(1, r.Width - 80), 40))) visible = true;
                    if (visible && r.Width >= MinimumSize.Width && r.Height >= MinimumSize.Height)
                    {
                        StartPosition = FormStartPosition.Manual;
                        Bounds = r;
                        if (p[4] == "1") WindowState = FormWindowState.Maximized;
                    }
                }
            }
            catch { }
        }

        void SavePlacement()
        {
            var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            cfg.Window = r.X + "," + r.Y + "," + r.Width + "," + r.Height + "," + (WindowState == FormWindowState.Maximized ? "1" : "0");
            cfg.Save();
        }

        // ------------------------------------------------------------------ sunucu ve ses
        void StartServer()
        {
            System.Security.Cryptography.X509Certificates.X509Certificate2 cert;
            try { cert = Net.Certificate(); }
            catch (Exception ex) { playerError = L.T("Güvenlik sertifikası oluşturulamadı: ", "Couldn't create the security certificate: ") + ex.Message; return; }
            server = new Server(cert, cfg.Key);
            server.Connected += delegate (Client c) { SafeUi(delegate { OnClientsChanged(); }); };
            server.Disconnected += delegate (Client c)
            {
                lock (audioLock) if (activeId == c.Id) activeId = 0;
                SafeUi(delegate { OnClientsChanged(); });
            };
            server.Changed += delegate (Client c) { SafeUi(delegate { list.Invalidate(); }); };
            server.Audio += OnAudio;
            // port doluysa sıradaki birkaç port denenir
            for (int i = 0; i < 10; i++)
                if (server.Start(cfg.Port + i))
                {
                    if (i > 0) { cfg.Port += i; cfg.Save(); }
                    return;
                }
            playerError = L.T("Bağlantı portu açılamadı: ", "Couldn't open the connection port: ") + server.Error;
            server = null;
        }

        void SafeUi(MethodInvoker m)
        {
            if (ExitRequested || IsDisposed) return;
            try { BeginInvoke(m); } catch { }
        }

        // Ağ iş parçacığında: sadece etkin telefonun sesi çıkışa gider. Etkin telefon susup başka biri
        // konuşmaya başlarsa ses ona geçer (iki telefonun sesi karışmaz).
        void OnAudio(Client c, byte[] buf, int off, int n)
        {
            lock (audioLock)
            {
                if (activeId != c.Id)
                {
                    Client active = null;
                    if (activeId != 0 && server != null) foreach (var x in server.Clients) if (x.Id == activeId) active = x;
                    if (active != null && active.Sending) return;
                    activeId = c.Id;
                    SafeUi(delegate { list.ActiveId = activeId; list.Invalidate(); });
                }
                if (c.Muted) return;
                try
                {
                    if (player == null || player.Rate != c.Rate)
                    {
                        if (player != null) player.Dispose();
                        player = null;
                        player = new Player(output == null ? -1 : output.Id, c.Rate);
                        playerError = null;
                    }
                    player.Write(buf, off, n);
                    if (cfg.Monitor && output != null && output.Virtual)
                    {
                        if (monitor == null || monitor.Rate != c.Rate)
                        {
                            if (monitor != null) monitor.Dispose();
                            monitor = new Player(-1, c.Rate);
                        }
                        monitor.Write(buf, off, n);
                    }
                    else if (monitor != null) { monitor.Dispose(); monitor = null; }
                }
                catch (Exception ex)
                {
                    if (playerError == null) playerError = ex.Message;
                }
            }
        }

        void ResetPlayers()
        {
            lock (audioLock)
            {
                if (player != null) { player.Dispose(); player = null; }
                if (monitor != null) { monitor.Dispose(); monitor = null; }
                playerError = null;
            }
        }

        OutputDevice PickOutput()
        {
            foreach (var d in outputs) if (d.Name == cfg.Output) return d;
            foreach (var d in outputs) if (d.Virtual) return d;
            return null;   // Windows'un varsayılan çıkışı
        }

        bool HasVirtual { get { foreach (var d in outputs) if (d.Virtual) return true; return false; } }

        string Url
        {
            get
            {
                if (server == null || addresses.Count == 0) return "";
                var a = SelectedAddress;
                return "https://" + a.Ip + ":" + server.Port + "/#k=" + cfg.Key;
            }
        }

        LocalAddress SelectedAddress
        {
            get
            {
                foreach (var a in addresses) if (a.Ip.ToString() == cfg.Address) return a;
                return addresses.Count > 0 ? addresses[0] : null;
            }
        }

        // ------------------------------------------------------------------ arayüz
        T Add<T>(T c) where T : Control
        {
            if (c.Font == DefaultFont || !(c is Label)) c.Font = Font;
            page.Controls.Add(c);
            return c;
        }

        FlatButton Btn(string text, BtnKind kind, EventHandler click)
        {
            var b = Add(new FlatButton(text, kind));
            b.Click += click;
            return b;
        }

        Label Text2(string text, Color color, float size, FontStyle style)
        {
            var l = new Label();
            l.Text = text;
            l.ForeColor = color;
            l.BackColor = Theme.Bg;
            l.Font = new Font(Font.FontFamily, size, style);
            l.AutoSize = true;
            page.Controls.Add(l);
            return l;
        }

        Label Section(string text) { return Text2(L.Upper(text), Theme.Sub, 8f, FontStyle.Bold); }

        void BuildUi()
        {
            SuspendLayout();
            Font = new Font("Segoe UI", 9.5f);

            logo = new PictureBox();
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.BackColor = Theme.Bg;
            logo.Image = Theme.Logo();
            page.Controls.Add(logo);

            lblTitle = Text2(AppInfo.Name, Theme.Text, 15f, FontStyle.Bold);
            lblSub = Text2(L.T("Telefonunu ya da tabletini bilgisayarın mikrofonu yap.", "Use your phone or tablet as the computer's microphone."), Theme.Sub, 9.5f, FontStyle.Regular);

            segLang = Add(new Seg());
            segLang.Items = new[] { "TR", "EN" };
            segLang.Font = new Font("Segoe UI", 8.5f);
            segLang.Selected = Math.Max(0, Array.IndexOf(L.Codes, L.Lang));
            segLang.Changed += delegate (int i)
            {
                cfg.Lang = L.Codes[i];
                cfg.Save();
                L.Set(cfg.Lang);
                BeginInvoke((MethodInvoker)RebuildUi);
            };
            btnAbout = Btn(L.T("Hakkında", "About"), BtnKind.Ghost, delegate { using (var a = new AboutDialog()) a.ShowDialog(this); });

            info = Add(new StatusBox());
            btnDriver = Btn(L.T("Sanal mikrofonu kur…", "Install a virtual microphone…"), BtnKind.Primary, delegate { ShowDriverHelp(); });

            // sol: telefon
            secPhone = Section(L.T("Telefonu bağla", "Connect your phone"));
            qr = Add(new QrView());
            qr.Font = new Font(Font.FontFamily, 9f);
            cmbAddress = Add(new DarkCombo());
            foreach (var a in addresses) cmbAddress.Items.Add(a);
            var sel = SelectedAddress;
            if (sel != null) cmbAddress.SelectedItem = sel;
            cmbAddress.SelectedIndexChanged += delegate
            {
                var a = cmbAddress.SelectedItem as LocalAddress;
                if (a == null) return;
                cfg.Address = a.Ip.ToString();
                cfg.Save();
                UpdateUrl();
            };
            btnCopy = Btn(L.T("Adresi kopyala", "Copy address"), BtnKind.Secondary, delegate
            {
                try { Clipboard.SetText(Url); btnCopy.Text = L.T("Kopyalandı", "Copied"); } catch { }
            });
            btnHelp = Btn(L.T("Bağlanamıyor musun?", "Can't connect?"), BtnKind.Ghost, delegate { ShowConnectHelp(); });
            lblSteps = Text2(L.T(
                "1. Telefon bilgisayarla aynı Wi-Fi ağında olsun.\n2. Telefonun kamerasıyla QR kodu okut.\n3. \"Bağlantı gizli değil\" uyarısında Ayrıntılar → Siteyi ziyaret et (sadece ilk seferde).\n4. Mikrofon düğmesine bas ve izin ver.",
                "1. Put the phone on the same Wi-Fi as the computer.\n2. Scan the QR code with the phone's camera.\n3. At the \"connection is not private\" warning choose Details → Visit website (first time only).\n4. Tap the microphone button and allow access."),
                Theme.Sub, 9f, FontStyle.Regular);

            // sağ: ses
            secOut = Section(L.T("Ses nereye gitsin", "Where the audio goes"));
            cmbOutput = Add(new DarkCombo());
            FillOutputs();
            cmbOutput.SelectedIndexChanged += delegate
            {
                var d = cmbOutput.SelectedItem as OutputDevice;
                output = d != null && d.Id >= 0 ? d : null;
                cfg.Output = output == null ? "" : output.Name;
                cfg.Save();
                ResetPlayers();
                UpdateUi();
            };
            lblOutHint = Text2("", Theme.Sub, 9f, FontStyle.Regular);
            meter = Add(new LevelMeter());
            tglMonitor = Add(new Toggle(L.T("Gelen sesi hoparlörden de dinle", "Also play the audio on the speakers")));
            tglMonitor.Checked = cfg.Monitor;
            tglMonitor.Changed += delegate (bool on) { cfg.Monitor = on; cfg.Save(); };

            secDevices = Section(L.T("Bağlı cihazlar", "Connected devices"));
            list = Add(new ClientList());
            list.ActiveId = activeId;
            list.Picked += delegate (Client c)
            {
                lock (audioLock) activeId = c.Id;
                list.ActiveId = c.Id;
                list.Invalidate();
            };
            if (server != null) list.Items = server.Clients;

            lblFoot = Text2(L.T("Ses sadece yerel ağda, bilgisayarla telefon arasında gider; internete çıkmaz. QR koddaki anahtarı bilmeyen kimse bağlanamaz.",
                                "Audio only travels between the phone and the computer on your local network; nothing goes to the internet. Nobody can connect without the key in the QR code."),
                            Theme.Sub, 8.5f, FontStyle.Regular);

            UpdateUrl();
            UpdateUi();
            ResumeLayout();
            DoLayout();
        }

        void FillOutputs()
        {
            cmbOutput.Items.Clear();
            var def = new OutputDevice();
            def.Id = -1;
            def.Name = L.T("Windows'un varsayılan hoparlörü", "Windows default speakers");
            cmbOutput.Items.Add(def);
            foreach (var d in outputs) cmbOutput.Items.Add(d);
            object pick = def;
            foreach (OutputDevice d in cmbOutput.Items) if (output != null && d.Id == output.Id && d.Name == output.Name) pick = d;
            cmbOutput.SelectedItem = pick;
        }

        void RebuildUi()
        {
            SuspendLayout();
            var old = new List<Control>();
            foreach (Control c in page.Controls) old.Add(c);
            page.Controls.Clear();
            foreach (var c in old) c.Dispose();
            BuildUi();
            ResumeLayout();
        }

        void DoLayout()
        {
            if (lblFoot == null) return;
            int W = page.ClientSize.Width;
            int pad = P(24), x = pad, w = W - pad * 2, y = pad - page.VerticalScroll.Value;

            logo.SetBounds(x, y, P(48), P(48));
            lblTitle.Location = new Point(x + P(62), y - P(1));
            lblSub.Location = new Point(x + P(64), y + P(30));
            int bx = x + w;
            btnAbout.SetBounds(bx - P(90), y + P(6), P(90), P(32));
            bx -= P(90) + P(8);
            segLang.SetBounds(bx - P(96), y + P(6), P(96), P(32));
            y += P(70);

            int infoW = btnDriver.Visible ? w - P(250) : w;
            info.SetBounds(x, y, infoW, Math.Max(P(46), info.Measure(infoW)));
            if (btnDriver.Visible) btnDriver.SetBounds(x + w - P(238), y + (info.Height - P(40)) / 2, P(238), P(40));
            y = info.Bottom + P(20);

            // iki sütun: solda QR, sağda ses
            int leftW = P(300), gap = P(24), rightX = x + leftW + gap, rightW = w - leftW - gap;
            int top = y;

            secPhone.Location = new Point(x, y);
            y += P(24);
            qr.SetBounds(x, y, leftW, leftW + P(26));
            y = qr.Bottom + P(10);
            cmbAddress.Visible = addresses.Count > 1;
            if (cmbAddress.Visible) { cmbAddress.SetBounds(x, y, leftW, P(30)); y = cmbAddress.Bottom + P(10); }
            btnCopy.SetBounds(x, y, (leftW - P(8)) / 2, P(36));
            btnHelp.SetBounds(x + (leftW + P(8)) / 2, y, (leftW - P(8)) / 2, P(36));
            y = btnCopy.Bottom + P(12);
            lblSteps.MaximumSize = new Size(leftW, 0);
            lblSteps.Location = new Point(x, y);
            int leftBottom = y + lblSteps.Height;

            y = top;
            secOut.Location = new Point(rightX, y);
            y += P(24);
            cmbOutput.SetBounds(rightX, y, rightW, P(30));
            y = cmbOutput.Bottom + P(8);
            lblOutHint.MaximumSize = new Size(rightW, 0);
            lblOutHint.Location = new Point(rightX, y);
            y = lblOutHint.Bottom + P(12);
            meter.SetBounds(rightX, y, rightW, P(12));
            y = meter.Bottom + P(12);
            tglMonitor.SetBounds(rightX, y, rightW, P(30));
            tglMonitor.Visible = output != null && output.Virtual;
            if (tglMonitor.Visible) y = tglMonitor.Bottom + P(14); else y += P(4);

            secDevices.Location = new Point(rightX, y);
            y += P(24);
            list.SetBounds(rightX, y, rightW, list.Measure());
            int rightBottom = list.Bottom;

            y = Math.Max(leftBottom, rightBottom) + P(22);
            lblFoot.MaximumSize = new Size(w, 0);
            lblFoot.Location = new Point(x, y);
            page.AutoScrollMinSize = new Size(0, y + lblFoot.Height + pad + page.VerticalScroll.Value);
            page.Invalidate();
        }

        void UpdateUrl()
        {
            qr.Url = Url;
            var a = SelectedAddress;
            qr.Caption = a == null || server == null ? L.T("Ağ bağlantısı yok", "No network connection") : a.Ip + ":" + server.Port;
            qr.Invalidate();
        }

        // ------------------------------------------------------------------ durum
        void OnClientsChanged()
        {
            if (server == null) return;
            list.Items = server.Clients;
            lock (audioLock)
            {
                if (activeId == 0 && list.Items.Count > 0) activeId = list.Items[list.Items.Count - 1].Id;
                list.ActiveId = activeId;
            }
            if (list.Items.Count == 0) ResetPlayers();
            UpdateUi();
            DoLayout();
        }

        Client Active
        {
            get
            {
                if (server == null) return null;
                foreach (var c in list.Items) if (c.Id == activeId) return c;
                return null;
            }
        }

        string MicName
        {
            get
            {
                if (output == null || !output.Virtual) return null;
                string n = output.Name.ToLowerInvariant();
                if (n.Contains("broken microphone")) return "Broken Microphone";
                if (n.Contains("cable")) return "CABLE Output (VB-Audio Virtual Cable)";
                return output.Name;
            }
        }

        void UpdateUi()
        {
            string s;
            StatusKind kind;
            var a = Active;
            if (server == null)
            {
                s = playerError ?? L.T("Bağlantı başlatılamadı.", "Couldn't start the connection.");
                kind = StatusKind.Warn;
            }
            else if (addresses.Count == 0)
            {
                s = L.T("Bilgisayar bir ağa bağlı değil. Telefonla aynı Wi-Fi ağına bağlan.", "The computer isn't on a network. Connect it to the same Wi-Fi as the phone.");
                kind = StatusKind.Warn;
            }
            else if (a == null)
            {
                s = L.T("Telefon bekleniyor. Aşağıdaki QR kodu telefonun kamerasıyla okut.", "Waiting for a phone. Scan the QR code below with the phone's camera.");
                kind = StatusKind.Idle;
            }
            else if (a.Muted)
            {
                s = string.Format(L.T("{0} bağlı ama telefonda sessize alınmış.", "{0} is connected but muted on the phone."), a.Name);
                kind = StatusKind.Warn;
            }
            else if (!a.Sending)
            {
                s = string.Format(L.T("{0} bağlı. Telefonda mikrofon düğmesine bas.", "{0} is connected. Tap the microphone button on the phone."), a.Name);
                kind = StatusKind.Idle;
            }
            else
            {
                s = string.Format(L.T("{0} mikrofon olarak çalışıyor.", "{0} is working as a microphone."), a.Name + (a.Mic != "" ? " (" + a.Mic + ")" : ""));
                kind = StatusKind.Active;
            }
            if (server != null && playerError != null) { s += "\n" + L.T("Ses çıkışı: ", "Audio output: ") + playerError; kind = StatusKind.Warn; }
            if (server != null && !HasVirtual)
            {
                s += "\n" + L.T("Sanal mikrofon sürücüsü kurulu değil: ses şimdilik sadece hoparlörden duyulur, başka programlarda mikrofon olarak görünmez.",
                                "No virtual microphone driver is installed: for now the audio only plays on the speakers and doesn't show up as a microphone in other apps.");
                if (kind == StatusKind.Idle) kind = StatusKind.Warn;
            }
            bool layout = false;
            if (info.Text != s) { info.Text = s; layout = true; }
            info.Kind = kind;
            info.Invalidate();
            bool showDriver = !HasVirtual;
            if (btnDriver.Visible != showDriver) { btnDriver.Visible = showDriver; layout = true; }

            string hint;
            string mic = MicName;
            if (mic != null)
                hint = string.Format(L.T("Discord, Zoom, Teams, OBS ya da oyunda mikrofon olarak \"{0}\" seç.", "In Discord, Zoom, Teams, OBS or your game, pick \"{0}\" as the microphone."), mic);
            else
                hint = L.T("Bu bir hoparlör: sesi sadece duyarsın. Başka programlarda mikrofon olarak kullanmak için sanal mikrofonun girişini seç.",
                           "This is a speaker: you only hear the audio. To use it as a microphone in other apps, choose a virtual microphone's input.");
            if (lblOutHint.Text != hint) { lblOutHint.Text = hint; layout = true; }
            if (tglMonitor.Visible != (output != null && output.Virtual)) layout = true;
            if (layout) DoLayout();
        }

        int slow;
        void OnTick()
        {
            var a = Active;
            float lv = 0;
            if (a != null && a.Sending) lv = a.Peak;
            meter.Muted = a != null && a.Muted;
            meter.Push(lv);
            if (++slow % 6 == 0)
            {
                // bağlantı olayı pencere hazır olmadan geldiyse kaçmasın
                if (server != null && !SameClients(server.Clients, list.Items)) OnClientsChanged();
                list.Invalidate();
                UpdateUi();
            }
            if (slow % 60 == 0) btnCopy.Text = L.T("Adresi kopyala", "Copy address");
            // ağ değişti mi (ör. Wi-Fi'ye yeni bağlandı)
            if (slow % 75 == 0)
            {
                var now = Net.Addresses();
                if (Signature(now) != Signature(addresses))
                {
                    addresses = now;
                    cmbAddress.Items.Clear();
                    foreach (var x in addresses) cmbAddress.Items.Add(x);
                    var sel = SelectedAddress;
                    if (sel != null) cmbAddress.SelectedItem = sel;
                    UpdateUrl();
                    DoLayout();
                }
            }
        }

        static bool SameClients(List<Client> a, List<Client> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].Id != b[i].Id) return false;
            return true;
        }

        static string Signature(List<LocalAddress> l)
        {
            var s = "";
            foreach (var a in l) s += a.Ip + ";";
            return s;
        }

        // ------------------------------------------------------------------ yardım
        void ShowConnectHelp()
        {
            using (var d = new ConnectHelpDialog(cfg.Port)) d.ShowDialog(this);
        }

        void ShowDriverHelp()
        {
            using (var d = new DriverDialog()) d.ShowDialog(this);
            // kurulumdan sonra cihaz listesi tazelenir
            outputs = Player.Devices();
            output = PickOutput();
            FillOutputs();
            ResetPlayers();
            UpdateUi();
            DoLayout();
        }
    }
}
