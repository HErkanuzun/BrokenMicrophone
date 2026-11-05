# Broken Microphone - logo.png ve app.ico üretir (ailenin tarzında: koyu gövde, turkuaz mikrofon, kırmızı çizgili bozuk kısım)
# Kullanım: powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'

function Round([System.Drawing.Drawing2D.GraphicsPath]$p, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure()
}

function Draw([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $k = $S / 256.0

    $body = [System.Drawing.Color]::FromArgb(40, 44, 52)
    $edge = [System.Drawing.Color]::FromArgb(70, 76, 88)
    $teal = [System.Drawing.Color]::FromArgb(46, 196, 176)
    $red = [System.Drawing.Color]::FromArgb(222, 62, 66)
    $red2 = [System.Drawing.Color]::FromArgb(150, 30, 36)
    $lens = [System.Drawing.Color]::FromArgb(234, 237, 242)

    # gövde
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    Round $p (8 * $k) (8 * $k) (240 * $k) (240 * $k) (44 * $k)
    $g.FillPath((New-Object System.Drawing.SolidBrush $body), $p)
    $g.DrawPath((New-Object System.Drawing.Pen $edge, ([Math]::Max(1, 6 * $k))), $p)

    # mikrofon kapsülü: üst kısmı sağlam (turkuaz), alt kısmı bozuk (kırmızı çizgili) - telefon onun yerine geçer
    $cw = 76 * $k; $ch = 118 * $k; $cx = 128 * $k - $cw / 2; $cy = 30 * $k
    $cap = New-Object System.Drawing.Drawing2D.GraphicsPath
    Round $cap $cx $cy $cw $ch ($cw / 2)
    $g.FillPath((New-Object System.Drawing.SolidBrush $teal), $cap)
    $split = $cy + $ch * 0.58
    $g.SetClip($cap)
    $hb = New-Object System.Drawing.Drawing2D.HatchBrush ([System.Drawing.Drawing2D.HatchStyle]::WideUpwardDiagonal), $red2, $red
    if ($S -ge 48) { $g.FillRectangle($hb, $cx - 2, $split, $cw + 4, $ch) } else { $g.FillRectangle((New-Object System.Drawing.SolidBrush $red), $cx - 2, $split, $cw + 4, $ch) }
    $g.ResetClip()
    # ayak: yay, gövde ve taban
    $pw = [Math]::Max(1.5, 14 * $k)
    $pen = New-Object System.Drawing.Pen $lens, $pw
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $aw = 128 * $k
    $g.DrawArc($pen, 128 * $k - $aw / 2, 76 * $k, $aw, 110 * $k, 0, 180)
    $g.DrawLine($pen, 128 * $k, 186 * $k, 128 * $k, 214 * $k)
    $g.DrawLine($pen, 98 * $k, 216 * $k, 158 * $k, 216 * $k)
    $g.Dispose()
    return $bmp
}

$big = Draw 256
$big.Save((Join-Path $src 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# ICO: her boyut PNG olarak gömülür (Windows Vista+ destekler)
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $b = Draw $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $src 'app.ico'), $out.ToArray())
Write-Host "logo.png ve app.ico hazır: $src"
