<#
.SYNOPSIS
    Рисует «хром» UI-пака: панели, кнопки, слоты, полосы, переключатели.

.DESCRIPTION
    Стиль утверждён владельцем 15 сентября 2026 (синие панели, кремовые
    карточки, коралловые кнопки, срезанные углы). Концепты:
    ART/UI/concepts-2026-09-15/v2-*.png и v3-*.png.

    Простая геометрия рисуется здесь, а не генератором картинок: ей нужны
    ровные симметричные углы, чтобы Unity растягивал её 9-slice без
    искажений. Рисованные части (иконки, ленты) лежат отдельно в art/.

    Всё рисуется в 2× и сохраняется PNG с прозрачностью. Границы 9-slice
    для каждого спрайта пишутся в slices.json — редакторный импортёр
    проставляет их в Sprite Editor автоматически.
#>
param([string] $OutDir = '')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if ($OutDir -eq '') { $OutDir = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'ART\UI\kit-2026-09-15\chrome' }
[IO.Directory]::CreateDirectory($OutDir) | Out-Null

# ---- палитра (снята с концептов) ----
function C([string]$hex, [int]$a = 255) { $h = $hex.TrimStart('#'); [Drawing.Color]::FromArgb($a, [Convert]::ToInt32($h.Substring(0,2),16), [Convert]::ToInt32($h.Substring(2,2),16), [Convert]::ToInt32($h.Substring(4,2),16)) }
$NavyTop    = C '0F3A68'
$NavyBottom = C '0A2B52'
$NavyDeep   = C '082546'
$Outline    = C '6E9FC4'
$OutlineSoft= C '82B2D4' 150
$Cream      = C 'F0E7DC'
$CreamEdge  = C 'C9B9A6'
$CoralTop   = C 'DE6168'
$CoralBottom= C 'C54B54'
$CoralEdge  = C 'F4A7A3'
$Cyan       = C '97EBFD'
$CyanFill   = C '1FBFFD'
$Track      = C '082D52'
$Teal       = C '5FC7A6'
$Copper     = C 'E67965'
$Disabled   = C '3A5068'

$slices = [ordered]@{}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object Drawing.Bitmap $w, $h, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)
    return @($bmp, $g)
}

# Прямоугольник со срезанными углами.
function Chamfer([double]$x, [double]$y, [double]$w, [double]$h, [double]$c) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $pts = @(
        [Drawing.PointF]::new($x + $c, $y), [Drawing.PointF]::new($x + $w - $c, $y),
        [Drawing.PointF]::new($x + $w, $y + $c), [Drawing.PointF]::new($x + $w, $y + $h - $c),
        [Drawing.PointF]::new($x + $w - $c, $y + $h), [Drawing.PointF]::new($x + $c, $y + $h),
        [Drawing.PointF]::new($x, $y + $h - $c), [Drawing.PointF]::new($x, $y + $c))
    $p.AddPolygon($pts); return $p
}

# Кнопка-«капсула» с острыми боковыми носиками, как в концептах.
function Pointed([double]$x, [double]$y, [double]$w, [double]$h, [double]$tip) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $m = $y + $h / 2
    $pts = @(
        [Drawing.PointF]::new($x + $tip, $y), [Drawing.PointF]::new($x + $w - $tip, $y),
        [Drawing.PointF]::new($x + $w, $m), [Drawing.PointF]::new($x + $w - $tip, $y + $h),
        [Drawing.PointF]::new($x + $tip, $y + $h), [Drawing.PointF]::new($x, $m))
    $p.AddPolygon($pts); return $p
}

function Fill-Gradient($g, $path, $top, $bottom, [double]$y, [double]$h) {
    $rect = [Drawing.RectangleF]::new(0, $y, 10, $h)
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush $rect, $top, $bottom, 90.0
    $g.FillPath($brush, $path); $brush.Dispose()
}

function Stroke($g, $path, $color, [double]$width) {
    $pen = New-Object Drawing.Pen $color, $width
    $pen.LineJoin = 'Miter'; $g.DrawPath($pen, $path); $pen.Dispose()
}

