using System;
using System.Drawing;
using System.Windows.Forms;

namespace BrokenMicrophone
{
    // Koyu temalı onay penceresi. word verilirse kullanıcının o kelimeyi yazması gerekir.
    class ConfirmDialog : Form
    {
        readonly TextBox input;
        readonly FlatButton ok;

        public ConfirmDialog(string title, string body, string word, string okText, bool danger)
        {
            Text = AppInfo.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.None;
            int w = Theme.P(500), pad = Theme.P(24), y = pad;

            var t = new Label();
            t.Text = title;
            t.Font = new Font(Font.FontFamily, 13f, FontStyle.Bold);
            t.ForeColor = danger ? Theme.Danger : Theme.Text;
            t.SetBounds(pad, y, w - pad * 2, Theme.P(30));
            Controls.Add(t);
            y += Theme.P(38);

            var b = new Label();
            b.Text = body;
            b.ForeColor = Theme.Text;
            using (var g = CreateGraphics())
                b.SetBounds(pad, y, w - pad * 2, (int)Math.Ceiling(g.MeasureString(body, Font, w - pad * 2).Height) + Theme.P(4));
            Controls.Add(b);
            y = b.Bottom + Theme.P(14);

            if (!string.IsNullOrEmpty(word))
            {
                var hint = new Label();
                hint.Text = L.T("Onaylamak için büyük harflerle şunu yaz: ", "To confirm, type this in capitals: ") + word;
                hint.ForeColor = Theme.Sub;
                hint.SetBounds(pad, y, w - pad * 2, Theme.P(22));
                Controls.Add(hint);
                y += Theme.P(26);
                input = new TextBox();
                input.BorderStyle = BorderStyle.FixedSingle;
                input.BackColor = Theme.Field;
                input.ForeColor = Theme.Text;
                input.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
                input.SetBounds(pad, y, w - pad * 2, Theme.P(30));
                input.TextChanged += delegate { ok.Enabled = input.Text.Trim() == word; };
                Controls.Add(input);
                y = input.Bottom + Theme.P(18);
            }

            var cancel = new FlatButton(L.T("Vazgeç", "Cancel"), BtnKind.Secondary);
            cancel.Font = Font;
            cancel.SetBounds(w - pad - Theme.P(300) - Theme.P(10), y, Theme.P(150), Theme.P(40));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            ok = new FlatButton(okText, BtnKind.Primary);
            ok.Font = Font;
            ok.SetBounds(w - pad - Theme.P(150), y, Theme.P(150), Theme.P(40));
            ok.Enabled = string.IsNullOrEmpty(word);
            ok.Click += delegate { if (ok.Enabled) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(ok);
            y += Theme.P(40) + pad;

            ClientSize = new Size(w, y);
            CancelButton = null;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(this); }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (input != null) input.Focus();
        }

        protected override bool ProcessDialogKey(Keys k)
        {
            if (k == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            return base.ProcessDialogKey(k);
        }

        public static bool Ask(IWin32Window owner, string title, string body, string word, string okText, bool danger)
        {
            using (var d = new ConfirmDialog(title, body, word, okText, danger)) return d.ShowDialog(owner) == DialogResult.OK;
        }

        public static void Info(IWin32Window owner, string title, string body)
        {
            using (var d = new ConfirmDialog(title, body, null, L.T("Tamam", "OK"), false))
            {
                foreach (Control c in d.Controls)
                    if (c is FlatButton && ((FlatButton)c).Kind == BtnKind.Secondary) c.Visible = false;
                d.ShowDialog(owner);
            }
        }
    }
}

namespace BrokenMicrophone
{
    // ------------------------------------------------------------------ Numaralı adımlar (rehber)
    class StepsView : Ctl
    {
        public string[][] Steps = new string[0][];   // { başlık, açıklama }

        public StepsView() { Cursor = Cursors.Default; }

        public int Measure(int width)
        {
            using (var g = CreateGraphics())
            {
                float h = 0;
                foreach (var s in Steps) h += Row(g, s, width) + Theme.P(10);
                return (int)Math.Ceiling(h);
            }
        }

