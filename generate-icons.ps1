# generate-icons.ps1
# Generates all Microsoft Store icon assets for Stream Command.
# Run from the StreamCommandWPF root folder:
#   powershell -ExecutionPolicy Bypass -File .\generate-icons.ps1
#
# Output:  StreamCommandPackage\Images\*.png  (all required Store sizes)
# Requires: Windows with .NET / GDI+ available (any modern Windows machine)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Drawing.Imaging

$outputDir = "$PSScriptRoot\StreamCommandPackage\Images"
if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

# ── Color palette ─────────────────────────────────────────────────────────────
$bgTop    = [System.Drawing.Color]::FromArgb(255, 124, 58, 237)   # #7C3AED  purple top
$bgBottom = [System.Drawing.Color]::FromArgb(255,  91, 33, 182)   # #5B21B6  purple bottom
$bgDark   = [System.Drawing.Color]::FromArgb(255,  13, 13,  20)   # #0D0D14  splash bg
$white    = [System.Drawing.Color]::White

# ── Helper: draw one icon at the given pixel size ─────────────────────────────
function New-Icon {
    param([string]$Path, [int]$W, [int]$H, [bool]$IsSplash = $false, [bool]$IsSmall = $false)

    $bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode   = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    if ($IsSplash) {
        # Dark background full bleed
        $g.Clear($bgDark)

        # Centered rounded square (logo area)  ~20% of splash height
        $logoSize = [int]($H * 0.28)
        $lx = [int](($W - $logoSize) / 2)
        $ly = [int](($H - $logoSize) / 2) - [int]($H * 0.06)

        $path2d = New-Object System.Drawing.Drawing2D.GraphicsPath
        $radius = [int]($logoSize * 0.22)
        $path2d.AddArc($lx, $ly, $radius*2, $radius*2, 180, 90)
        $path2d.AddArc($lx + $logoSize - $radius*2, $ly, $radius*2, $radius*2, 270, 90)
        $path2d.AddArc($lx + $logoSize - $radius*2, $ly + $logoSize - $radius*2, $radius*2, $radius*2, 0, 90)
        $path2d.AddArc($lx, $ly + $logoSize - $radius*2, $radius*2, $radius*2, 90, 90)
        $path2d.CloseFigure()

        $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            [System.Drawing.Point]::new($lx, $ly),
            [System.Drawing.Point]::new($lx + $logoSize, $ly + $logoSize),
            $bgTop, $bgBottom)
        $g.FillPath($grad, $path2d)
        $grad.Dispose(); $path2d.Dispose()

        # Play triangle
        $triPad  = [int]($logoSize * 0.28)
        $triX    = $lx + $triPad + [int]($logoSize * 0.05)
        $triY    = $ly + $triPad
        $triW    = $logoSize - $triPad * 2
        $triH    = $logoSize - $triPad * 2
        $pts = @(
            [System.Drawing.PointF]::new($triX, $triY),
            [System.Drawing.PointF]::new($triX + $triW, $triY + $triH / 2),
            [System.Drawing.PointF]::new($triX, $triY + $triH)
        )
        $g.FillPolygon([System.Drawing.Brushes]::White, $pts)

        # App name text below the logo
        $fontSize = [int]($H * 0.065)
        if ($fontSize -lt 8) { $fontSize = 8 }
        $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $sf   = New-Object System.Drawing.StringFormat
        $sf.Alignment = [System.Drawing.StringAlignment]::Center
        $textY = $ly + $logoSize + [int]($H * 0.04)
        $textRect = [System.Drawing.RectangleF]::new(0, $textY, $W, $H - $textY)
        $g.DrawString("Stream Command", $font, [System.Drawing.Brushes]::White, $textRect, $sf)
        $font.Dispose(); $sf.Dispose()

    } else {
        # Rounded square background
        $r = [int]([Math]::Min($W, $H) * 0.18)
        $path2d = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path2d.AddArc(0, 0, $r*2, $r*2, 180, 90)
        $path2d.AddArc($W - $r*2, 0, $r*2, $r*2, 270, 90)
        $path2d.AddArc($W - $r*2, $H - $r*2, $r*2, $r*2, 0, 90)
        $path2d.AddArc(0, $H - $r*2, $r*2, $r*2, 90, 90)
        $path2d.CloseFigure()

        $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            [System.Drawing.Point]::new(0, 0),
            [System.Drawing.Point]::new($W, $H),
            $bgTop, $bgBottom)
        $g.FillPath($grad, $path2d)
        $grad.Dispose(); $path2d.Dispose()

        # Play triangle — centered, with slight right nudge
        $pad  = [int]([Math]::Min($W,$H) * 0.26)
        $nudge = [int]([Math]::Min($W,$H) * 0.04)
        $tX   = $pad + $nudge
        $tY   = $pad
        $tW   = $W - $pad * 2
        $tH   = $H - $pad * 2

        if ($IsSmall -and [Math]::Min($W,$H) -le 24) {
            # For tiny sizes, draw a simpler solid play triangle without padding fuss
            $tX = [int]($W * 0.30)
            $tY = [int]($H * 0.20)
            $tW = [int]($W * 0.50)
            $tH = [int]($H * 0.60)
        }

        $pts = @(
            [System.Drawing.PointF]::new($tX, $tY),
            [System.Drawing.PointF]::new($tX + $tW, $tY + $tH / 2),
            [System.Drawing.PointF]::new($tX, $tY + $tH)
        )
        $g.FillPolygon([System.Drawing.Brushes]::White, $pts)
    }

    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  Created: $(Split-Path $Path -Leaf) ($W x $H)"
}

