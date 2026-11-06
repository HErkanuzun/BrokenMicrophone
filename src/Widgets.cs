using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BrokenMicrophone
{
    // QR kod kartı: beyaz zemin üstünde keskin kod, altında adres
    class QrView : Ctl
    {
        Qr qr;
        string url = "";
        public string Caption = "";

        public QrView() { Cursor = Cursors.Default; }

        public string Url
        {
            get { return url; }
            set
            {
                if (value == url) return;
                url = value ?? "";
                try { qr = url == "" ? null : new Qr(url); } catch { qr = null; }
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Glass.Card(g, r, Theme.P(22), Color.Empty);

            float pad = Theme.P(18), capH = Caption == "" ? 0 : Font.Height + Theme.P(14);
            float side = Math.Min(Width - pad * 2, Height - pad * 2 - capH);
            var box = new RectangleF((Width - side) / 2f, pad, side, side);
            Theme.Fill(g, box, Theme.P(10), Color.White);
            if (qr != null)
            {
                int n = qr.Size + 6;   // 3 modül kenar boşluğu
                int cell = (int)Math.Floor(side / n);
                float codeSide = cell * n;
                float ox = (float)Math.Round(box.X + (side - codeSide) / 2f + cell * 3), oy = (float)Math.Round(box.Y + (side - codeSide) / 2f + cell * 3);
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var b = new SolidBrush(Color.FromArgb(16, 18, 22)))
                    for (int y = 0; y < qr.Size; y++)
                        for (int x = 0; x < qr.Size; x++)
                            if (qr[x, y]) g.FillRectangle(b, ox + x * cell, oy + y * cell, cell, cell);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Default;
            }
            if (capH > 0)
                Theme.Draw(g, Caption, Font, new RectangleF(pad, box.Bottom + Theme.P(8), Width - pad * 2, Font.Height + Theme.P(4)), Theme.Sub,
                    StringAlignment.Center, StringAlignment.Near, false);
        }
    }

    // Yatay ses seviyesi çubuğu
    class LevelMeter : Ctl
    {
        float level, hold;
        public bool Muted;

        public LevelMeter() { Cursor = Cursors.Default; }

        public void Push(float v)
        {
            v = Math.Max(0f, Math.Min(1f, v));
            level = Math.Max(v, level * 0.75f);
            hold = Math.Max(level, hold - 0.01f);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            float h = Height, rad = h / 2f;
            var r = new RectangleF(0, 0, Width - 1, h - 1);
            Theme.Fill(g, r, rad, Color.FromArgb(30, 255, 255, 255));
            // dB'ye yakın görünüm: kısık sesler de görünsün
            float shown = level <= 0.0005f ? 0 : (float)Math.Max(0, 1 + Math.Log10(level) / 2.6);
            if (shown > 0.01f)
            {
                Color c = Muted ? Theme.Sub : shown > 0.92f ? Theme.Warn : Theme.Accent;
                Theme.Fill(g, new RectangleF(0, 0, Math.Max(h, (Width - 1) * shown), h - 1), rad, c);
            }
            float hs = hold <= 0.0005f ? 0 : (float)Math.Max(0, 1 + Math.Log10(hold) / 2.6);
            if (hs > 0.02f)
                using (var p = new Pen(Color.FromArgb(150, Theme.Text), Theme.P(2)))
                    g.DrawLine(p, (Width - 1) * hs - Theme.P(2), Theme.P(2), (Width - 1) * hs - Theme.P(2), h - Theme.P(3));
        }
    }

    // Bağlı telefonların listesi; tıklanan cihaz sesi veren cihaz olur
    class ClientList : Ctl
    {
        public List<Client> Items = new List<Client>();
        public int ActiveId;
        public event Action<Client> Picked;
        int hoverRow = -1;

        public ClientList() { }

        int RowH { get { return Theme.P(58); } }

        public int Measure() { return Math.Max(1, Items.Count) * RowH + Theme.P(2); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = e.Y / RowH;
            if (i >= Items.Count) i = -1;
            if (i != hoverRow) { hoverRow = i; Invalidate(); }
            Cursor = i >= 0 && Items[i].Id != ActiveId ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverRow = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = e.Y / RowH;
            if (i >= 0 && i < Items.Count && Picked != null) Picked(Items[i]);
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            var all = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Glass.Card(g, all, Theme.P(18), Color.Empty);
            if (Items.Count == 0)
            {
                Theme.Draw(g, L.T("Henüz bağlı cihaz yok", "No device connected yet"), Font, new RectangleF(Theme.P(16), 0, Width - Theme.P(32), RowH), Theme.Sub,
                    StringAlignment.Near, StringAlignment.Center, false);
                return;
            }
            using (var small = new Font(Font.FontFamily, Font.Size * 0.88f))
            using (var bold = new Font(Font, FontStyle.Bold))
                for (int i = 0; i < Items.Count; i++)
                {
                    var c = Items[i];
                    var row = new RectangleF(Theme.P(4), i * RowH + Theme.P(4), Width - Theme.P(9), RowH - Theme.P(7));
                    bool active = c.Id == ActiveId;
                    if (active || i == hoverRow) Theme.Fill(g, row, Theme.P(14), active ? Color.FromArgb(40, Theme.Accent) : Color.FromArgb(18, 255, 255, 255));
                    Color dot = c.Muted ? Theme.Danger : c.Sending ? Theme.Ok : Theme.Sub;
                    float d = Theme.P(10), cy = row.Y + row.Height / 2f;
                    using (var b = new SolidBrush(dot)) g.FillEllipse(b, row.X + Theme.P(12), cy - d / 2, d, d);
                    float tx = row.X + Theme.P(34);
                    Theme.Draw(g, c.Name, bold, new RectangleF(tx, row.Y + Theme.P(7), row.Width - Theme.P(150), Font.Height + 2), Theme.Text, StringAlignment.Near, StringAlignment.Near, false);
                    string sub = (c.Mic != "" ? c.Mic + "  ·  " : "") + (c.Rate > 0 ? (c.Rate / 1000.0).ToString("0.#") + " kHz" : "") + "  ·  " + c.Address;
                    Theme.Draw(g, sub, small, new RectangleF(tx, row.Y + Theme.P(9) + Font.Height, row.Width - Theme.P(150), small.Height + 2), Theme.Sub, StringAlignment.Near, StringAlignment.Near, false);
                    string state = active ? (c.Muted ? L.T("Sessizde", "Muted") : c.Sending ? L.T("Ses veriyor", "Live") : L.T("Bekliyor", "Waiting"))
                                          : L.T("Bunu kullan", "Use this");
                    Theme.Draw(g, state, small, new RectangleF(row.Right - Theme.P(130), row.Y, Theme.P(118), row.Height),
                        active ? (c.Muted ? Theme.Danger : c.Sending ? Theme.Ok : Theme.Sub) : Theme.Accent, StringAlignment.Far, StringAlignment.Center, false);
                }
        }
    }

    // Koyu temalı açılır liste
    class DarkCombo : ComboBox
    {
        public DarkCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            ItemHeight = Theme.P(26);
            IntegralHeight = false;
            MaxDropDownItems = 12;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(sel ? Theme.Mix(Theme.Field, Theme.Accent, 0.35f) : Theme.Field)) e.Graphics.FillRectangle(b, e.Bounds);
            var r = new Rectangle(e.Bounds.X + Theme.P(8), e.Bounds.Y, e.Bounds.Width - Theme.P(10), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, r, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