function Glow($g, $pathFactory, $color, [int]$steps, [double]$maxWidth) {
    for ($i = $steps; $i -ge 1; $i--) {
        $a = [int](70 * (1 - ($i - 1) / $steps))
        $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb($a, $color)), ($maxWidth * $i / $steps)
        $pen.LineJoin = 'Round'; $path = & $pathFactory; $g.DrawPath($pen, $path); $pen.Dispose(); $path.Dispose()
    }
}

function Diamond($g, [double]$cx, [double]$cy, [double]$r, $color) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $p.AddPolygon(@([Drawing.PointF]::new($cx, $cy - $r), [Drawing.PointF]::new($cx + $r, $cy), [Drawing.PointF]::new($cx, $cy + $r), [Drawing.PointF]::new($cx - $r, $cy)))
    $b = New-Object Drawing.SolidBrush $color; $g.FillPath($b, $p); $b.Dispose(); $p.Dispose()
}

function Save($canvas, [string]$name, [int[]]$border) {
    $bmp, $g = $canvas
    $g.Dispose(); $bmp.Save((Join-Path $OutDir "$name.png"), [Drawing.Imaging.ImageFormat]::Png)
    $slices[$name] = [ordered]@{ w = $bmp.Width; h = $bmp.Height; left = $border[0]; bottom = $border[1]; right = $border[2]; top = $border[3] }
    $bmp.Dispose()
}

# ---- панели ----
function Panel([string]$name, [bool]$selected) {
    $cv = New-Canvas 192 192; $g = $cv[1]; $pad = 10; $s = 192 - 2 * $pad
    if ($selected) { Glow $g { Chamfer $pad $pad $s $s 26 } $Cyan 6 14 }
    $path = Chamfer $pad $pad $s $s 26
    Fill-Gradient $g $path $NavyTop $NavyBottom $pad $s
    Stroke $g $path ($(if ($selected) { $Cyan } else { $Outline })) 3
    $inner = Chamfer ($pad + 9) ($pad + 9) ($s - 18) ($s - 18) 20
    Stroke $g $inner $OutlineSoft 1.5
    Diamond $g 96 ($pad + 1) 6 ($(if ($selected) { $Cyan } else { $Outline }))
    Diamond $g 96 (192 - $pad - 1) 6 ($(if ($selected) { $Cyan } else { $Outline }))
    $path.Dispose(); $inner.Dispose()
    Save $cv $name @(48, 48, 48, 48)
}
Panel 'panel_navy' $false
Panel 'panel_navy_selected' $true

# ---- кремовая карточка (строки, подсказки) ----
function Card([string]$name, $edge, [double]$edgeWidth) {
    $cv = New-Canvas 128 128; $g = $cv[1]; $pad = 6; $s = 128 - 2 * $pad
    $path = Chamfer $pad $pad $s $s 12
    Fill-Gradient $g $path (C 'F6EEE4') $Cream $pad $s
    Stroke $g $path $edge $edgeWidth
    $path.Dispose()
    Save $cv $name @(24, 24, 24, 24)
}
Card 'card_cream' $CreamEdge 2
Card 'card_cream_selected' $Cyan 4

# ---- кнопки ----
function Button([string]$name, $top, $bottom, $edge, [bool]$outlined, [bool]$glow) {
    $cv = New-Canvas 256 96; $g = $cv[1]; $pad = 10
    if ($glow) { Glow $g { Pointed $pad $pad (256 - 2 * $pad) (96 - 2 * $pad) 22 } $Cyan 6 12 }
    $path = Pointed $pad $pad (256 - 2 * $pad) (96 - 2 * $pad) 22
    if ($outlined) { Fill-Gradient $g $path (C '0F3A68' 235) (C '0A2B52' 235) $pad (96 - 2 * $pad) }
    else { Fill-Gradient $g $path $top $bottom $pad (96 - 2 * $pad) }
    Stroke $g $path $edge 2.5
    Diamond $g ($pad + 14) 48 4.5 $edge
    Diamond $g (256 - $pad - 14) 48 4.5 $edge
    $path.Dispose()
    Save $cv $name @(44, 30, 44, 30)
}
Button 'button_primary'          $CoralTop $CoralBottom $CoralEdge $false $false
Button 'button_primary_hover'    (C 'E8737A') (C 'D05A62') (C 'FFD0CC') $false $false
Button 'button_primary_pressed'  (C 'B8444C') (C 'A83D45') $CoralEdge $false $false
Button 'button_secondary'        $null $null $OutlineSoft $true $false
Button 'button_secondary_hover'  $null $null $Cyan $true $true
Button 'button_disabled'         $Disabled (C '2E4257') (C '5C7289') $false $false

