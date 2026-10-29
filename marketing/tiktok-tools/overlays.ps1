# Renders the PNG overlays for the Stream Command TikTok videos (1080x1920 canvas).
param([string]$Out)
Add-Type -AssemblyName System.Drawing
$teal = [Drawing.Color]::FromArgb(29, 211, 176); $ink = [Drawing.Color]::FromArgb(6, 42, 36)
$bg = [Drawing.Color]::FromArgb(7, 10, 14); $white = [Drawing.Color]::FromArgb(240, 244, 248); $grey = [Drawing.Color]::FromArgb(167, 182, 197)

function New-Canvas($w, $h) { $b = New-Object Drawing.Bitmap $w, $h; $g = [Drawing.Graphics]::FromImage($b); $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'; @($b, $g) }
function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) { $p = New-Object Drawing.Drawing2D.GraphicsPath; $d = $r * 2
  $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90); $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure(); $p }
# The app mark: teal rounded square with broadcast arcs (same as the website favicon).
function Draw-Logo($g, [float]$x, [float]$y, [float]$s) {
  $g.FillPath((New-Object Drawing.SolidBrush $teal), (RoundRect $x $y $s $s ($s * 0.22)))
  $pen = New-Object Drawing.Pen $ink, ($s * 0.07); $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
  $cx = $x + $s / 2; $cy = $y + $s / 2
  foreach ($r in @(($s * 0.25), ($s * 0.39))) { $g.DrawArc($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r, 135, 90); $g.DrawArc($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r, -45, 90) }
  $d = $s * 0.17; $g.FillEllipse((New-Object Drawing.SolidBrush $ink), $cx - $d / 2, $cy - $d / 2, $d, $d) }
# Bold text with a soft dark outline so it reads over bright gameplay.
function Draw-Outlined($g, [string]$text, $font, $brush, [Drawing.RectangleF]$rect, $fmt, [float]$outline) {
  $path = New-Object Drawing.Drawing2D.GraphicsPath ([Drawing.Drawing2D.FillMode]::Winding)
  $path.AddString($text, $font.FontFamily, [int]$font.Style, $font.Size, $rect, $fmt)
  $op = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(210, 0, 0, 0)), $outline; $op.LineJoin = 'Round'
  $g.DrawPath($op, $path); $g.FillPath($brush, $path) }

$fam = New-Object Drawing.FontFamily 'Bahnschrift'
$center = New-Object Drawing.StringFormat; $center.Alignment = 'Center'; $center.LineAlignment = 'Center'

# 1) Brand pill, bottom-left during the clip
$b, $g = New-Canvas 470 96
$g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(205, 7, 10, 14))), (RoundRect 1 1 466 92 22))
$g.DrawPath((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(160, 29, 211, 176)), 2), (RoundRect 1 1 466 92 22))
Draw-Logo $g 16 16 62
$g.DrawString('STREAM COMMAND', (New-Object Drawing.Font 'Bahnschrift SemiBold', 30, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)), (New-Object Drawing.SolidBrush $white), 92, 28)
$b.Save("$Out\pill.png"); $g.Dispose(); $b.Dispose()

# 2) Hook titles (top of frame), one per video
$hooks = @{
  'hook1' = "every streamer has lost`na fight to the OBS window"
  'hook2' = "your stream shouldn't`nneed an alt-tab"
  'hook3' = "mid-fight is not the time`nto alt-tab to OBS"
  'hook4' = "eyes on the fight.`nnot on OBS."
}
foreach ($k in $hooks.Keys) {
  $b, $g = New-Canvas 1080 360
  Draw-Outlined $g $hooks[$k] (New-Object Drawing.Font 'Bahnschrift SemiBold', 78, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)) (New-Object Drawing.SolidBrush $white) (New-Object Drawing.RectangleF 40, 0, 1000, 360) $center 14
  $b.Save("$Out\$k.png"); $g.Dispose(); $b.Dispose() }