        float Row(Graphics g, string[] s, int width)
        {
            float tx = Theme.P(44);
            using (var bold = new Font(Font, FontStyle.Bold))
            {
                float a = g.MeasureString(s[0], bold, (int)(width - tx)).Height;
                float b = g.MeasureString(s[1], Font, (int)(width - tx)).Height;
                return Math.Max(Theme.P(30), a + b + Theme.P(2));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, BackColor);
            float y = 0, tx = Theme.P(44), d = Theme.P(28);
            using (var bold = new Font(Font, FontStyle.Bold))
                for (int i = 0; i < Steps.Length; i++)
                {
                    var s = Steps[i];
                    float h = Row(g, s, Width);
                    using (var b = new SolidBrush(Theme.Mix(Theme.Accent, Theme.Bg, 0.75f))) g.FillEllipse(b, 0, y, d, d);
                    Theme.Draw(g, (i + 1).ToString(), bold, new RectangleF(0, y, d, d), Theme.Accent, StringAlignment.Center, StringAlignment.Center, false);
                    float th = g.MeasureString(s[0], bold, (int)(Width - tx)).Height;
                    Theme.Draw(g, s[0], bold, new RectangleF(tx, y + Theme.P(4), Width - tx, th), Theme.Text, StringAlignment.Near, StringAlignment.Near, true);
                    Theme.Draw(g, s[1], Font, new RectangleF(tx, y + Theme.P(4) + th, Width - tx, h - th), Theme.Sub, StringAlignment.Near, StringAlignment.Near, true);
                    y += h + Theme.P(10);
                }
        }
    }

    // ------------------------------------------------------------------ Koyu temalı küçük pencere tabanı
    class DarkDialog : Form
    {
        protected int W, Pad, Y;

        public DarkDialog(int width)
        {
            Text = AppInfo.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.None;
            Icon = Theme.AppIcon(32);
            W = Theme.P(width);
            Pad = Theme.P(26);
            Y = Pad;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(this); }

        protected override bool ProcessDialogKey(Keys k)
        {
            if (k == Keys.Escape) { Close(); return true; }
            return base.ProcessDialogKey(k);
        }

        protected Label AddLabel(string text, Font f, Color c, int x, int width)
        {
            var l = new Label();
            l.Text = text;
            l.Font = f;
            l.ForeColor = c;
            l.BackColor = Theme.Bg;
            using (var g = CreateGraphics())
                l.SetBounds(x, Y, width, (int)Math.Ceiling(g.MeasureString(text, f, width).Height) + Theme.P(2));
            Controls.Add(l);
            Y = l.Bottom;
            return l;
        }

        protected void Header(string title, string sub)
        {
            var logo = new PictureBox();
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.Image = Theme.Logo();
            logo.SetBounds(Pad, Y, Theme.P(52), Theme.P(52));
            Controls.Add(logo);
            int tx = Pad + Theme.P(66);
            int y0 = Y;
            Y = y0 + Theme.P(2);
            AddLabel(title, new Font(Font.FontFamily, 14f, FontStyle.Bold), Theme.Text, tx, W - tx - Pad);
            AddLabel(sub, Font, Theme.Sub, tx, W - tx - Pad);
            Y = Math.Max(Y, y0 + Theme.P(52)) + Theme.P(18);
        }
    }

    // ------------------------------------------------------------------ Hakkında
    class AboutDialog : DarkDialog
    {
        readonly Label lblUpdate;
        readonly FlatButton btnUpdate;
        string newUrl;

