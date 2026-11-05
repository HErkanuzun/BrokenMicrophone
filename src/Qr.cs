using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace BrokenMicrophone
{
    // Küçük bir QR kod üreticisi: bayt kipi, M hata düzeltme seviyesi, sürüm 1-40.
    // Algoritma Project Nayuki'nin "QR Code generator library" (MIT) yapısını izler.
    class Qr
    {
        public readonly int Size;
        readonly bool[,] mods, func;
        readonly int ver;

        // M seviyesi tabloları (indeks = sürüm)
        static readonly int[] EccPerBlock = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 };
        static readonly int[] Blocks = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 };

        public bool this[int x, int y] { get { return mods[y, x]; } }

        public Qr(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            for (ver = 1; ; ver++)
            {
                if (ver > 40) throw new ArgumentException("text too long");
                int ccBits = ver <= 9 ? 8 : 16;
                if (4 + ccBits + data.Length * 8 <= DataCodewords(ver) * 8) break;
            }
            Size = ver * 4 + 17;
            mods = new bool[Size, Size];
            func = new bool[Size, Size];

            // veri bitleri
            var bits = new List<bool>();
            Append(bits, 4, 4);
            Append(bits, data.Length, ver <= 9 ? 8 : 16);
            foreach (byte b in data) Append(bits, b, 8);
            int cap = DataCodewords(ver) * 8;
            Append(bits, 0, Math.Min(4, cap - bits.Count));
            Append(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);
            var cw = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) cw[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            DrawFunctionPatterns();
            DrawCodewords(AddEccAndInterleave(cw));

            // en düşük cezalı maske seçilir
            int best = 0, bestPenalty = int.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(m);
                DrawFormatBits(m);
                int p = Penalty();
                if (p < bestPenalty) { best = m; bestPenalty = p; }
                ApplyMask(m);   // geri al (XOR)
            }
            ApplyMask(best);
            DrawFormatBits(best);
        }

        static void Append(List<bool> bits, int val, int len)
        {
            for (int i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0);
        }

        static int RawDataModules(int v)
        {
            int r = (16 * v + 128) * v + 64;
            if (v >= 2)
            {
                int na = v / 7 + 2;
                r -= (25 * na - 10) * na - 55;
                if (v >= 7) r -= 36;
            }
            return r;
        }

        static int DataCodewords(int v) { return RawDataModules(v) / 8 - EccPerBlock[v] * Blocks[v]; }

        // ------------------------------------------------------------------ desenler
        void Set(int x, int y, bool dark) { mods[y, x] = dark; func[y, x] = true; }

        void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++) { Set(6, i, i % 2 == 0); Set(i, 6, i % 2 == 0); }
            Finder(3, 3); Finder(Size - 4, 3); Finder(3, Size - 4);
            var al = AlignmentPositions();
            int n = al.Length;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            Set(al[i] + dx, al[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            DrawFormatBits(0);
            if (ver >= 7)
            {
                int rem = ver;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                int b = ver << 12 | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((b >> i) & 1) != 0;
                    int a = Size - 11 + i % 3, c = i / 3;
                    Set(a, c, bit);
                    Set(c, a, bit);
                }
            }
        }

        void Finder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy)), xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) Set(xx, yy, d != 2 && d != 4);
                }
        }

        int[] AlignmentPositions()
        {
            if (ver == 1) return new int[0];
            int n = ver / 7 + 2;
            int step = ver == 32 ? 26 : (ver * 4 + n * 2 + 1) / (n * 2 - 2) * 2;
            var r = new int[n];
            r[0] = 6;
            for (int i = n - 1, pos = Size - 7; i >= 1; i--, pos -= step) r[i] = pos;
            return r;
        }

        void DrawFormatBits(int mask)
        {
            int data = (0 << 3) | mask;     // M seviyesi = 00
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int b = (data << 10 | rem) ^ 0x5412;
            for (int i = 0; i <= 5; i++) Set(8, i, Bit(b, i));
            Set(8, 7, Bit(b, 6));
            Set(8, 8, Bit(b, 7));
            Set(7, 8, Bit(b, 8));
            for (int i = 9; i < 15; i++) Set(14 - i, 8, Bit(b, i));
            for (int i = 0; i < 8; i++) Set(Size - 1 - i, 8, Bit(b, i));
            for (int i = 8; i < 15; i++) Set(8, Size - 15 + i, Bit(b, i));
            Set(8, Size - 8, true);
        }

        static bool Bit(int x, int i) { return ((x >> i) & 1) != 0; }

        // ------------------------------------------------------------------ hata düzeltme
        byte[] AddEccAndInterleave(byte[] data)
        {
            int nb = Blocks[ver], eccLen = EccPerBlock[ver];
            int raw = RawDataModules(ver) / 8;
            int shortCount = nb - raw % nb, shortLen = raw / nb;
            var div = RsDivisor(eccLen);
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < nb; i++)
            {
                int dl = shortLen - eccLen + (i < shortCount ? 0 : 1);
                var dat = new byte[dl];
                Array.Copy(data, k, dat, 0, dl);
                k += dl;
                var block = new byte[shortLen + 1];
                Array.Copy(dat, block, dl);
                var ecc = RsRemainder(dat, div);
                Array.Copy(ecc, 0, block, block.Length - eccLen, eccLen);
                blocks.Add(block);
            }
            var result = new List<byte>(raw);
            for (int i = 0; i < blocks[0].Length; i++)
                for (int j = 0; j < blocks.Count; j++)
                    if (i != shortLen - eccLen || j >= shortCount) result.Add(blocks[j][i]);
            return result.ToArray();
        }

        static byte[] RsDivisor(int degree)
        {
            var r = new byte[degree];
            r[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    r[j] = (byte)Mul(r[j], root);
                    if (j + 1 < degree) r[j] ^= r[j + 1];
                }
                root = Mul(root, 0x02);
            }
            return r;
        }

        static byte[] RsRemainder(byte[] data, byte[] div)
        {
            var r = new byte[div.Length];
            foreach (byte b in data)
            {
                int f = b ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1);
                r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(div[i], f);
            }
            return r;
        }

        static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }

        // ------------------------------------------------------------------ yerleştirme
        void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool up = ((right + 1) & 2) == 0;
                        int y = up ? Size - 1 - vert : vert;
                        if (!func[y, x] && i < data.Length * 8)
                        {
                            mods[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
            }
        }

        void ApplyMask(int m)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (func[y, x]) continue;
                    bool inv;
                    switch (m)
                    {
                        case 0: inv = (x + y) % 2 == 0; break;
                        case 1: inv = y % 2 == 0; break;
                        case 2: inv = x % 3 == 0; break;
                        case 3: inv = (x + y) % 3 == 0; break;
                        case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: inv = x * y % 2 + x * y % 3 == 0; break;
                        case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (inv) mods[y, x] = !mods[y, x];
                }
        }

        // QR standardındaki dört ceza kuralı (okunabilirlik)
        int Penalty()
        {
            int p = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < Size; a++)
                {
                    int run = 1;
                    for (int b = 1; b <= Size; b++)
                    {
                        bool same = b < Size && M(pass, a, b) == M(pass, a, b - 1);
                        if (same) run++;
                        else { if (run >= 5) p += run - 2; run = 1; }
                    }
                    // 1:1:3:1:1 bulucu benzeri desen, iki yanında 4 açık modül
                    for (int b = 0; b + 6 < Size; b++)
                    {
                        if (!(M(pass, a, b) && !M(pass, a, b + 1) && M(pass, a, b + 2) && M(pass, a, b + 3) && M(pass, a, b + 4) && !M(pass, a, b + 5) && M(pass, a, b + 6))) continue;
                        bool before = true, after = true;
                        for (int k = 1; k <= 4; k++)
                        {
                            if (b - k >= 0 && M(pass, a, b - k)) before = false;
                            if (b + 6 + k < Size && M(pass, a, b + 6 + k)) after = false;
                        }
                        if (before || after) p += 40;
                    }
                }
            int dark = 0;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (mods[y, x]) dark++;
                    if (x + 1 < Size && y + 1 < Size && mods[y, x] == mods[y, x + 1] && mods[y, x] == mods[y + 1, x] && mods[y, x] == mods[y + 1, x + 1]) p += 3;
                }
            int total = Size * Size;
            int k2 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            p += Math.Max(0, k2) * 10;
            return p;
        }

        bool M(int pass, int a, int b) { return pass == 0 ? mods[a, b] : mods[b, a]; }

        // Kenarda 4 modül boşluklu, keskin (bulanıklaşmayan) resim
        public Bitmap Render(int px, Color dark, Color light)
        {
            int n = Size + 8;
            int scale = Math.Max(1, px / n);
            var bmp = new Bitmap(n * scale, n * scale);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(light);
                using (var br = new SolidBrush(dark))
                    for (int y = 0; y < Size; y++)
                        for (int x = 0; x < Size; x++)
                            if (mods[y, x]) g.FillRectangle(br, (x + 4) * scale, (y + 4) * scale, scale, scale);
            }
            return bmp;
        }
    }
}