# 3) Shortcut callout: keycaps + the app's real "now on program" notice
$b, $g = New-Canvas 1000 300
$kf = New-Object Drawing.Font 'Bahnschrift SemiBold', 50, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
$caps = 'Ctrl','Shift','F2'; $total = ($caps | % { [Math]::Max(110, $g.MeasureString($_, $kf).Width + 40) } | Measure-Object -Sum).Sum + 22 * ($caps.Count - 1); $x = (1000 - $total) / 2
foreach ($cap in 'Ctrl','Shift','F2') {
  $w = [Math]::Max(110, $g.MeasureString($cap, $kf).Width + 40)
  $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(235, 15, 23, 32))), (RoundRect $x 10 $w 96 16))
  $g.DrawPath((New-Object Drawing.Pen $teal, 4), (RoundRect $x 10 $w 96 16))
  $g.DrawString($cap, $kf, (New-Object Drawing.SolidBrush $white), (New-Object Drawing.RectangleF $x, 10, $w, 96), $center); $x += $w + 22 }
$g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(240, 15, 23, 32))), (RoundRect 230 140 540 140 24))
$g.DrawPath((New-Object Drawing.Pen $teal, 4), (RoundRect 230 140 540 140 24))
$g.FillEllipse((New-Object Drawing.SolidBrush $teal), 268, 188, 44, 44)
$g.DrawString('Gameplay', (New-Object Drawing.Font 'Bahnschrift SemiBold', 50, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)), (New-Object Drawing.SolidBrush $white), 336, 156)
$g.DrawString('Now on program', (New-Object Drawing.Font 'Bahnschrift', 34, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)), (New-Object Drawing.SolidBrush $grey), 338, 216)
$b.Save("$Out\callout.png"); $g.Dispose(); $b.Dispose()

# 4) End card
$b, $g = New-Canvas 1080 1920
$g.Clear($bg)
$glow = New-Object Drawing.Drawing2D.GraphicsPath; $glow.AddEllipse(90, 340, 900, 900)
$pgb = New-Object Drawing.Drawing2D.PathGradientBrush $glow; $pgb.CenterColor = [Drawing.Color]::FromArgb(70, 29, 211, 176); $pgb.SurroundColors = @([Drawing.Color]::FromArgb(0, 29, 211, 176)); $g.FillPath($pgb, $glow)
Draw-Logo $g 430 560 220
$g.DrawString('Stream Command', (New-Object Drawing.Font 'Bahnschrift SemiBold', 92, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)), (New-Object Drawing.SolidBrush $white), (New-Object Drawing.RectangleF 0, 830, 1080, 120), $center)
$g.DrawString("Run your whole stream`nwithout leaving your game.", (New-Object Drawing.Font 'Bahnschrift', 56, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)), (New-Object Drawing.SolidBrush $grey), (New-Object Drawing.RectangleF 40, 960, 1000, 160), $center)
$g.DrawString(('Scenes  {0}  Mic  {0}  Chat  {0}  Go Live' -f [char]0x00B7), (New-Object Drawing.Font 'Bahnschrift SemiBold', 46, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)), (New-Object Drawing.SolidBrush $teal), (New-Object Drawing.RectangleF 0, 1150, 1080, 70), $center)
$g.FillPath((New-Object Drawing.SolidBrush $teal), (RoundRect 150 1290 780 120 28))
$g.DrawString('streamcommand.netlify.app', (New-Object Drawing.Font 'Bahnschrift SemiBold', 52, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)), (New-Object Drawing.SolidBrush $ink), (New-Object Drawing.RectangleF 150, 1290, 780, 120), $center)
$g.DrawString('Windows app for Twitch + OBS streamers', (New-Object Drawing.Font 'Bahnschrift', 38, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)), (New-Object Drawing.SolidBrush $grey), (New-Object Drawing.RectangleF 0, 1450, 1080, 60), $center)
$g.DrawString('Gameplay: Fortnite. Not affiliated with or endorsed by Epic Games.', (New-Object Drawing.Font 'Bahnschrift', 26, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)), (New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(124, 139, 154))), (New-Object Drawing.RectangleF 0, 1780, 1080, 50), $center)
$b.Save("$Out\endcard.png"); $g.Dispose(); $b.Dispose()
"done"