# ---- слоты предметов и способностей ----
function Slot([string]$name, $edge, [double]$edgeWidth, [bool]$glow) {
    $cv = New-Canvas 128 128; $g = $cv[1]; $pad = 10; $s = 128 - 2 * $pad
    if ($glow) { Glow $g { Chamfer $pad $pad $s $s 14 } $edge 6 12 }
    $path = Chamfer $pad $pad $s $s 14
    Fill-Gradient $g $path (C '12406F') $NavyDeep $pad $s
    Stroke $g $path $edge $edgeWidth
    $path.Dispose()
    Save $cv $name @(28, 28, 28, 28)
}

<#
    Ячейка редкости. Прежние рамки отличались только цветом тонкой обводки —
    владелец 16 сентября: «не очевидно где какая, надо прям усилить».
    Теперь ступень видна четырьмя признаками сразу: толщина канта, свечение,
    подкраска самой ячейки, угловые метки и ромб сверху у высоких редкостей.

    tier: 0 обычная, 1 редкая, 2 эпическая, 3 уникальная.
#>
function DiamondPath([double]$cx, [double]$cy, [double]$size) {
    $d = New-Object Drawing.Drawing2D.GraphicsPath
    $d.AddPolygon(@(
        (New-Object Drawing.PointF ([float]$cx), ([float]($cy - $size)))
        (New-Object Drawing.PointF ([float]($cx + $size)), ([float]$cy))
        (New-Object Drawing.PointF ([float]$cx), ([float]($cy + $size)))
        (New-Object Drawing.PointF ([float]($cx - $size)), ([float]$cy))))
    $d
}

function RaritySlot([string]$name, $edge, [int]$tier) {
    $cv = New-Canvas 128 128; $g = $cv[1]; $pad = 10; $s = 128 - 2 * $pad
    $width = @(2.5, 4.5, 6, 7)[$tier]
    if ($tier -ge 1) { Glow $g { Chamfer $pad $pad $s $s 14 } $edge (@(0, 5, 9, 13)[$tier]) (@(0, 10, 16, 22)[$tier]) }
    $path = Chamfer $pad $pad $s $s 14
    # Подкраска ячейки цветом редкости: даже без канта видно, что предмет не простой.
    $tint = @(0, 26, 46, 62)[$tier]
    $inner = [Drawing.Color]::FromArgb(255,
        [Math]::Min(255, 18 + [int]($edge.R * $tint / 255)),
        [Math]::Min(255, 64 + [int]($edge.G * $tint / 255)),
        [Math]::Min(255, 111 + [int]($edge.B * $tint / 255)))
    Fill-Gradient $g $path $inner $NavyDeep $pad $s
    Stroke $g $path $edge $width
    $path.Dispose()

    # Угловые метки: одна пара у редкой, две у эпической и уникальной.
    if ($tier -ge 1) {
        # Скобки обязательны: запятая в PowerShell связывает сильнее минуса,
        # и «128 - $pad, 128 - $pad» читается как «число минус массив».
        $far = 128 - $pad
        $corners = if ($tier -ge 2) { @(@($pad, $pad), @($far, $pad), @($pad, $far), @($far, $far)) }
                   else { @(@($pad, $pad), @($far, $far)) }
        $mark = @(0, 9, 12, 14)[$tier]
        # Метки кремовые: одного цвета с кантом они с ним сливались.
        $brush = New-Object Drawing.SolidBrush (C 'FFF3E2')
        foreach ($c in $corners) {
            $pts = @(
                (New-Object Drawing.PointF ([float]$c[0]), ([float]($c[1])))
                (New-Object Drawing.PointF ([float]($c[0] + $(if ($c[0] -lt 64) { $mark } else { -$mark }))), ([float]$c[1]))
                (New-Object Drawing.PointF ([float]$c[0]), ([float]($c[1] + $(if ($c[1] -lt 64) { $mark } else { -$mark })))))
            $g.FillPolygon($brush, $pts)
        }
        $brush.Dispose()
    }

    # Ромб сверху — знак высокой редкости; у уникальной он крупнее и со свечением.
    if ($tier -ge 2) {
        $size = @(0, 0, 11, 15)[$tier]
        $cx = 64; $cy = $pad
        $diamond = DiamondPath $cx $cy $size
        # Glow уничтожает переданный путь, поэтому ромб строится дважды: для свечения и для заливки.
        if ($tier -eq 3) { Glow $g { DiamondPath $cx $cy $size } $edge 6 14 }
        $b = New-Object Drawing.SolidBrush $edge; $g.FillPath($b, $diamond); $b.Dispose()
        Stroke $g $diamond (C 'FFF3E2') 2
        $diamond.Dispose()
    }
    Save $cv $name @(28, 28, 28, 28)
}

