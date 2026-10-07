# assets/penwin.ico üretir: koyu zemin üzerinde turuncu kalem ve mavi iz.
# PNG gömülü ICO (Windows Vista+), 256/48/32/16 boyutları. Çalıştırma: powershell -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.ScaleTransform($size / 256.0, $size / 256.0)

    # Yuvarlatılmış koyu kare
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 56; $w = 240; $o = 8
    $path.AddArc($o, $o, $r, $r, 180, 90)
    $path.AddArc($o + $w - $r, $o, $r, $r, 270, 90)
    $path.AddArc($o + $w - $r, $o + $w - $r, $r, $r, 0, 90)
    $path.AddArc($o, $o + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 18, 20, 23))), $path)

    # Mavi iz (kalemin çizdiği eğri)
    $ink = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 127, 220, 255)), 14
    $ink.StartCap = 'Round'; $ink.EndCap = 'Round'
    $g.DrawBezier($ink, 44, 196, 84, 150, 112, 214, 150, 176)

    # Kalem: döndürülmüş gövde + uç
    $g.TranslateTransform(160, 160)
    $g.RotateTransform(-45)
    $orange = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 138, 61))
    $g.FillRectangle($orange, -20, -118, 40, 112)
    $tip = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF -20, -6),
        (New-Object System.Drawing.PointF 20, -6),
        (New-Object System.Drawing.PointF 0, 34))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 232, 230, 225))), $tip)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 27, 15, 5))), -20, -98, 40, 8)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 256, 48, 32, 16
$images = @($sizes | ForEach-Object { , (New-IconPng $_) })

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$images[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()

$dest = Join-Path $PSScriptRoot '..\assets\penwin.ico'
New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
[System.IO.File]::WriteAllBytes($dest, $out.ToArray())
Write-Output "yazıldı: $dest ($($out.Length) bayt)"
