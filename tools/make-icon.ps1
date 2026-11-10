# Generates src/MemoryPulse/Resources/app.ico (16-256 px): rounded square with a white pulse line.
# Usage: powershell -File tools/make-icon.ps1
$out = Join-Path $PSScriptRoot '..\src\MemoryPulse\Resources\app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

. (Join-Path $PSScriptRoot 'icon-draw.ps1')

function Draw([int]$s) {
    $bmp = New-IconBitmap $s $s
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