Slot 'slot'            (C '3E6F94') 2 $false
Slot 'slot_selected'   $Cyan 3.5 $true
# Редкости: обычная, редкая, эпическая, уникальная (имена спрайтов исторические).
RaritySlot 'slot_common' (C 'C9BFAC') 0
RaritySlot 'slot_magic'  (C '3FD2E0') 1
RaritySlot 'slot_rare'   (C 'A96BE8') 2
RaritySlot 'slot_unique' (C 'E2563F') 3

<#
    Контурные рамки ячейки: без заливки, поэтому не закрывают ни предмет, ни кант
    редкости. Прежняя отметка выбора (slot_selected) была сплошной и прятала и то, и другое.
#>
function OutlineFrame([string]$name, $edge, [double]$width, [int]$glowPasses, [int]$glowWidth) {
    $cv = New-Canvas 128 128; $g = $cv[1]; $pad = 6; $s = 128 - 2 * $pad
    if ($glowPasses -gt 0) { Glow $g { Chamfer $pad $pad $s $s 16 } $edge $glowPasses $glowWidth }
    $path = Chamfer $pad $pad $s $s 16
    Stroke $g $path $edge $width
    $path.Dispose()
    Save $cv $name @(28, 28, 28, 28)
}
OutlineFrame 'slot_hover' (C 'FFF3E2') 3 4 8
OutlineFrame 'slot_focus' $Cyan 5 7 14

<#
    Круглые слоты палатки. icon_ring залит синим диском — поверх значка он
    прятал надетую вещь (16 сентября). Эти кольца без заливки.
#>
function RingPath([double]$cx, [double]$cy, [double]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $p.AddEllipse([float]($cx - $r), [float]($cy - $r), [float](2 * $r), [float](2 * $r))
    $p
}
function OutlineRing([string]$name, $edge, [double]$width, [int]$glowPasses, [int]$glowWidth) {
    $cv = New-Canvas 128 128; $g = $cv[1]
    if ($glowPasses -gt 0) { Glow $g { RingPath 64 64 52 } $edge $glowPasses $glowWidth }
    $path = RingPath 64 64 52
    Stroke $g $path $edge $width
    $path.Dispose()
    Save $cv $name @(0, 0, 0, 0)
}
OutlineRing 'ring_hover' (C 'FFF3E2') 3 4 8
OutlineRing 'ring_focus' $Cyan 5 7 14

# Мягкое свечение за предметом; белое — игра красит его в цвет редкости.
$cv = New-Canvas 128 128; $g = $cv[1]
$glowPath = New-Object Drawing.Drawing2D.GraphicsPath
$glowPath.AddEllipse(4, 4, 120, 120)
$brush = New-Object Drawing.Drawing2D.PathGradientBrush $glowPath
$brush.CenterColor = [Drawing.Color]::FromArgb(210, 255, 255, 255)
$brush.SurroundColors = @([Drawing.Color]::FromArgb(0, 255, 255, 255))
$g.FillPath($brush, $glowPath)
$brush.Dispose(); $glowPath.Dispose()
Save $cv 'rarity_glow' @(0, 0, 0, 0)

