# Text overlays for the kill-highlight videos, same look as overlays.ps1 (Bahnschrift SemiBold, dark outline,
# teal accent). Brand pill + end card come from overlays.ps1 itself so there is one source for the brand.
param([string]$Out, [string]$Hook, [string]$Name, [int]$Total = 0)
Add-Type -AssemblyName System.Drawing
$teal = [Drawing.Color]::FromArgb(29, 211, 176); $white = [Drawing.Color]::FromArgb(240, 244, 248)

function New-Canvas($w, $h) { $b = New-Object Drawing.Bitmap $w, $h; $g = [Drawing.Graphics]::FromImage($b); $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; @($b, $g) }
function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) { $p = New-Object Drawing.Drawing2D.GraphicsPath; $d = $r * 2
  $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90); $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure(); $p }
function Draw-Outlined($g, [string]$text, $font, $brush, [Drawing.RectangleF]$rect, $fmt, [float]$outline) {
  $path = New-Object Drawing.Drawing2D.GraphicsPath ([Drawing.Drawing2D.FillMode]::Winding)
  $path.AddString($text, $font.FontFamily, [int]$font.Style, $font.Size, $rect, $fmt)
  $op = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(210, 0, 0, 0)), $outline; $op.LineJoin = 'Round'
  $g.DrawPath($op, $path); $g.FillPath($brush, $path) }
$center = New-Object Drawing.StringFormat; $center.Alignment = 'Center'; $center.LineAlignment = 'Center'
$font = { param($px) New-Object Drawing.Font 'Bahnschrift SemiBold', $px, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel) }

# Hook title for the top of the frame (first ~3 s).
$b, $g = New-Canvas 1080 360
Draw-Outlined $g $Hook (& $font 78) (New-Object Drawing.SolidBrush $white) (New-Object Drawing.RectangleF 40, 0, 1000, 360) $center 14
$b.Save("$Out\$Name-hook.png"); $g.Dispose(); $b.Dispose()

# Montage counter chips ("1/3" ...), teal pill under the hook area.
for ($i = 1; $i -le $Total; $i++) {
  $b, $g = New-Canvas 300 110
  $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(215, 7, 10, 14))), (RoundRect 2 2 296 106 30))
  $g.DrawPath((New-Object Drawing.Pen $teal, 4), (RoundRect 2 2 296 106 30))
  $g.DrawString(("ELIM {0}/{1}" -f $i, $Total), (& $font 54), (New-Object Drawing.SolidBrush $white), (New-Object Drawing.RectangleF 0, 2, 300, 106), $center)
  $b.Save("$Out\$Name-chip$i.png"); $g.Dispose(); $b.Dispose() }
