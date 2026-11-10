# Draws the MemoryPulse icon (rounded square, green-blue gradient, white pulse line). Dot-sourced by
# make-icon.ps1 (app.ico) and make-msix.ps1 (Store tile images).
Add-Type -AssemblyName System.Drawing

# Returns a Bitmap of $w x $h with the icon ($s px, scaled by $fill) centred on a transparent background.
function New-IconBitmap([int]$w, [int]$h, [double]$fill = 1.0) {
    $bmp = New-Object Drawing.Bitmap $w, $h
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $s = [int][math]::Round([math]::Min($w, $h) * $fill)
    $g.TranslateTransform([single](($w - $s) / 2), [single](($h - $s) / 2))
    $r = [single]($s * 0.22); $d = $r * 2; $e = [single]($s - 1)
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $p.AddArc(0, 0, $d, $d, 180, 90); $p.AddArc($e - $d, 0, $d, $d, 270, 90)
    $p.AddArc($e - $d, $e - $d, $d, $d, 0, 90); $p.AddArc(0, $e - $d, $d, $d, 90, 90); $p.CloseFigure()
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
    return $bmp
}