# Камень редкости: белый ромб с кремовым кантом, красится в цвет редкости.
$cv = New-Canvas 32 32; $g = $cv[1]
$gem = DiamondPath 16 16 12
$b = New-Object Drawing.SolidBrush ([Drawing.Color]::White); $g.FillPath($b, $gem); $b.Dispose()
$shade = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(70, 0, 0, 0))
$g.FillPolygon($shade, @((New-Object Drawing.PointF 16, 16), (New-Object Drawing.PointF 28, 16), (New-Object Drawing.PointF 16, 28)))
$shade.Dispose()
Stroke $g $gem (C 'FFF3E2') 2
$gem.Dispose()
Save $cv 'rarity_gem' @(0, 0, 0, 0)

# ---- полосы (жизнь, лавидий, опыт) ----
$cv = New-Canvas 128 40; $g = $cv[1]
$path = Chamfer 4 4 120 32 8; $b = New-Object Drawing.SolidBrush $Track; $g.FillPath($b, $path); $b.Dispose()
Stroke $g $path (C '2E5E86') 2; $path.Dispose()
Save $cv 'bar_track' @(16, 16, 16, 16)
$cv = New-Canvas 128 40; $g = $cv[1]
$path = Chamfer 6 6 116 28 7; Fill-Gradient $g $path ([Drawing.Color]::White) (C 'D9D9D9') 6 28; $path.Dispose()
$hl = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(90, 255, 255, 255)); $g.FillRectangle($hl, 12, 9, 104, 6); $hl.Dispose()
Save $cv 'bar_fill_white' @(14, 14, 14, 14)

# ---- клавиша, плашка-пилюля ----
$cv = New-Canvas 64 64; $g = $cv[1]
$path = Chamfer 6 6 52 52 9; Fill-Gradient $g $path (C 'D4485E') (C 'B83A50') 6 52; Stroke $g $path (C 'F2A0A6') 2; $path.Dispose()
Save $cv 'keycap' @(20, 20, 20, 20)
$cv = New-Canvas 192 72; $g = $cv[1]
$path = Pointed 6 6 180 60 24; Fill-Gradient $g $path $NavyTop $NavyBottom 6 60; Stroke $g $path $Outline 2.5; $path.Dispose()
Save $cv 'pill_navy' @(40, 24, 40, 24)

# ---- переключатель и слайдер ----
$cv = New-Canvas 128 64; $g = $cv[1]
$p = New-Object Drawing.Drawing2D.GraphicsPath; $p.AddArc(6, 6, 52, 52, 90, 180); $p.AddArc(70, 6, 52, 52, 270, 180); $p.CloseFigure()
Fill-Gradient $g $p (C '0A8BDB') (C '0570BB') 6 52; Stroke $g $p $Cyan 2; $p.Dispose()
Save $cv 'toggle_on' @(32, 32, 32, 32)
$cv = New-Canvas 128 64; $g = $cv[1]
$p = New-Object Drawing.Drawing2D.GraphicsPath; $p.AddArc(6, 6, 52, 52, 90, 180); $p.AddArc(70, 6, 52, 52, 270, 180); $p.CloseFigure()
$b = New-Object Drawing.SolidBrush $Track; $g.FillPath($b, $p); $b.Dispose(); Stroke $g $p $Outline 2; $p.Dispose()
Save $cv 'toggle_off' @(32, 32, 32, 32)
$cv = New-Canvas 64 64; $g = $cv[1]
$b = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(70, 0, 20, 40)); $g.FillEllipse($b, 8, 10, 50, 50); $b.Dispose()
$p = New-Object Drawing.Drawing2D.GraphicsPath; $p.AddEllipse(7, 6, 50, 50)
Fill-Gradient $g $p ([Drawing.Color]::White) (C 'D8E6EF') 6 50; Stroke $g $p (C 'A7C6DA') 2; $p.Dispose()
Save $cv 'knob' @(0, 0, 0, 0)

