# Generates src/MemoryPulse/Resources/app.ico (16-256 px): rounded square with a white pulse line.
# Usage: powershell -File tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\MemoryPulse\Resources\app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function Draw([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $r = [single]($s * 0.22); $d = $r * 2; $w = [single]($s - 1)
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $p.AddArc(0, 0, $d, $d, 180, 90); $p.AddArc($w - $d, 0, $d, $d, 270, 90)
    $p.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $p.AddArc(0, $w - $d, $d, $d, 90, 90); $p.CloseFigure()
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.PointF]::new(0, 0)), ([Drawing.PointF]::new($s, $s)),
        ([Drawing.Color]::FromArgb(36, 150, 90)), ([Drawing.Color]::FromArgb(30, 100, 180))
    $g.FillPath($brush, $p)
    # pulse line
    $pts = @(@(0.12, 0.55), @(0.32, 0.55), @(0.42, 0.30), @(0.54, 0.76), @(0.64, 0.48), @(0.70, 0.55), @(0.88, 0.55)) |
        ForEach-Object { [Drawing.PointF]::new($_[0] * $s, $_[1] * $s) }
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), ([single][math]::Max(1.5, $s * 0.085))
    $pen.LineJoin = 'Round'; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLines($pen, [Drawing.PointF[]]$pts)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return , $ms.ToArray()
}

$images = $sizes | ForEach-Object { , (Draw $_) }
$fs = [IO.File]::Create($out); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
"Written $out"
