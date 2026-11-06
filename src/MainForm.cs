using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace BrokenMicrophone
{
    /// <summary>
    /// Ana pencere. Telefon uygulamasıyla aynı düzen: altta sekme çubuğu (Mikrofon · Bağlan · Ses · Ayarlar),
    /// her sekme ayrı sayfa, büyük başlık, cam kartlar.
    /// </summary>
    class MainForm : Form, ISurface
    {
        readonly Settings cfg;
        readonly GlassPage page = new GlassPage();
        readonly TabBar bar = new TabBar();
        readonly TitleBar titleBar = new TitleBar();
        readonly AppHeader header = new AppHeader();
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

        // güvenlik duvarı (arka planda okunur)
        volatile FwState fw = FwState.Unknown;
        int fwChecking;
        string fwMessage;

        // sekmeler
        const int TabMic = 0, TabConnect = 1, TabAudio = 2, TabSettings = 3;
        int tab;
        readonly List<Control>[] groups = { new List<Control>(), new List<Control>(), new List<Control>(), new List<Control>() };
        int building = -1;
        readonly Label[] titles = new Label[4], subs = new Label[4];

        // Mikrofon
        MicOrb orb;
        Label lblState, lblState2, secDevices;
        LevelMeter meter;
        ClientList list;
        FlatButton btnGoConnect;
        readonly Label[] alertText = new Label[4];
        readonly FlatButton[] alertBtn = new FlatButton[4];
        readonly Action[] alertAct = new Action[4];
        int alertCount;

        // Bağlan
        QrView qr;
        Picker pickAddress;
        FlatButton btnCopy, btnHelp, btnAndroid;
        Label secSteps, lblSteps;

        // Ses
        Label secOut, lblOutHint, secMon, secVirt, lblVirt;
        Picker pickOutput;
        Toggle tglMonitor;
        LevelMeter meter2;
        FlatButton btnDriver, btnRename;
        Label lblDiag, lblMicName;
        TextField txtMicName;
        MicName.Endpoint micEp;           // sanal mikrofonun kayıt ucu (Discord'da görünen ad)
        string renameMsg;
        NotifyIcon tray;
        bool background;                  // pencere kapalı, uygulama tepside çalışıyor

        // Ayarlar
        Label secLang, secNet, lblFw, secAbout, lblApp, lblFoot, secStart, lblStart;
        Toggle tglStart;
        Seg segLang;
        FlatButton btnFw, btnHelp2, btnAbout;
        PictureBox logo;

        public MainForm(Settings cfg)
        {
            this.cfg = cfg;
            Text = AppInfo.Name + " " + AppInfo.Version;
            BackColor = Glass.Bg;
            ForeColor = Theme.Text;
            AutoScaleMode = AutoScaleMode.None;
            Icon = Theme.AppIcon(32);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            DoubleBuffered = true;
            using (var g = CreateGraphics()) Theme.S = Math.Max(1f, g.DpiX / 96f);

            page.Dock = DockStyle.Fill;
            bar.Dock = DockStyle.Bottom;
            bar.Height = P(84);
            titleBar.Dock = DockStyle.Top;
            Controls.Add(page);
            Controls.Add(bar);
            header.Dock = DockStyle.Top;
            Controls.Add(header);
            titleBar.ShowTitle = false;
            Controls.Add(titleBar);
            page.Resize += delegate { DoLayout(); };
            page.Scrolled += delegate { DoLayout(); };
            bar.Changed += delegate (int i) { ShowTab(i); };

            addresses = Net.Addresses();
            outputs = Player.Devices();
            output = PickOutput();

            StartServer();
            BuildUi();
            InitialSize();
            CheckFirewall();
            SetupTray();
            DefaultMicName();

            tick.Interval = 80;
            tick.Tick += delegate { OnTick(); };
            tick.Start();
        }

        static int P(float v) { return Theme.P(v); }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Frame.Apply(this); }
        protected override void WndProc(ref Message m) { if (!Frame.Handle(this, ref m, titleBar.Height + header.Height)) base.WndProc(ref m); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); titleBar.Invalidate(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); titleBar.Invalidate(); }

        public void PaintSurface(Graphics g, Rectangle r) { Glass.Backdrop(g, ClientSize, Point.Empty, r); }
        protected override void OnPaintBackground(PaintEventArgs e) { PaintSurface(e.Graphics, e.ClipRectangle); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(true); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // X: uygulama kapanmaz, tepsiye iner ve telefondan gelen ses akmaya devam eder
            if (!ExitRequested && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                GoBackground();
                return;
            }
            ExitRequested = true;
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
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
            if (background) LeaveBackground();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            BringToFront();
        }

        // ------------------------------------------------------------------ tepsi (arka plan)
        void SetupTray()
        {
            tray = new NotifyIcon();
            tray.Icon = Theme.AppIcon(16);
            tray.Text = AppInfo.Name;
            var menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false };
            menu.ShowImageMargin = false;
            menu.BackColor = Color.FromArgb(27, 31, 39);
            menu.ForeColor = Theme.Text;
            menu.Font = Font;
            var open = menu.Items.Add(L.T("Aç", "Open"));
            open.Font = new Font(Font, FontStyle.Bold);
            open.Click += delegate { BringToFront2(); };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Çıkış (mikrofonu kapat)", "Exit (turn the microphone off)")).Click += delegate { RequestExit(); };
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) BringToFront2(); };
            tray.Visible = true;
        }

        // Windows ile başlarken pencere hiç gösterilmez, doğrudan tepside çalışır
        bool startHidden;
        public void StartHidden() { startHidden = true; }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && value)
            {
                startHidden = false;
                if (!IsHandleCreated) CreateHandle();   // diğer kopyadan gelen "göster" sinyali için gerekli
                base.SetVisibleCore(false);
                GoBackground(false);
                return;
            }
            base.SetVisibleCore(value);
        }

        void GoBackground() { GoBackground(true); }

        void GoBackground(bool closed)
        {
            if (closed) SavePlacement();
            background = true;
            Hide();
            tick.Interval = 1000;   // arayüz çizilmez; yalnızca bağlantı, ağ ve güvenlik duvarı kontrolü (sesi etkilemez)
            UpdateTray();
            if (closed && !cfg.TrayTipShown && tray != null)
            {
                cfg.TrayTipShown = true;
                cfg.Save();
                tray.ShowBalloonTip(5000, AppInfo.Name,
                    L.T("Arka planda çalışmaya devam ediyor; mikrofon açık kalır. Tamamen kapatmak için simgeye sağ tıkla → Çıkış.",
                        "Still running in the background; the microphone stays on. To quit, right-click the icon → Exit."), ToolTipIcon.Info);
            }
            TrimMemory();
        }

        void LeaveBackground()
        {
            background = false;
            tick.Interval = 80;
            UpdateUi();
            DoLayout();
        }

        void UpdateTray()
        {
            if (tray == null) return;
            var a = Active;
            string t = a != null ? L.T("Mikrofon açık: ", "Microphone on: ") + a.Name
                     : server != null && server.Clients.Count > 0 ? L.T("Telefon bağlı", "Phone connected")
                     : L.T("Telefon bekleniyor", "Waiting for the phone");
            t = AppInfo.Name + "\n" + t;
            if (t.Length > 63) t = t.Substring(0, 63);
            if (tray.Text != t) tray.Text = t;
        }

        [System.Runtime.InteropServices.DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);

        // Pencere gizliyken kullanılmayan bellek Windows'a geri verilir
        static void TrimMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                using (var p = System.Diagnostics.Process.GetCurrentProcess()) EmptyWorkingSet(p.Handle);
            }
            catch { }
        }

        // Discord vb. programlarda sanal mikrofon "VB-Audio Virtual Cable" değil "Broken Mic" görünsün (bir kez; sonra kullanıcı değiştirebilir)
        void DefaultMicName()
        {
            if (cfg.MicNamed) return;
            var ep = MicName.Find();
            if (ep == null) return;   // sürücü henüz yok ya da yeniden başlatma bekleniyor: sonraki açılışta
            cfg.MicNamed = true;
            cfg.Save();
            if (!ep.IsFactoryName) return;   // kullanıcı zaten ad vermiş
            if (MicName.Rename(ep, MicName.Default, false) == null)
            {
                micEp = MicName.Find();
                if (micEp != null) txtMicName.Box.Text = micEp.Name;
            }
        }

        void InitialSize()
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            var frame = SizeFromClientSize(Size.Empty);
            MinimumSize = SizeFromClientSize(new Size(P(400), P(560)));
            ClientSize = new Size(Math.Min(P(980), wa.Width - frame.Width), Math.Min(P(780), wa.Height - frame.Height));
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
                        // ekrana sığdır (alttaki sekme çubuğu görev çubuğunun arkasında kalmasın)
                        var area = Screen.FromRectangle(r).WorkingArea;
                        r.Width = Math.Min(r.Width, area.Width);
                        r.Height = Math.Min(r.Height, area.Height);
                        r.X = Math.Max(area.Left, Math.Min(r.X, area.Right - r.Width));
                        r.Y = Math.Max(area.Top, Math.Min(r.Y, area.Bottom - r.Height));
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
            server.Changed += delegate (Client c) { SafeUi(delegate { if (list != null) list.Invalidate(); }); };
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
                    // Sanal mikrofon seçiliyse ses hep oraya gider. Hoparlöre (kendi sesini duymak) sadece istenirse.
                    bool virt = output != null && output.Virtual;
                    if (virt || cfg.Monitor)
                    {
                        if (player == null || player.Rate != c.Rate)
                        {
                            if (player != null) player.Dispose();
                            player = null;
                            player = new Player(output == null ? -1 : output.Id, c.Rate);
                            playerError = null;
                        }
                        player.Write(buf, off, n);
                    }
                    else if (player != null) { player.Dispose(); player = null; }
                    if (cfg.Monitor && virt)
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
        bool WrongOut { get { return HasVirtual && (output == null || !output.Virtual); } }

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

        int Port { get { return server != null ? server.Port : cfg.Port; } }

        // ------------------------------------------------------------------ güvenlik duvarı
        void CheckFirewall()
        {
            if (server == null || Interlocked.Exchange(ref fwChecking, 1) == 1) return;
            int port = server.Port;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var s = Firewall.Check(port);
                fw = s;
                Interlocked.Exchange(ref fwChecking, 0);
                SafeUi(UpdateUi);
            });
        }

        void AllowFirewall()
        {
            fwMessage = Firewall.Allow(Port);
            if (fwMessage == null) fw = FwState.Unknown;
            UpdateUi();
            CheckFirewall();
        }

        // ------------------------------------------------------------------ arayüz
        T Add<T>(T c) where T : Control
        {
            if (!(c is Label)) c.Font = Font;
            page.Controls.Add(c);
            if (building >= 0) groups[building].Add(c);
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
            l.BackColor = Color.Transparent;
            l.Font = new Font(Font.FontFamily, size, style);
            l.AutoSize = true;
            l.UseMnemonic = false;
            return Add(l);
        }

        Label Section(string text) { return Text2(L.Upper(text), Theme.Sub, 8f, FontStyle.Bold); }
        Label Title(string text) { return Text2(text, Theme.Text, 19f, FontStyle.Bold); }
        Label Sub(string text) { return Text2(text, Theme.Sub, 10f, FontStyle.Regular); }

        void BuildUi()
        {
            SuspendLayout();
            Font = new Font("Segoe UI", 9.5f);
            for (int i = 0; i < 4; i++) { groups[i].Clear(); subs[i] = null; }

            bar.Font = Font;
            header.Font = Font;
            header.Logo = Theme.Logo();
            header.Title = AppInfo.Name;
            header.Subtitle = L.T("Telefonunu ya da tabletini bilgisayarın mikrofonu yap.", "Use your phone or tablet as the computer's microphone.");
            header.Invalidate();
            bar.Labels = new[] { L.T("Mikrofon", "Microphone"), L.T("Bağlan", "Connect"), L.T("Ses", "Audio"), L.T("Ayarlar", "Settings") };
            bar.IconKinds = new[] { Icons.Mic, Icons.Scan, Icons.Tune, Icons.Gear };
            bar.Selected = tab;
            bar.Invalidate();

            // ---- Mikrofon
            building = TabMic;
            titles[TabMic] = Title(L.T("Mikrofon", "Microphone"));
            orb = Add(new MicOrb());
            lblState = Text2("", Theme.Text, 15f, FontStyle.Bold);
            lblState.TextAlign = ContentAlignment.TopCenter;
            lblState2 = Text2("", Theme.Sub, 10f, FontStyle.Regular);
            lblState2.TextAlign = ContentAlignment.TopCenter;
            meter = Add(new LevelMeter());
            btnGoConnect = Btn(L.T("Telefonu bağla", "Connect a phone"), BtnKind.Primary, delegate { ShowTab(TabConnect); });
            for (int i = 0; i < alertText.Length; i++)
            {
                int k = i;
                alertText[i] = Text2("", Theme.Text, 9.5f, FontStyle.Regular);
                alertBtn[i] = Btn("", BtnKind.Primary, delegate { if (alertAct[k] != null) alertAct[k](); });
            }
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

            // ---- Bağlan
            building = TabConnect;
            titles[TabConnect] = Title(L.T("Telefonu bağla", "Connect your phone"));
            subs[TabConnect] = Sub(L.T("Telefonda Broken Microphone uygulamasıyla ya da kamerayla bu QR kodu okut.",
                                       "Scan this QR code with the Broken Microphone app or the phone's camera."));
            qr = Add(new QrView());
            qr.Font = new Font(Font.FontFamily, 9f);
            pickAddress = Add(new Picker());
            FillAddresses();
            pickAddress.Changed += delegate
            {
                var a = pickAddress.SelectedItem as LocalAddress;
                if (a == null) return;
                cfg.Address = a.Ip.ToString();
                cfg.Save();
                UpdateUrl();
            };
            btnCopy = Btn(L.T("Adresi kopyala", "Copy address"), BtnKind.Secondary, delegate
            {
                try { Clipboard.SetText(Url); btnCopy.Text = L.T("Kopyalandı", "Copied"); } catch { }
            });
            btnAndroid = Btn(L.T("Android uygulaması", "Android app"), BtnKind.Secondary, delegate { ShowAndroid(); });
            btnHelp = Btn(L.T("Bağlanamıyor musun?", "Can't connect?"), BtnKind.Ghost, delegate { ShowConnectHelp(); });
            secSteps = Section(L.T("Nasıl bağlanır", "How to connect"));
            lblSteps = Text2(L.T(
                "1. Telefon bilgisayarla aynı Wi-Fi ağında olsun.\n2. Broken Microphone uygulamasında QR Okut'a bas (ya da telefonun kamerasıyla okut).\n3. Tarayıcıda açtıysan \"Bağlantı gizli değil\" uyarısında Ayrıntılar → Siteyi ziyaret et (sadece ilk seferde).\n4. Mikrofon düğmesine bas ve izin ver.",
                "1. Put the phone on the same Wi-Fi as the computer.\n2. In the Broken Microphone app tap Scan (or use the phone's camera).\n3. In a browser, at the \"connection is not private\" warning choose Details → Visit website (first time only).\n4. Tap the microphone button and allow access."),
                Theme.Text, 9.5f, FontStyle.Regular);

            // ---- Ses
            building = TabAudio;
            titles[TabAudio] = Title(L.T("Ses", "Audio"));
            subs[TabAudio] = Sub(L.T("Telefondan gelen ses nereye gitsin.", "Where the audio from the phone goes."));
            secOut = Section(L.T("Ses nereye gitsin", "Where the audio goes"));
            pickOutput = Add(new Picker());
            FillOutputs();
            pickOutput.Changed += delegate
            {
                var d = pickOutput.SelectedItem as OutputDevice;
                output = d != null && d.Id >= 0 ? d : null;
                cfg.Output = output == null ? "" : output.Name;
                cfg.Save();
                ResetPlayers();
                UpdateUi();
            };
            lblOutHint = Text2("", Theme.Sub, 9f, FontStyle.Regular);
            secMon = Section(L.T("Dinleme", "Listening"));
            tglMonitor = Add(new Toggle(L.T("Kendi sesimi hoparlörden duy", "Hear myself on the speakers")));
            tglMonitor.Checked = cfg.Monitor;
            tglMonitor.Changed += delegate (bool on) { cfg.Monitor = on; cfg.Save(); UpdateUi(); };
            meter2 = Add(new LevelMeter());
            lblDiag = Text2(" ", Theme.Sub, 8.5f, FontStyle.Regular);
            secVirt = Section(L.T("Sanal mikrofon", "Virtual microphone"));
            lblVirt = Text2("", Theme.Text, 9.5f, FontStyle.Regular);
            btnDriver = Btn("", BtnKind.Primary, delegate { FixOutput(); });
            micEp = MicName.Find();
            lblMicName = Section(L.T("Programlarda görünen adı", "Name shown in apps"));
            txtMicName = Add(new TextField());
            txtMicName.Box.Text = micEp == null || micEp.IsFactoryName ? MicName.Default : micEp.Name;
            txtMicName.Box.MaxLength = 40;
            btnRename = Btn(L.T("Adını değiştir", "Rename"), BtnKind.Secondary, delegate { RenameMic(); });
            txtMicName.Box.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RenameMic(); } };

            // ---- Ayarlar
            building = TabSettings;
            titles[TabSettings] = Title(L.T("Ayarlar", "Settings"));
            secLang = Section(L.T("Dil", "Language"));
            segLang = Add(new Seg());
            segLang.Items = new[] { "Türkçe", "English" };
            segLang.Selected = Math.Max(0, Array.IndexOf(L.Codes, L.Lang));
            segLang.Changed += delegate (int i)
            {
                cfg.Lang = L.Codes[i];
                cfg.Save();
                L.Set(cfg.Lang);
                BeginInvoke((MethodInvoker)RebuildUi);
            };
            secStart = Section(L.T("Başlangıç", "Startup"));
            tglStart = Add(new Toggle(L.T("Windows ile başlat", "Start with Windows")));
            tglStart.Checked = AutoStart.Enabled;
            tglStart.Changed += delegate (bool on) { if (!AutoStart.Set(on)) tglStart.Checked = AutoStart.Enabled; };
            lblStart = Text2(L.T("Bilgisayar açıldıktan biraz sonra, düşük öncelikle ve pencere açmadan tepside başlar; açılışı yavaşlatmaz.",
                                 "Starts in the tray a little after Windows starts, at low priority and without opening the window, so it doesn't slow down startup."),
                             Theme.Sub, 9f, FontStyle.Regular);
            secNet = Section(L.T("Bağlantı", "Connection"));
            lblFw = Text2("", Theme.Text, 9.5f, FontStyle.Regular);
            btnFw = Btn("", BtnKind.Secondary, delegate { AllowFirewall(); });
            btnHelp2 = Btn(L.T("Bağlanamıyor musun?", "Can't connect?"), BtnKind.Ghost, delegate { ShowConnectHelp(); });
            secAbout = Section(L.T("Hakkında", "About"));
            logo = new PictureBox();
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.BackColor = Color.Transparent;
            logo.Image = Theme.Logo();
            Add(logo);
            lblApp = Text2(AppInfo.Name + "\n" + L.T("Sürüm ", "Version ") + AppInfo.Version, Theme.Text, 10.5f, FontStyle.Bold);
            lblFoot = Text2(L.T("Ses sadece yerel ağda, bilgisayarla telefon arasında gider; internete çıkmaz. QR koddaki anahtarı bilmeyen kimse bağlanamaz.",
                                "Audio only travels between the phone and the computer on your local network; nothing goes to the internet. Nobody can connect without the key in the QR code."),
                            Theme.Sub, 9f, FontStyle.Regular);
            btnAbout = Btn(L.T("Lisans ve ayrıntılar", "License and details"), BtnKind.Secondary, delegate { using (var a = new AboutDialog()) a.ShowDialog(this); });
            building = -1;

            UpdateUrl();
            UpdateUi();
            ResumeLayout();
            ShowTab(tab);
        }

        void FillOutputs()
        {
            pickOutput.Items.Clear();
            var def = new OutputDevice();
            def.Id = -1;
            def.Name = L.T("Windows'un varsayılan hoparlörü", "Windows default speakers");
            pickOutput.Items.Add(def);
            foreach (var d in outputs) pickOutput.Items.Add(d);
            object pick = def;
            foreach (OutputDevice d in pickOutput.Items) if (output != null && d.Id == output.Id && d.Name == output.Name) pick = d;
            pickOutput.SelectedItem = pick;
        }

        void FillAddresses()
        {
            pickAddress.Items.Clear();
            foreach (var a in addresses) pickAddress.Items.Add(a);
            var sel = SelectedAddress;
            pickAddress.SelectedItem = sel;
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

        void ShowTab(int i)
        {
            tab = i;
            bar.Selected = i;
            page.SuspendLayout();
            for (int g = 0; g < groups.Length; g++)
                foreach (var c in groups[g]) c.Visible = g == i;
            page.ScrollY = 0;
            page.ResumeLayout(false);
            DoLayout();
        }

        // ------------------------------------------------------------------ düzen
        // Her sekme ortada, en fazla belli genişlikte bir sütun. Uzun içerik kaydırılır (zemin sabit kalır).
        int X, W, Y, pad;

        void DoLayout()
        {
            if (titles[TabSettings] == null) return;
            page.SuspendLayout();
            for (int pass = 0; pass < 2; pass++)
            {
                page.Cards.Clear();
                int PW = page.ClientSize.Width;
                pad = PW < P(560) ? P(16) : P(28);
                W = Math.Min(tab == TabConnect ? P(900) : P(620), PW - pad * 2);
                X = (PW - W) / 2;
                Y = Math.Max(P(6), pad - P(10)) - page.ScrollY;
                titles[tab].Location = new Point(X, Y);
                Y = titles[tab].Bottom + P(2);
                if (subs[tab] != null)
                {
                    subs[tab].MaximumSize = new Size(W, 0);
                    subs[tab].Location = new Point(X + P(1), Y);
                    Y = subs[tab].Bottom;
                }
                Y += P(20);
                switch (tab)
                {
                    case TabMic: PlaceMic(); break;
                    case TabConnect: PlaceConnect(); break;
                    case TabAudio: PlaceAudio(); break;
                    default: PlaceSettings(); break;
                }
                page.ContentH = Y + page.ScrollY + pad;
                if (page.ScrollY <= page.MaxScroll) break;
                page.ScrollY = page.MaxScroll;
            }
            page.ResumeLayout(false);
            page.Invalidate(true);
        }

        void Card(int x, int y, int w, int h, Color tint) { page.Cards.Add(new GlassCard(new Rectangle(x, y, w, h), tint)); }

        static int Wrap(Label l, int x, int y, int w)
        {
            l.MaximumSize = new Size(Math.Max(1, w), 0);
            l.Location = new Point(x, y);
            return y + l.Height;
        }

        void PlaceMic()
        {
            int ow = Math.Min(W, P(210));
            orb.SetBounds(X + (W - ow) / 2, Y, ow, ow);
            Y = orb.Bottom + P(4);
            lblState.MaximumSize = new Size(W, 0);
            lblState.Location = new Point(X + (W - lblState.Width) / 2, Y);
            Y = lblState.Bottom + P(4);
            lblState2.MaximumSize = new Size(Math.Min(W, P(470)), 0);
            lblState2.Location = new Point(X + (W - lblState2.Width) / 2, Y);
            Y = lblState2.Bottom + P(16);
            int mw = Math.Min(W, P(300));
            meter.SetBounds(X + (W - mw) / 2, Y, mw, P(8));
            Y = meter.Bottom + P(24);

            bool noPhone = list.Items.Count == 0 && server != null && addresses.Count > 0;
            btnGoConnect.Visible = noPhone;
            if (noPhone)
            {
                int bw = Math.Min(W, P(240));
                btnGoConnect.SetBounds(X + (W - bw) / 2, Y, bw, P(46));
                Y = btnGoConnect.Bottom + P(24);
            }

            int cp = P(18);
            for (int i = 0; i < alertText.Length; i++)
            {
                bool show = i < alertCount;
                alertText[i].Visible = show;
                alertBtn[i].Visible = show && alertBtn[i].Text != "";
                if (!show) continue;
                bool side = W >= P(520) && alertBtn[i].Visible;
                int bw = Math.Min(W - cp * 2, P(200));
                int top = Y;
                int ty = Wrap(alertText[i], X + cp, Y + cp, W - cp * 2 - (side ? bw + P(14) : 0));
                int bottom;
                if (!alertBtn[i].Visible) bottom = ty;
                else if (side)
                {
                    int h = Math.Max(ty - (Y + cp), P(42));
                    alertBtn[i].SetBounds(X + W - cp - bw, Y + cp + (h - P(42)) / 2, bw, P(42));
                    alertText[i].Top = Y + cp + (h - alertText[i].Height) / 2;
                    bottom = Y + cp + h;
                }
                else
                {
                    alertBtn[i].SetBounds(X + cp, ty + P(12), W - cp * 2, P(42));
                    bottom = alertBtn[i].Bottom;
                }
                Card(X, top, W, bottom + cp - top, Theme.Warn);
                Y = bottom + cp + P(12);
            }
            if (alertCount > 0) Y += P(12);

            secDevices.Location = new Point(X + P(6), Y);
            Y = secDevices.Bottom + P(8);
            list.SetBounds(X, Y, W, list.Measure());
            Y = list.Bottom;
        }

        void PlaceConnect()
        {
            bool wide = W >= P(720);
            int gap = P(24), cp = P(18);
            int qw = wide ? P(340) : Math.Min(W, P(360));
            int qx = wide ? X : X + (W - qw) / 2;
            qr.SetBounds(qx, Y, qw, qw + P(26));
            int leftBottom = qr.Bottom;

            int rx = wide ? X + qw + gap : X, rw = wide ? W - qw - gap : W;
            int y = wide ? Y : qr.Bottom + P(16);
            int top = y;
            y += cp;
            pickAddress.Visible = addresses.Count > 1;
            if (pickAddress.Visible) { pickAddress.SetBounds(rx + cp, y, rw - cp * 2, P(42)); y = pickAddress.Bottom + P(10); }
            int half = (rw - cp * 2 - P(10)) / 2;
            btnCopy.SetBounds(rx + cp, y, half, P(42));
            btnAndroid.SetBounds(rx + cp + half + P(10), y, rw - cp * 2 - half - P(10), P(42));
            y = btnCopy.Bottom + P(8);
            btnHelp.SetBounds(rx + cp, y, rw - cp * 2, P(36));
            y = btnHelp.Bottom + cp;
            Card(rx, top, rw, y - top, Color.Empty);

            y += P(14);
            top = y;
            secSteps.Location = new Point(rx + cp, y + cp);
            y = Wrap(lblSteps, rx + cp, secSteps.Bottom + P(8), rw - cp * 2) + cp;
            Card(rx, top, rw, y - top, Color.Empty);
            Y = Math.Max(leftBottom, y);
        }

        void PlaceAudio()
        {
            int cp = P(18);
            int top = Y;
            secOut.Location = new Point(X + cp, Y + cp);
            pickOutput.SetBounds(X + cp, secOut.Bottom + P(8), W - cp * 2, P(42));
            Y = Wrap(lblOutHint, X + cp + P(2), pickOutput.Bottom + P(10), W - cp * 2) + cp;
            Card(X, top, W, Y - top, Color.Empty);

            Y += P(14);
            top = Y;
            secMon.Location = new Point(X + cp, Y + cp);
            tglMonitor.SetBounds(X + cp, secMon.Bottom + P(8), W - cp * 2, P(32));
            meter2.SetBounds(X + cp, tglMonitor.Bottom + P(12), W - cp * 2, P(8));
            lblDiag.Location = new Point(X + cp, meter2.Bottom + P(8));
            Y = lblDiag.Bottom + cp;
            Card(X, top, W, Y - top, Color.Empty);

            Y += P(14);
            top = Y;
            secVirt.Location = new Point(X + cp, Y + cp);
            Y = Wrap(lblVirt, X + cp, secVirt.Bottom + P(8), W - cp * 2);
            btnDriver.Visible = !HasVirtual || WrongOut;
            if (btnDriver.Visible)
            {
                btnDriver.SetBounds(X + cp, Y + P(12), Math.Min(W - cp * 2, P(280)), P(42));
                Y = btnDriver.Bottom;
            }
            bool named = micEp != null;
            lblMicName.Visible = txtMicName.Visible = btnRename.Visible = named;
            if (named)
            {
                lblMicName.Location = new Point(X + cp, Y + P(16));
                int bw = Math.Min(P(170), (W - cp * 2) / 3);
                txtMicName.SetBounds(X + cp, lblMicName.Bottom + P(8), W - cp * 2 - bw - P(10), P(42));
                btnRename.SetBounds(txtMicName.Right + P(10), txtMicName.Top, bw, P(42));
                Y = txtMicName.Bottom;
            }
            Y += cp;
            Card(X, top, W, Y - top, HasVirtual && !WrongOut ? Color.Empty : Theme.Warn);
        }

        void PlaceSettings()
        {
            int cp = P(18);
            int top = Y;
            secLang.Location = new Point(X + cp, Y + cp);
            segLang.SetBounds(X + cp, secLang.Bottom + P(8), Math.Min(W - cp * 2, P(240)), P(38));
            Y = segLang.Bottom + cp;
            Card(X, top, W, Y - top, Color.Empty);

            Y += P(14);
            top = Y;
            secStart.Location = new Point(X + cp, Y + cp);
            tglStart.SetBounds(X + cp, secStart.Bottom + P(8), W - cp * 2, P(32));
            Y = Wrap(lblStart, X + cp, tglStart.Bottom + P(6), W - cp * 2) + cp;
            Card(X, top, W, Y - top, Color.Empty);

            Y += P(14);
            top = Y;
            secNet.Location = new Point(X + cp, Y + cp);
            Y = Wrap(lblFw, X + cp, secNet.Bottom + P(8), W - cp * 2) + P(12);
            int bw = Math.Min((W - cp * 2 - P(10)) / 2, P(230));
            btnFw.SetBounds(X + cp, Y, bw, P(42));
            btnHelp2.SetBounds(X + cp + bw + P(10), Y, bw, P(42));
            Y = btnFw.Bottom + cp;
            Card(X, top, W, Y - top, fw == FwState.Blocked ? Theme.Warn : Color.Empty);

            Y += P(14);
            top = Y;
            secAbout.Location = new Point(X + cp, Y + cp);
            logo.SetBounds(X + cp, secAbout.Bottom + P(10), P(48), P(48));
            lblApp.Location = new Point(logo.Right + P(12), logo.Top + (logo.Height - lblApp.Height) / 2);
            Y = Wrap(lblFoot, X + cp, logo.Bottom + P(12), W - cp * 2);
            btnAbout.SetBounds(X + cp, Y + P(12), Math.Min(W - cp * 2, P(230)), P(42));
            Y = btnAbout.Bottom + cp;
            Card(X, top, W, Y - top, Color.Empty);
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

        string VirtualMicLabel
        {
            get
            {
                if (output == null || !output.Virtual) return null;
                string n = output.Name.ToLowerInvariant();
                if (micEp != null) return micEp.Full;
                if (n.Contains("broken microphone")) return "Broken Microphone";
                if (n.Contains("cable")) return "CABLE Output (VB-Audio Virtual Cable)";
                return output.Name;
            }
        }

        void Alert(string text, string button, Action act)
        {
            if (alertCount >= alertText.Length) return;
            alertText[alertCount].Text = text;
            alertBtn[alertCount].Text = button ?? "";
            alertAct[alertCount] = act;
            alertCount++;
        }

        string lastSig;

        void UpdateUi()
        {
            if (lblFoot == null) return;
            var a = Active;
            string title, sub;
            OrbMode mode;
            if (server == null)
            {
                title = L.T("Bağlantı başlatılamadı", "Couldn't start");
                sub = playerError ?? "";
                mode = OrbMode.Warn;
            }
            else if (addresses.Count == 0)
            {
                title = L.T("Ağ bağlantısı yok", "No network");
                sub = L.T("Bilgisayarı telefonla aynı Wi-Fi ağına bağla.", "Connect the computer to the same Wi-Fi as the phone.");
                mode = OrbMode.Warn;
            }
            else if (a == null)
            {
                title = L.T("Telefon bekleniyor", "Waiting for a phone");
                sub = L.T("Telefonda Broken Microphone'u aç ya da Bağlan sekmesindeki QR kodu okut.", "Open Broken Microphone on the phone or scan the QR code on the Connect tab.");
                mode = OrbMode.Idle;
            }
            else if (a.Muted)
            {
                title = L.T("Sessizde", "Muted");
                sub = string.Format(L.T("{0} bağlı ama telefonda sessize alınmış.", "{0} is connected but muted on the phone."), a.Name);
                mode = OrbMode.Muted;
            }
            else if (!a.Sending)
            {
                title = L.T("Bağlı", "Connected");
                sub = string.Format(L.T("{0}. Telefonda mikrofon düğmesine bas.", "{0}. Tap the microphone button on the phone."), a.Name);
                mode = OrbMode.Waiting;
            }
            else
            {
                title = L.T("Mikrofon açık", "Microphone on");
                string to = VirtualMicLabel != null ? VirtualMicLabel : cfg.Monitor ? L.T("hoparlör", "speakers") : L.T("hiçbir yere çalınmıyor", "not played anywhere");
                sub = a.Name + (a.Mic != "" ? " (" + a.Mic + ")" : "") + "  →  " + to;
                mode = OrbMode.Live;
            }
            lblState.Text = title;
            lblState2.Text = sub;
            orb.Mode = mode;
            orb.Invalidate();

            alertCount = 0;
            if (server != null && playerError != null) Alert(L.T("Ses çıkışı: ", "Audio output: ") + playerError, null, null);
            if (server != null && fw == FwState.Blocked)
                Alert(string.Format(L.T("Windows güvenlik duvarı telefonun bağlanmasını engelliyor (ağ: {0}). İzin sadece bu programa ve yerel ağdaki cihazlara verilir.",
                                        "Windows Firewall is blocking the phone (network: {0}). Permission is given only to this program and devices on your local network."), Firewall.ProfileName),
                      L.T("İzin ver", "Allow"), AllowFirewall);
            if (server != null && WrongOut)
                Alert(L.T("Ses Discord gibi programlara gitmiyor: hoparlör seçili.", "The audio isn't reaching apps like Discord: a speaker is selected."),
                      L.T("Sanal mikrofona gönder", "Send to virtual mic"), FixOutput);
            if (server != null && !HasVirtual)
                Alert(L.T("Discord, Zoom ya da oyunlarda mikrofon olarak kullanmak için sanal mikrofon gerekli.", "You need a virtual microphone to use it in Discord, Zoom or games."),
                      L.T("Sanal mikrofonu kur…", "Install virtual mic…"), FixOutput);

            // Ses sekmesi
            string mic = VirtualMicLabel;
            if (mic != null)
                lblOutHint.Text = string.Format(L.T("Discord, Zoom, Teams, OBS ya da oyunda mikrofon (giriş cihazı) olarak \"{0}\" seç.\nDiscord'da Ses ve Görüntü → Gürültü Azaltma ve Yankı Engelleme'yi kapat: telefon bunları zaten yapıyor, iki kez yapılınca ses titrer.",
                                                    "In Discord, Zoom, Teams, OBS or your game, pick \"{0}\" as the microphone (input device).\nIn Discord's Voice & Video settings turn off Noise Suppression and Echo Cancellation: the phone already does them, and doing them twice makes the voice warble."), mic);
            else
                lblOutHint.Text = cfg.Monitor
                    ? L.T("Bu bir hoparlör: sesi sadece sen duyarsın, Discord gibi programlara gitmez.", "This is a speaker: only you hear the audio; it doesn't reach apps like Discord.")
                    : L.T("Ses şu an hiçbir yere çalınmıyor; çubuk sesin geldiğini gösterir.", "The audio isn't played anywhere right now; the bar shows it's arriving.");
            if (!HasVirtual)
            {
                lblVirt.Text = L.T("Kurulu değil. Discord, Zoom ya da oyunlarda kullanmak için gerekli. Broken Microphone, VB-Audio'nun ücretsiz sürücüsünü resmi sunucudan indirip imzasını denetleyerek kurar.",
                                   "Not installed. You need it for Discord, Zoom or games. Broken Microphone downloads VB-Audio's free driver from the official server and checks its signature before installing.");
                btnDriver.Text = L.T("Sanal mikrofonu kur…", "Install virtual mic…");
            }
            else if (WrongOut)
            {
                lblVirt.Text = L.T("Kurulu ama ses ona gönderilmiyor.", "Installed, but the audio isn't sent to it.");
                btnDriver.Text = L.T("Sanal mikrofona gönder", "Send to virtual mic");
            }
            else lblVirt.Text = L.T("Kurulu ve seçili. ✓", "Installed and selected. ✓");
            if (renameMsg != null) lblVirt.Text += "\n" + renameMsg;
            btnDriver.Invalidate();

            // Ayarlar sekmesi
            string fwText;
            if (server == null) fwText = L.T("Bağlantı başlatılamadı.", "The connection couldn't start.");
            else if (fw == FwState.Open) fwText = string.Format(L.T("✓ Telefon bu bilgisayara bağlanabilir (ağ: {0}, port {1}).", "✓ The phone can reach this computer (network: {0}, port {1})."), Firewall.ProfileName, Port);
            else if (fw == FwState.Blocked) fwText = string.Format(L.T("Windows güvenlik duvarı telefonu engelliyor (ağ: {0}). \"İzin ver\"e bas; Windows yönetici izni sorar.", "Windows Firewall is blocking the phone (network: {0}). Press \"Allow\"; Windows asks for administrator rights."), Firewall.ProfileName);
            else fwText = L.T("Güvenlik duvarı denetleniyor…", "Checking the firewall…");
            if (fwMessage != null) fwText += "\n" + fwMessage;
            lblFw.Text = fwText;
            lblFw.ForeColor = fw == FwState.Blocked ? Theme.Warn : Theme.Text;
            btnFw.Text = fw == FwState.Blocked ? L.T("İzin ver", "Allow") : L.T("İzni yeniden ekle", "Re-add permission");
            btnFw.Kind = fw == FwState.Blocked ? BtnKind.Primary : BtnKind.Secondary;
            btnFw.Invalidate();

            string sig = title + "|" + sub + "|" + alertCount + "|" + alertText[0].Text + alertText[1].Text + alertText[2].Text + alertText[3].Text + "|" +
                         lblOutHint.Text + lblVirt.Text + lblFw.Text + list.Items.Count;
            if (sig != lastSig) { lastSig = sig; DoLayout(); }
        }

        // Pencere gizliyken saniyede bir: yalnızca gerekenler
        int bgSlow;
        void BackgroundTick()
        {
            bgSlow++;
            if (server != null && !SameClients(server.Clients, list.Items)) OnClientsChanged();
            UpdateTray();
            if (bgSlow % 20 == 0) CheckFirewall();
            if (bgSlow % 6 == 0)
            {
                var now = Net.Addresses();
                if (Signature(now) != Signature(addresses))
                {
                    addresses = now;
                    FillAddresses();
                    UpdateUrl();
                    CheckFirewall();
                }
            }
        }

        void RenameMic()
        {
            var ep = MicName.Find();
            if (ep == null) return;
            string err = MicName.Rename(ep, txtMicName.Box.Text);
            micEp = MicName.Find();
            renameMsg = err ?? string.Format(L.T("Adı \"{0}\" oldu. Discord'u yeniden başlatınca yeni adı görür.", "Renamed to \"{0}\". Restart Discord to see the new name."), micEp != null ? micEp.Name : "");
            UpdateUi();
        }

        void FixOutput()
        {
            if (!HasVirtual) { ShowDriverHelp(); return; }
            foreach (OutputDevice d in pickOutput.Items)
                if (d.Virtual)
                {
                    pickOutput.SelectedItem = d;
                    output = d;
                    cfg.Output = d.Name;
                    cfg.Save();
                    ResetPlayers();
                    UpdateUi();
                    break;
                }
        }

        int slow;
        void OnTick()
        {
            if (background) { BackgroundTick(); return; }
            var a = Active;
            float lv = 0;
            if (a != null && a.Sending) lv = a.Peak;
            meter.Muted = meter2.Muted = a != null && a.Muted;
            if (tab == TabMic) { meter.Push(lv); orb.Push(lv); }
            if (tab == TabAudio)
            {
                meter2.Push(lv);
                if (slow % 6 == 0)
                {
                    string d;
                    lock (audioLock)
                        d = player == null ? " " : string.Format(L.T("Tampon {0} ms (hedef {1}) · kesinti {2}", "Buffer {0} ms (target {1}) · dropouts {2}"), player.BufferedMs, player.TargetMs, player.Underruns);
                    if (lblDiag.Text != d) lblDiag.Text = d;
                }
            }
            if (++slow % 6 == 0)
            {
                // bağlantı olayı pencere hazır olmadan geldiyse kaçmasın
                if (server != null && !SameClients(server.Clients, list.Items)) OnClientsChanged();
                list.Invalidate();
                UpdateUi();
            }
            if (slow % 60 == 0) btnCopy.Text = L.T("Adresi kopyala", "Copy address");
            if (slow % 250 == 0) CheckFirewall();   // ~20 sn: ağ türü değişmiş olabilir
            // ağ değişti mi (ör. Wi-Fi'ye yeni bağlandı)
            if (slow % 75 == 0)
            {
                var now = Net.Addresses();
                if (Signature(now) != Signature(addresses))
                {
                    addresses = now;
                    FillAddresses();
                    UpdateUrl();
                    UpdateUi();
                    DoLayout();
                    CheckFirewall();
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
        void ShowAndroid()
        {
            using (var d = new AndroidDialog()) d.ShowDialog(this);
        }

        void ShowConnectHelp()
        {
            using (var d = new ConnectHelpDialog(Port)) d.ShowDialog(this);
            CheckFirewall();
        }

        void ShowDriverHelp()
        {
            using (var d = new DriverDialog()) d.ShowDialog(this);
            DefaultMicName();
            micEp = MicName.Find();
            // kurulumdan sonra cihaz listesi tazelenir
            outputs = Player.Devices();
            output = PickOutput();
            FillOutputs();
            ResetPlayers();
            UpdateUi();
            DoLayout();
            CheckFirewall();
        }
    }
}