# ---- орнамент и разделитель ----
$cv = New-Canvas 32 32; $g = $cv[1]; Diamond $g 16 16 11 $Cyan; Save $cv 'diamond' @(0, 0, 0, 0)
$cv = New-Canvas 256 16; $g = $cv[1]
$pen = New-Object Drawing.Pen $OutlineSoft, 2; $g.DrawLine($pen, 4, 8, 112, 8); $g.DrawLine($pen, 144, 8, 252, 8); $pen.Dispose()
Diamond $g 128 8 6 $Outline
Save $cv 'divider' @(60, 0, 60, 0)

# ---- боевой HUD по концепту v3-hud: толстая светлая фаска ----
$Bevel      = C '86B0D2'
$BevelLight = C 'D2E6F4'
$BevelDark  = C '2F5F8A'
$FrameGeom  = { Chamfer 10 10 108 108 12 }

# Плашка частей HUD: мелкая фаска, толстый светлый контур, тонкая тёмная линия внутри.
$cv = New-Canvas 128 128; $g = $cv[1]
$path = Chamfer 6 6 116 116 14
Fill-Gradient $g $path (C '12406F') $NavyBottom 6 116
Stroke $g $path $Bevel 5
$inner = Chamfer 12 12 104 104 10; Stroke $g $inner $BevelDark 1.5; $inner.Dispose()
$pen = New-Object Drawing.Pen $BevelLight, 2
$g.DrawLine($pen, 8, 19, 19, 8); $g.DrawLine($pen, 109, 8, 120, 19); $g.DrawLine($pen, 8, 109, 19, 120); $g.DrawLine($pen, 109, 120, 120, 109)
$pen.Dispose(); $path.Dispose()
Save $cv 'hud_panel' @(28, 28, 28, 28)

# Рамка плитки способности: только кант, середина прозрачна — под ней арт.
function HudFrame([string]$name, [bool]$active) {
    $cv = New-Canvas 128 128; $g = $cv[1]
    if ($active) { Glow $g $FrameGeom $Cyan 7 16 }
    $path = & $FrameGeom
    Stroke $g $path ($(if ($active) { C 'BFF4FF' } else { $Bevel })) 7
    $hi = Chamfer 8 8 112 112 13; Stroke $g $hi ($(if ($active) { [Drawing.Color]::White } else { $BevelLight })) 1.5; $hi.Dispose()
    $lo = Chamfer 14 14 100 100 9; Stroke $g $lo $NavyDeep 2; $lo.Dispose()
    $path.Dispose()
    Save $cv $name @(32, 32, 32, 32)
}
HudFrame 'hud_frame' $false
HudFrame 'hud_frame_active' $true

# Маска арта той же геометрии, что и рамка.
$cv = New-Canvas 128 128; $g = $cv[1]
$path = & $FrameGeom; $b = New-Object Drawing.SolidBrush ([Drawing.Color]::White); $g.FillPath($b, $path); $b.Dispose(); $path.Dispose()
Save $cv 'hud_mask' @(32, 32, 32, 32)

# Хвостик подсказки: кремовый треугольник, кант только по косым сторонам.
$cv = New-Canvas 64 40; $g = $cv[1]
$p = New-Object Drawing.Drawing2D.GraphicsPath
$p.AddPolygon(@([Drawing.PointF]::new(2, 0), [Drawing.PointF]::new(62, 0), [Drawing.PointF]::new(32, 34)))
$b = New-Object Drawing.SolidBrush $Cream; $g.FillPath($b, $p); $b.Dispose(); $p.Dispose()
$pen = New-Object Drawing.Pen $CreamEdge, 2.5; $g.DrawLine($pen, 2, 1, 32, 34); $g.DrawLine($pen, 62, 1, 32, 34); $pen.Dispose()
Save $cv 'tooltip_tail' @(0, 0, 0, 0)