Write-Host "Generating Stream Command icons..."
Write-Host ""

# ── Square tile icons ─────────────────────────────────────────────────────────
New-Icon "$outputDir\Square44x44Logo.png"          44   44
New-Icon "$outputDir\Square44x44Logo.scale-200.png" 88  88
New-Icon "$outputDir\Square71x71Logo.png"           71   71
New-Icon "$outputDir\Square71x71Logo.scale-200.png" 142 142
New-Icon "$outputDir\Square150x150Logo.png"         150 150
New-Icon "$outputDir\Square150x150Logo.scale-200.png" 300 300
New-Icon "$outputDir\Square310x310Logo.png"         310 310
New-Icon "$outputDir\Square310x310Logo.scale-200.png" 620 620

# ── Wide tile ─────────────────────────────────────────────────────────────────
New-Icon "$outputDir\Wide310x150Logo.png"           310 150
New-Icon "$outputDir\Wide310x150Logo.scale-200.png" 620 300

# ── Store logo (50×50 square, used on Store listing page) ─────────────────────
New-Icon "$outputDir\StoreLogo.png"                  50  50
New-Icon "$outputDir\StoreLogo.scale-200.png"       100 100

# ── Splash screen ─────────────────────────────────────────────────────────────
New-Icon "$outputDir\SplashScreen.png"              620 300 -IsSplash $true
New-Icon "$outputDir\SplashScreen.scale-200.png"   1240 600 -IsSplash $true

# ── Target-size variants (taskbar, title bar, notification icons) ─────────────
New-Icon "$outputDir\Square44x44Logo.targetsize-16.png"  16  16 -IsSmall $true
New-Icon "$outputDir\Square44x44Logo.targetsize-24.png"  24  24 -IsSmall $true
New-Icon "$outputDir\Square44x44Logo.targetsize-32.png"  32  32 -IsSmall $true
New-Icon "$outputDir\Square44x44Logo.targetsize-48.png"  48  48
New-Icon "$outputDir\Square44x44Logo.targetsize-256.png" 256 256

Write-Host ""
Write-Host "Done! All icons saved to: $outputDir"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Open StreamCommandPackage.wapproj in Visual Studio"
Write-Host "  2. Right-click the package project > Store > Associate with the Store"
Write-Host "  3. Build > Create App Packages"
Write-Host "  4. Upload the .msixupload file to Partner Center"