        public AboutDialog() : base(500)
        {
            Header(AppInfo.Name, L.T("Sürüm ", "Version ") + AppInfo.Version);
            AddLabel(L.T("Telefonunu, tabletini ya da onlara bağlı kablosuz kulaklığı bilgisayarın mikrofonu yapar. Ses yerel ağda doğrudan bilgisayara gelir; internete çıkmaz.",
                         "Turns your phone, tablet or the wireless headset connected to them into the computer's microphone. Audio goes straight to the computer over your local network; nothing goes to the internet."), Font, Theme.Text, Pad, W - Pad * 2);
            Y += Theme.P(12);
            AddLabel("Copyright © 2026 HErkanuzun · MIT License", Font, Theme.Sub, Pad, W - Pad * 2);
            Y += Theme.P(4);

            var link = new LinkLabel();
            link.Text = AppInfo.Repo.Replace("https://", "");
            link.LinkColor = Theme.Accent;
            link.ActiveLinkColor = Theme.AccentHover;
            link.VisitedLinkColor = Theme.Accent;
            link.BackColor = Theme.Bg;
            link.AutoSize = true;
            link.Location = new Point(Pad, Y);
            link.LinkClicked += delegate { Open(AppInfo.Repo); };
            Controls.Add(link);
            Y += Theme.P(32);

            AddLabel(L.T("Veriler: ", "Data: ") + System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BrokenMicrophone"),
                     new Font(Font.FontFamily, 8.5f), Theme.Sub, Pad, W - Pad * 2);
            Y += Theme.P(18);

            lblUpdate = AddLabel(" ", Font, Theme.Sub, Pad, W - Pad * 2);
            Y += Theme.P(10);

            btnUpdate = new FlatButton(L.T("Güncellemeleri denetle", "Check for updates"), BtnKind.Secondary);
            btnUpdate.Font = Font;
            btnUpdate.SetBounds(Pad, Y, Theme.P(210), Theme.P(40));
            btnUpdate.Click += delegate { CheckUpdate(); };
            Controls.Add(btnUpdate);

            var ok = new FlatButton(L.T("Kapat", "Close"), BtnKind.Primary);
            ok.Font = Font;
            ok.SetBounds(W - Pad - Theme.P(130), Y, Theme.P(130), Theme.P(40));
            ok.Click += delegate { Close(); };
            Controls.Add(ok);
            Y += Theme.P(40) + Pad;
            ClientSize = new Size(W, Y);
        }

        static void Open(string url)
        {
            try { System.Diagnostics.Process.Start(url); } catch { }
        }