# Круглая подложка иконки в заголовке подсказки.
$cv = New-Canvas 128 128; $g = $cv[1]
$p = New-Object Drawing.Drawing2D.GraphicsPath; $p.AddEllipse(8, 8, 112, 112)
Fill-Gradient $g $p (C '1C4B7A') $NavyBottom 8 112; Stroke $g $p $Bevel 6; $p.Dispose()
$p = New-Object Drawing.Drawing2D.GraphicsPath; $p.AddEllipse(17, 17, 94, 94); Stroke $g $p $BevelDark 2; $p.Dispose()
Save $cv 'icon_ring' @(0, 0, 0, 0)

# Метка героя на миникарте: коралловая стрелка с кремовым кантом, остриём вверх.
$cv = New-Canvas 96 96; $g = $cv[1]
$shadow = New-Object Drawing.Drawing2D.GraphicsPath
$shadow.AddPolygon(@([Drawing.PointF]::new(48, 12), [Drawing.PointF]::new(82, 88), [Drawing.PointF]::new(48, 70), [Drawing.PointF]::new(14, 88)))
$b = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(90, 8, 28, 50)); $g.FillPath($b, $shadow); $b.Dispose(); $shadow.Dispose()
$p = New-Object Drawing.Drawing2D.GraphicsPath
$p.AddPolygon(@([Drawing.PointF]::new(48, 6), [Drawing.PointF]::new(80, 82), [Drawing.PointF]::new(48, 64), [Drawing.PointF]::new(16, 82)))
Fill-Gradient $g $p (C 'EE7479') (C 'C4454E') 6 76
$pen = New-Object Drawing.Pen $Cream, 5; $pen.LineJoin = 'Round'; $g.DrawPath($pen, $p); $pen.Dispose(); $p.Dispose()
Save $cv 'map_player' @(0, 0, 0, 0)

