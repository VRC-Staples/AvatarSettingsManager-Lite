# make-icons.ps1
# Generates 256x256 PNG icons for ASM-Lite menu controls.
# Uses System.Drawing — no external tools required.
# Output: Assets/ASM-Lite/Icons/{Save,Load,Reset,Presets}.png

Add-Type -AssemblyName System.Drawing

$size    = 256
$outDir  = "Assets/ASM-Lite/Icons"
$bgColor = [System.Drawing.Color]::Transparent
$fg      = [System.Drawing.Color]::White

function New-Canvas {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear($bgColor)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    return $bmp, $g
}

function Save-Icon($bmp, $name) {
    $path = Join-Path $outDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "  Saved: $path"
}

function Get-Brush { return New-Object System.Drawing.SolidBrush($fg) }
function Get-Pen($w) { return New-Object System.Drawing.Pen($fg, $w) }

# ── Save icon: floppy disk ────────────────────────────────────────────────────
# Outer rectangle + cutout top-right + inner label window + inner slot
$bmp, $g = New-Canvas
$br  = Get-Brush
$pen = Get-Pen(0)

$margin = 48
$r = $margin  # left
$t = $margin  # top
$w = $size - $margin * 2
$h = $size - $margin * 2

# Outer body (full rect)
$g.FillRectangle($br, $r, $t, $w, $h)

# Cut top-right corner (the "notch") — overdraw with transparent
$notch = 40
$cutBr = New-Object System.Drawing.SolidBrush($bgColor)
$pts = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new($r + $w - $notch, $t),
    [System.Drawing.PointF]::new($r + $w,          $t),
    [System.Drawing.PointF]::new($r + $w,          $t + $notch)
)
$g.FillPolygon($cutBr, $pts)

# Label window (dark inset rectangle, lower portion)
$winBr = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(60, 0, 0, 0))
$winPad = 16
$winTop = $t + $h - 72
$g.FillRectangle($winBr, $r + $winPad, $winTop, $w - $winPad * 2, 56)

# Slot (bright horizontal bar near top — the write-protect slot)
$slotBr = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(180, 0, 0, 0))
$slotLeft  = $r + 20
$slotRight = $r + $w - 60
$slotTop   = $t + 14
$g.FillRectangle($slotBr, $slotLeft, $slotTop, $slotRight - $slotLeft, 28)

Save-Icon $bmp "Save"
$g.Dispose(); $bmp.Dispose()

# ── Load icon: down-arrow into tray ──────────────────────────────────────────
$bmp, $g = New-Canvas
$br  = Get-Brush
$pen = Get-Pen(22)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

# Vertical stem
$cx = $size / 2
$g.DrawLine($pen, $cx, 52, $cx, 158)

# Arrowhead (filled triangle pointing down)
$arrowPts = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new($cx,        170),
    [System.Drawing.PointF]::new($cx - 52,   108),
    [System.Drawing.PointF]::new($cx + 52,   108)
)
$g.FillPolygon($br, $arrowPts)

# Tray (thick horizontal bar at bottom)
$trayPen = Get-Pen(22)
$trayPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$trayPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($trayPen, 60, 196, $size - 60, 196)

Save-Icon $bmp "Load"
$g.Dispose(); $bmp.Dispose()

# ── Reset icon: circular arrow (counter-clockwise arc + arrowhead) ────────────
$bmp, $g = New-Canvas
$pen = Get-Pen(22)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Flat
$br = Get-Brush

$cx = $size / 2; $cy = $size / 2
$radius = 78
$arcRect = [System.Drawing.RectangleF]::new($cx - $radius, $cy - $radius, $radius * 2, $radius * 2)

# Arc from ~30° to ~320° (leaving a gap at top-right for arrowhead)
$g.DrawArc($pen, $arcRect, 40, 280)

# Arrowhead at the end of the arc (~40 degrees from top, clockwise direction)
# Point at arc endpoint, tip of arrow
$endAngleRad = [Math]::PI * 40 / 180
$ex = $cx + $radius * [Math]::Cos($endAngleRad)
$ey = $cy + $radius * [Math]::Sin($endAngleRad)

# Tangent direction at that point (perpendicular to radius, clockwise)
$tx = -[Math]::Sin($endAngleRad)
$ty =  [Math]::Cos($endAngleRad)

$arrowLen = 38
$arrowWid = 22
$tip   = [System.Drawing.PointF]::new($ex + $tx * $arrowLen * 0.5, $ey + $ty * $arrowLen * 0.5)
$left  = [System.Drawing.PointF]::new($ex - $ty * $arrowWid - $tx * $arrowLen * 0.5, $ey + $tx * $arrowWid - $ty * $arrowLen * 0.5)
$right = [System.Drawing.PointF]::new($ex + $ty * $arrowWid - $tx * $arrowLen * 0.5, $ey - $tx * $arrowWid - $ty * $arrowLen * 0.5)

$arrowPts = [System.Drawing.PointF[]]@($tip, $left, $right)
$g.FillPolygon($br, $arrowPts)

Save-Icon $bmp "Reset"
$g.Dispose(); $bmp.Dispose()

# ── Presets icon: three stacked horizontal bars (preset/list symbol) ──────────
$bmp, $g = New-Canvas
$br = Get-Brush

$barW  = 160
$barH  = 28
$left  = ($size - $barW) / 2
$gap   = 24
$totalH = $barH * 3 + $gap * 2
$top   = ($size - $totalH) / 2

for ($i = 0; $i -lt 3; $i++) {
    $y = $top + $i * ($barH + $gap)
    # Rounded rect via filled ellipses + rectangle
    $g.FillEllipse($br, $left,                $y, $barH, $barH)
    $g.FillEllipse($br, $left + $barW - $barH, $y, $barH, $barH)
    $g.FillRectangle($br, $left + $barH/2, $y, $barW - $barH, $barH)
}

# Small dot on the left of each bar (active indicator)
$dotBr = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(80, 0, 0, 0))
$dotR  = 8
for ($i = 0; $i -lt 3; $i++) {
    $y = $top + $i * ($barH + $gap) + ($barH - $dotR * 2) / 2
    $g.FillEllipse($dotBr, $left + 6, $y, $dotR * 2, $dotR * 2)
}

Save-Icon $bmp "Presets"
$g.Dispose(); $bmp.Dispose()

Write-Host ""
Write-Host "Icons generated in $outDir"