        // Sadece kullanıcı isteyince GitHub'daki son sürüme bakılır
        void CheckUpdate()
        {
            if (newUrl != null) { Open(newUrl); return; }
            btnUpdate.Enabled = false;
            lblUpdate.ForeColor = Theme.Sub;
            lblUpdate.Text = L.T("Denetleniyor…", "Checking…");
            var t = new System.Threading.Thread(delegate ()
            {
                string tag = null, err = null;
                try { tag = Updates.LatestTag(); }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        btnUpdate.Enabled = true;
                        if (err != null || tag == null)
                        {
                            lblUpdate.Text = L.T("Denetlenemedi. İnternet bağlantısını kontrol et ya da GitHub sayfasına bak.", "Couldn't check. Check your connection or visit the GitHub page.");
                            return;
                        }
                        if (Updates.IsNewer(tag, AppInfo.Version))
                        {
                            lblUpdate.ForeColor = Theme.Accent;
                            lblUpdate.Text = L.T("Yeni sürüm var: ", "A new version is available: ") + tag;
                            newUrl = AppInfo.Repo + "/releases/latest";
                            btnUpdate.Text = L.T("İndirme sayfasını aç", "Open download page");
                        }
                        else lblUpdate.Text = L.T("En son sürümü kullanıyorsun.", "You're on the latest version.");
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    // ------------------------------------------------------------------ Android uygulaması
    // APK GitHub'dan iner (gerçek HTTPS sertifikası, her sürümde SHA-256 özeti). Bilgisayar APK dağıtmaz.
    class AndroidDialog : DarkDialog
    {
        public AndroidDialog() : base(600)
        {
            Header(L.T("Android uygulaması", "Android app"),
                   L.T("Ekran kapalıyken de çalışır; sustur ve durdur bildirimde", "Keeps working with the screen off; mute and stop from the notification"));
            int qs = Theme.P(220);
            int top = Y;
            var q = new QrView();
            q.Url = AppInfo.AndroidPage;
            q.Caption = L.T("İndirme sayfası (GitHub)", "Download page (GitHub)");
            q.SetBounds(Pad, Y, qs, qs + Theme.P(26));
            Controls.Add(q);
            int tx = Pad + qs + Theme.P(22), tw = W - tx - Pad;
            var bold = new Font(Font, FontStyle.Bold);
            string[][] steps = {
                new[] { L.T("1. Kodu okut", "1. Scan the code"),
                        L.T("Sayfa GitHub'da açılır ve APK'yı oradan indirir. Adres çubuğunda herkanuzun.github.io ya da github.com olduğundan emin ol.",
                            "The page opens on GitHub and downloads the APK from there. Make sure the address bar shows herkanuzun.github.io or github.com.") },
                new[] { L.T("2. Kur", "2. Install"),
                        L.T("İndirilen dosyayı aç. Android \"bilinmeyen uygulama\" izni isterse sadece tarayıcına ver; kurduktan sonra kapatabilirsin.",
                            "Open the downloaded file. If Android asks to allow unknown apps, allow it only for your browser; you can turn it off after installing.") },
                new[] { L.T("3. Bağlan", "3. Connect"),
                        L.T("Penceredeki ana QR kodu okut, sayfada \"Uygulamada aç\"a dokun ve uygulamada bağlantıyı onayla.",
                            "Scan the main QR code in the window, tap \"Open in app\" on the page and confirm the connection in the app.") },
            };
            foreach (var s in steps)
            {
                AddLabel(s[0], bold, Theme.Text, tx, tw);
                Y += Theme.P(2);
                AddLabel(s[1], Font, Theme.Sub, tx, tw);
                Y += Theme.P(10);
            }
            Y = Math.Max(Y, top + qs + Theme.P(26)) + Theme.P(18);

            var open = new FlatButton(L.T("Sayfayı bilgisayarda aç", "Open the page here"), BtnKind.Secondary);
            open.Font = Font;
            open.SetBounds(Pad, Y, Theme.P(220), Theme.P(40));
            open.Click += delegate { try { System.Diagnostics.Process.Start(AppInfo.AndroidPage); } catch { } };
            Controls.Add(open);
            var ok = new FlatButton(L.T("Kapat", "Close"), BtnKind.Primary);
            ok.Font = Font;
            ok.SetBounds(W - Pad - Theme.P(130), Y, Theme.P(130), Theme.P(40));
            ok.Click += delegate { Close(); };
            Controls.Add(ok);
            Y += Theme.P(40) + Pad;
            ClientSize = new Size(W, Y);
        }
    }

    // ------------------------------------------------------------------ Bağlanamıyor musun?
    class ConnectHelpDialog : DarkDialog
    {
        readonly int port;
        readonly Label lblFw;

        public ConnectHelpDialog(int port) : base(560)
        {
            this.port = port;
            Header(L.T("Bağlanamıyor musun?", "Can't connect?"), L.T("En sık sebepler ve çözümleri", "The most common causes and fixes"));
            var bold = new Font(Font, FontStyle.Bold);
            string[][] items = {
                new[] { L.T("Aynı ağda değilsiniz", "Not on the same network"),
                        L.T("Telefon mobil veride ya da başka bir Wi-Fi'de olabilir. İkisi aynı modeme bağlı olmalı. Misafir ağları cihazların birbirini görmesini engeller.",
                            "The phone may be on mobile data or another Wi-Fi. Both must be on the same router. Guest networks stop devices from seeing each other.") },
                new[] { L.T("Windows güvenlik duvarı", "Windows Firewall"),
                        L.T("Windows, telefonun bağlanmasını engelliyor olabilir. Aşağıdaki düğme sadece Broken Microphone'a, sadece özel ağlarda izin verir (yönetici izni ister).",
                            "Windows may be blocking the phone. The button below allows only Broken Microphone, only on private networks (asks for administrator rights).") },
                new[] { L.T("Ağ \"Ortak\" olarak ayarlı", "Network set to \"Public\""),
                        L.T("Ortak ağlarda Windows gelen bağlantıları engeller. Ayarlar → Ağ ve İnternet → Wi-Fi → ağın adı → \"Özel ağ\" seç.",
                            "On public networks Windows blocks incoming connections. Settings → Network & internet → Wi-Fi → your network → choose \"Private network\".") },
                new[] { L.T("\"Bağlantı gizli değil\" uyarısı", "\"Connection is not private\" warning"),
                        L.T("Normaldir: sertifikayı bu bilgisayar kendisi üretti, kimse onaylamadı. iPhone: Ayrıntıları göster → bu web sitesini ziyaret et. Android: Gelişmiş → siteye ilerle.",
                            "This is expected: this computer made its own certificate. iPhone: Show details → visit this website. Android: Advanced → proceed to the site.") },
                new[] { L.T("Mikrofon izni", "Microphone permission"),
                        L.T("İzni reddettiysen: iPhone'da Ayarlar → Safari → Mikrofon; Android'de adres çubuğundaki kilit simgesi → İzinler.",
                            "If you denied it: on iPhone Settings → Safari → Microphone; on Android the lock icon in the address bar → Permissions.") },
            };
            foreach (var it in items)
            {
                AddLabel(it[0], bold, Theme.Text, Pad, W - Pad * 2);
                Y += Theme.P(2);
                AddLabel(it[1], Font, Theme.Sub, Pad, W - Pad * 2);
                Y += Theme.P(12);
            }
            lblFw = AddLabel(" ", Font, Theme.Sub, Pad, W - Pad * 2);
            Y += Theme.P(8);

            var fw = new FlatButton(L.T("Güvenlik duvarı izni ver", "Allow through the firewall"), BtnKind.Secondary);
            fw.Font = Font;
            fw.SetBounds(Pad, Y, Theme.P(250), Theme.P(40));
            fw.Click += delegate { AllowFirewall(); };
            Controls.Add(fw);
            var ok = new FlatButton(L.T("Kapat", "Close"), BtnKind.Primary);
            ok.Font = Font;
            ok.SetBounds(W - Pad - Theme.P(130), Y, Theme.P(130), Theme.P(40));
            ok.Click += delegate { Close(); };
            Controls.Add(ok);
            Y += Theme.P(40) + Pad;
            ClientSize = new Size(W, Y);
        }

        void AllowFirewall()
        {
            string exe = Application.ExecutablePath;
            string args = "/c netsh advfirewall firewall delete rule name=\"Broken Microphone\" >nul 2>&1 & " +
                          "netsh advfirewall firewall add rule name=\"Broken Microphone\" dir=in action=allow program=\"" + exe + "\" enable=yes profile=private,domain protocol=TCP localport=" + port;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", args);
                psi.Verb = "runas";
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                psi.UseShellExecute = true;
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    p.WaitForExit(20000);
                    lblFw.ForeColor = p.ExitCode == 0 ? Theme.Accent : Theme.Danger;
                    lblFw.Text = p.ExitCode == 0 ? L.T("İzin verildi. Telefondan sayfayı yenile.", "Allowed. Reload the page on the phone.")
                                                 : L.T("İzin eklenemedi (hata ", "Couldn't add the rule (error ") + p.ExitCode + ").";
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                lblFw.ForeColor = Theme.Sub;
                lblFw.Text = L.T("Yönetici izni verilmedi; değişiklik yapılmadı.", "Administrator permission wasn't given; nothing changed.");
            }
            catch (Exception ex) { lblFw.ForeColor = Theme.Danger; lblFw.Text = ex.Message; }
        }
    }

    // ------------------------------------------------------------------ Sanal mikrofon
    class DriverDialog : DarkDialog
    {
        public DriverDialog() : base(560)
        {
            Header(L.T("Sanal mikrofon", "Virtual microphone"), L.T("Telefonun sesinin başka programlarda mikrofon olarak görünmesi için", "So the phone's audio shows up as a microphone in other apps"));
            AddLabel(L.T("Windows'ta bir sesin Discord, Zoom ya da oyunlarda mikrofon olarak görünmesi için bir sanal ses sürücüsü gerekir. Broken Microphone sesi bu sürücünün girişine verir; diğer programlar da sürücünün çıkışını mikrofon olarak seçer.",
                         "For audio to show up as a microphone in Discord, Zoom or games, Windows needs a virtual audio driver. Broken Microphone plays the audio into the driver's input, and other apps pick the driver's output as their microphone."),
                     Font, Theme.Text, Pad, W - Pad * 2);
            Y += Theme.P(14);
            var bold = new Font(Font, FontStyle.Bold);
            AddLabel(L.T("Şimdilik: VB-CABLE (ücretsiz)", "For now: VB-CABLE (free)"), bold, Theme.Text, Pad, W - Pad * 2);
            Y += Theme.P(2);
            AddLabel(L.T("1. Aşağıdaki düğmeyle VB-Audio'nun sitesini aç ve \"VBCABLE_Driver_Pack\" zip'ini indir.\n2. Zip'i bir klasöre çıkar, VBCABLE_Setup_x64.exe'ye sağ tıkla → Yönetici olarak çalıştır → Install Driver.\n3. Bilgisayarı yeniden başlat ve Broken Microphone'u aç: \"CABLE Input\" kendiliğinden seçilir.",
                         "1. Open VB-Audio's site with the button below and download the \"VBCABLE_Driver_Pack\" zip.\n2. Extract it, right-click VBCABLE_Setup_x64.exe → Run as administrator → Install Driver.\n3. Restart the computer and open Broken Microphone: \"CABLE Input\" is selected automatically."),
                     Font, Theme.Sub, Pad, W - Pad * 2);
            Y += Theme.P(14);
            AddLabel(L.T("Yakında: Broken Microphone sürücüsü", "Coming: the Broken Microphone driver"), bold, Theme.Text, Pad, W - Pad * 2);
            Y += Theme.P(2);
            AddLabel(L.T("Kendi açık kaynak sürücümüz hazırlanıyor. Windows'un onu yüklemesi için Microsoft imzası gerekiyor; imza gelince kurulum paketine eklenecek ve mikrofon \"Broken Microphone\" adıyla görünecek.",
                         "Our own open-source driver is in the works. Windows needs it to be signed by Microsoft; once it is, it will ship in the setup and the microphone will show up as \"Broken Microphone\"."),
                     Font, Theme.Sub, Pad, W - Pad * 2);
            Y += Theme.P(20);

            var dl = new FlatButton(L.T("VB-CABLE sitesini aç", "Open the VB-CABLE site"), BtnKind.Primary);
            dl.Font = Font;
            dl.SetBounds(Pad, Y, Theme.P(230), Theme.P(40));
            dl.Click += delegate { try { System.Diagnostics.Process.Start("https://vb-audio.com/Cable/"); } catch { } };
            Controls.Add(dl);
            var ok = new FlatButton(L.T("Kapat", "Close"), BtnKind.Secondary);
            ok.Font = Font;
            ok.SetBounds(W - Pad - Theme.P(130), Y, Theme.P(130), Theme.P(40));
            ok.Click += delegate { Close(); };
            Controls.Add(ok);
            Y += Theme.P(40) + Pad;
            ClientSize = new Size(W, Y);
        }
    }

    static class Updates
    {
        public static string LatestTag()
        {
            System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;   // TLS 1.2
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(AppInfo.Repo.Replace("https://github.com/", "https://api.github.com/repos/") + "/releases/latest");
            req.UserAgent = "BrokenMicrophone/" + AppInfo.Version;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 10000;
            using (var resp = req.GetResponse())
            using (var rd = new System.IO.StreamReader(resp.GetResponseStream()))
            {
                var m = System.Text.RegularExpressions.Regex.Match(rd.ReadToEnd(), "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                return m.Success ? m.Groups[1].Value : null;
            }
        }

        public static bool IsNewer(string tag, string mine)
        {
            Version a, b;
            if (!Version.TryParse(Norm(tag), out a) || !Version.TryParse(Norm(mine), out b)) return false;
            return a > b;
        }

        static string Norm(string v)
        {
            v = v.Trim().TrimStart('v', 'V');
            int i = v.IndexOfAny(new[] { '-', ' ', '+' });
            if (i > 0) v = v.Substring(0, i);
            if (v.IndexOf('.') < 0) v += ".0";
            return v;
        }
    }
}