# ---- плоские значки параметров для подсказок: одна заливка, читаются на 30 px ----
$GlyphInk = C '1C3A5E'
function Glyph([string]$name, [scriptblock]$draw) {
    $cv = New-Canvas 96 96; $g = $cv[1]
    & $draw $g
    Save $cv $name @(0, 0, 0, 0)
}
function Brush($color) { New-Object Drawing.SolidBrush $color }
function RoundPen($color, [double]$width) { $pen = New-Object Drawing.Pen $color, $width; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'; $pen }

Glyph 'stat_glyph_heart' { param($g)
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $p.AddBezier(48, 86, 10, 58, 6, 24, 29, 16); $p.AddBezier(29, 16, 41, 12, 48, 24, 48, 30)
    $p.AddBezier(48, 30, 48, 24, 55, 12, 67, 16); $p.AddBezier(67, 16, 90, 24, 86, 58, 48, 86)
    $b = Brush (C 'D9505A'); $g.FillPath($b, $p); $b.Dispose(); $p.Dispose() }

Glyph 'stat_glyph_lavidium' { param($g)
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $p.AddBezier(50, 6, 72, 28, 84, 48, 76, 68); $p.AddBezier(76, 68, 70, 84, 60, 90, 48, 90)
    $p.AddBezier(48, 90, 28, 90, 16, 76, 18, 58); $p.AddBezier(18, 58, 20, 44, 32, 38, 34, 26)
    $p.AddBezier(34, 26, 44, 36, 42, 46, 46, 48); $p.AddBezier(46, 48, 54, 36, 52, 20, 50, 6)
    $b = Brush (C 'E0843F'); $g.FillPath($b, $p); $b.Dispose(); $p.Dispose()
    $q = New-Object Drawing.Drawing2D.GraphicsPath
    $q.AddBezier(48, 50, 60, 60, 62, 74, 56, 80); $q.AddBezier(56, 80, 50, 86, 40, 84, 38, 76); $q.AddBezier(38, 76, 36, 66, 44, 60, 48, 50)
    $b = Brush (C 'F7C98A'); $g.FillPath($b, $q); $b.Dispose(); $q.Dispose() }

Glyph 'stat_glyph_cooldown' { param($g)
    $pen = RoundPen $GlyphInk 9; $g.DrawEllipse($pen, 13, 17, 70, 70)
    $g.DrawLine($pen, 48, 52, 48, 32); $g.DrawLine($pen, 48, 52, 62, 60); $pen.Dispose()
    $b = Brush $GlyphInk; $g.FillRectangle($b, 40, 4, 16, 9); $b.Dispose() }

Glyph 'stat_glyph_damage' { param($g)
    $b = Brush $GlyphInk
    $g.TranslateTransform(48, 48); $g.RotateTransform(45)
    $blade = New-Object Drawing.Drawing2D.GraphicsPath
    $blade.AddPolygon(@([Drawing.PointF]::new(-8, 14), [Drawing.PointF]::new(-8, -36), [Drawing.PointF]::new(0, -50), [Drawing.PointF]::new(8, -36), [Drawing.PointF]::new(8, 14)))
    $g.FillPath($b, $blade); $blade.Dispose()
    $g.FillRectangle($b, -24, 14, 48, 10); $g.FillRectangle($b, -5, 24, 10, 16); $g.FillEllipse($b, -9, 36, 18, 14)
    $g.ResetTransform(); $b.Dispose() }

Glyph 'stat_glyph_range' { param($g)
    $b = Brush $GlyphInk
    $g.FillRectangle($b, 10, 42, 56, 12)
    $g.FillPolygon($b, @([Drawing.PointF]::new(58, 24), [Drawing.PointF]::new(90, 48), [Drawing.PointF]::new(58, 72)))
    $g.FillRectangle($b, 6, 28, 9, 40); $b.Dispose() }

Glyph 'stat_glyph_radius' { param($g)
    $pen = RoundPen $GlyphInk 8; $g.DrawEllipse($pen, 10, 10, 76, 76); $g.DrawLine($pen, 48, 48, 80, 48); $pen.Dispose()
    $b = Brush $GlyphInk; $g.FillEllipse($b, 38, 38, 20, 20); $b.Dispose() }

Glyph 'stat_glyph_duration' { param($g)
    $b = Brush $GlyphInk
    $g.FillRectangle($b, 16, 6, 64, 10); $g.FillRectangle($b, 16, 80, 64, 10)
    $g.FillPolygon($b, @([Drawing.PointF]::new(24, 16), [Drawing.PointF]::new(72, 16), [Drawing.PointF]::new(52, 48), [Drawing.PointF]::new(72, 80), [Drawing.PointF]::new(24, 80), [Drawing.PointF]::new(44, 48)))
    $b.Dispose()
    $sand = Brush (C 'F0E7DC'); $g.FillPolygon($sand, @([Drawing.PointF]::new(36, 74), [Drawing.PointF]::new(60, 74), [Drawing.PointF]::new(48, 60))); $sand.Dispose() }

# Блик для коралловых кнопок и шапки: косая мягкая светлая полоса, края прозрачные.
$cv = New-Canvas 160 256; $g = $cv[1]
$g.TranslateTransform(80, 128); $g.RotateTransform(18)
$rect = [Drawing.RectangleF]::new(-46, -170, 92, 340)
$shine = New-Object Drawing.Drawing2D.LinearGradientBrush $rect, ([Drawing.Color]::FromArgb(0, 255, 255, 255)), ([Drawing.Color]::FromArgb(0, 255, 255, 255)), 0.0
$blend = New-Object Drawing.Drawing2D.ColorBlend 3
$blend.Colors = @([Drawing.Color]::FromArgb(0, 255, 255, 255), [Drawing.Color]::FromArgb(150, 255, 248, 235), [Drawing.Color]::FromArgb(0, 255, 255, 255))
$blend.Positions = @(0.0, 0.5, 1.0)
$shine.InterpolationColors = $blend
$g.FillRectangle($shine, $rect); $shine.Dispose(); $g.ResetTransform()
Save $cv 'pause_shine' @(0, 0, 0, 0)

($slices | ConvertTo-Json -Depth 4) | Set-Content -Path (Join-Path $OutDir 'slices.json') -Encoding UTF8
# Простая построчная копия для редакторного импортёра: «имя лево низ право верх».
$lines = foreach ($name in $slices.Keys) { $s = $slices[$name]; "$name $($s.left) $($s.bottom) $($s.right) $($s.top)" }
[IO.File]::WriteAllLines((Join-Path $OutDir 'slices.txt'), [string[]]$lines)
"chrome sprites: " + $slices.Count + " -> " + $OutDir
