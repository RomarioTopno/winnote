# WinNote icon design: renders the line-icon variants.
#   powershell -ExecutionPolicy Bypass -File tools\icon-design.ps1 -Preview <png>   -> comparison sheet
#   (make-icon.ps1 dot-sources this file to build the real .ico files)
param([string]$Preview)

Add-Type -AssemblyName System.Drawing

# ---- Glyph geometry, in a 24x24 grid (like Fluent System Icons) ----
# A note with a folded bottom-right corner; first row checked, then two text lines.

function New-PagePath([bool]$simple) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($simple) {
        # Small sizes: plain rounded page, no fold.
        $p.AddArc(5, 3, 4, 4, 180, 90); $p.AddArc(15, 3, 4, 4, 270, 90)
        $p.AddArc(15, 17, 4, 4, 0, 90);  $p.AddArc(5, 17, 4, 4, 90, 90)
        $p.CloseFigure()
        return $p
    }
    $p.AddArc(5, 3, 4, 4, 180, 90)                 # top-left corner
    $p.AddArc(15, 3, 4, 4, 270, 90)                # top-right corner
    $p.AddLine(19, 5, 19, 15.5)                    # right edge
    $p.AddLine(19, 15.5, 13.5, 21)                 # 45 degree cut (folded corner)
    $p.AddLine(13.5, 21, 7, 21)                    # bottom edge
    $p.AddArc(5, 17, 4, 4, 90, 90)                 # bottom-left corner
    $p.CloseFigure()
    return $p
}

function New-FoldPath {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddLine(13.5, 21, 13.5, 17.5)
    $p.AddArc(13.5, 15.5, 4, 4, 180, 90)           # rounded inner corner of the fold
    $p.AddLine(15.5, 15.5, 19, 15.5)
    return $p
}

function New-Pen($brushOrColor, [float]$width) {
    $pen = if ($brushOrColor -is [System.Drawing.Brush]) { New-Object System.Drawing.Pen $brushOrColor, $width } else { New-Object System.Drawing.Pen $brushOrColor, $width }
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    return $pen
}

# Draws the glyph mapped from the 24-grid into [x, y, size]. $line / $accent: Color or Brush.
function Draw-Glyph($g, [float]$x, [float]$y, [float]$size, $line, $accent, [float]$strokeGrid = 1.5) {
    # Small sizes: crop the grid tightly around the page (2.5..21.5) so the glyph fills the
    # space, and use the simplified shape; large sizes use the whole 24 grid.
    $simple = $size -lt 26
    $crop = if ($simple) { 2.5 } else { 0 }
    $span = 24 - 2 * $crop
    $scale = $size / $span
    $stroke = [Math]::Max(1.25, $strokeGrid * $scale) / $scale   # never thinner than 1.25 px
    $state = $g.Save()
    $g.TranslateTransform($x, $y); $g.ScaleTransform($scale, $scale); $g.TranslateTransform(-$crop, -$crop)
    if ($line -eq 'accent') { $line = Get-AccentBrush }
    if ($accent -eq 'accent') { $accent = Get-AccentBrush }

    $pen = New-Pen $line $stroke
    $accentPen = New-Pen $accent $stroke
    $g.DrawPath($pen, (New-PagePath $simple))
    if ($simple) {
        $g.DrawLines($accentPen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 8.5, 10), (New-Object System.Drawing.PointF 10.2, 11.7), (New-Object System.Drawing.PointF 13, 8.5)))
        $g.DrawLine($pen, 8.5, 16, 15.5, 16)
    } else {
        $g.DrawPath($pen, (New-FoldPath))
        $g.DrawLines($accentPen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 8, 8.8), (New-Object System.Drawing.PointF 9.6, 10.4), (New-Object System.Drawing.PointF 12.2, 7.4)))
        $g.DrawLine($pen, 14.5, 9, 16, 9)
        $g.DrawLine($pen, 8, 12.8, 16, 12.8)
        $g.DrawLine($pen, 8, 16.6, 10.8, 16.6)
    }
    $g.Restore($state)
}

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function C([string]$hex, [int]$a = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex); [System.Drawing.Color]::FromArgb($a, $c)
}

function New-Canvas([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

# Tile with gradient, soft top highlight and hairline inner border; glyph centred on it.
function Draw-Tile($g, [int]$s, [string]$from, [string]$to, $line, $accent) {
    $m = [Math]::Max(1, [Math]::Round($s * 0.04))
    $w = $s - 2 * $m
    $r = [Math]::Max(2, $w * 0.225)
    $tile = New-RoundRect $m $m $w $w $r
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF $m, $m), (New-Object System.Drawing.PointF ($m + $w), ($m + $w)), (C $from), (C $to))
    $g.FillPath($bg, $tile)
    if ($s -ge 32) {
        $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF 0, ($m - 1)), (New-Object System.Drawing.PointF 0, ($m + $w + 1)), (C '#FFFFFF' 0), (C '#FFFFFF' 0))
        $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
        $blend.Colors = @((C '#FFFFFF' 38), (C '#FFFFFF' 0), (C '#FFFFFF' 0))
        $blend.Positions = @(0.0, 0.55, 1.0)
        $hl.InterpolationColors = $blend
        $g.FillPath($hl, $tile)
        $g.DrawPath((New-Object System.Drawing.Pen (C '#FFFFFF' 40), ([Math]::Max(1, $s / 128))), $tile)
    }
    $gs = if ($s -lt 26) { $w * 0.78 } else { $w * 0.80 }
    Draw-Glyph $g ($m + ($w - $gs) / 2) ($m + ($w - $gs) / 2) $gs $line $accent 1.6
}

# Blue -> violet, in glyph grid units (the pen is drawn under the grid transform).
function Get-AccentBrush {
    New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF 5, 3), (New-Object System.Drawing.PointF 19, 21), (C '#38BDF8'), (C '#8B5CF6'))
}

# ---- Variants: each returns a Bitmap of the given size ----
function Render-Variant([string]$name, [int]$s) {
    $bmp, $g = New-Canvas $s
    switch ($name) {
        'indigo'   { Draw-Tile $g $s '#4C6EF5' '#7048E8' (C '#FFFFFF') (C '#FFFFFF') }
        'graphite' { Draw-Tile $g $s '#34343C' '#18181D' (C '#F4F4F6') 'accent' }
        'line'     {
            Draw-Glyph $g 0 0 $s 'accent' 'accent' 1.6
        }
    }
    $g.Dispose(); return $bmp
}

# Tray: single-colour glyph, white for a dark taskbar, near-black for a light one.
function Render-Tray([int]$s, [bool]$darkTaskbar) {
    $bmp, $g = New-Canvas $s
    $col = if ($darkTaskbar) { C '#FFFFFF' } else { C '#1B1B1F' }
    Draw-Glyph $g 0 0 $s $col $col 1.6
    $g.Dispose(); return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# ---- Preview sheet ----
if ($Preview) {
    $variants = @(@('indigo', '1  Indigo'), @('graphite', '2  Graphite'), @('line', '3  Line only'))
    $sizes = 48, 32, 24, 16
    $W = 1180; $H = 150 + 190 * $variants.Count + 170
    $sheet = New-Object System.Drawing.Bitmap $W, $H
    $sg = [System.Drawing.Graphics]::FromImage($sheet)
    $sg.SmoothingMode = 'AntiAlias'; $sg.TextRenderingHint = 'AntiAliasGridFit'; $sg.InterpolationMode = 'NearestNeighbor'
    $sg.Clear((C '#F3F3F3'))
    $sg.FillRectangle((New-Object System.Drawing.SolidBrush (C '#202020')), $W / 2, 0, $W / 2, $H)
    $font = New-Object System.Drawing.Font 'Segoe UI Variable Display', 15, ([System.Drawing.FontStyle]::Bold)
    $small = New-Object System.Drawing.Font 'Segoe UI', 10
    $sg.DrawString('Light background', $small, (New-Object System.Drawing.SolidBrush (C '#555555')), 24, 20)
    $sg.DrawString('Dark background', $small, (New-Object System.Drawing.SolidBrush (C '#AAAAAA')), $W / 2 + 24, 20)

    $y = 60
    foreach ($v in $variants) {
        foreach ($half in 0, 1) {
            $x0 = $half * $W / 2 + 24
            $txt = if ($half -eq 0) { C '#1B1B1F' } else { C '#FFFFFF' }
            $sg.DrawString($v[1], $font, (New-Object System.Drawing.SolidBrush $txt), $x0, $y)
            $big = Render-Variant $v[0] 128
            $sg.DrawImage($big, $x0, $y + 34, 128, 128)
            $x = $x0 + 150
            foreach ($s in $sizes) {
                $icon = Render-Variant $v[0] $s
                $sg.DrawImage($icon, $x, $y + 34 + (48 - $s), $s, $s)                    # actual size
                if ($s -le 32) { $sg.DrawImage($icon, $x, $y + 100, $s * 2, $s * 2) }
                $x += [Math]::Max($s * 2, 48) + 22
            }
        }
        $y += 190
    }

    # Tray row: on Windows 11 taskbar colours, actual size and 3x zoom.
    $sg.DrawString('Tray icon (single colour, follows taskbar theme)', $font, (New-Object System.Drawing.SolidBrush (C '#1B1B1F')), 24, $y)
    $sg.DrawString('Tray icon on dark taskbar', $font, (New-Object System.Drawing.SolidBrush (C '#FFFFFF')), $W / 2 + 24, $y)
    foreach ($half in 0, 1) {
        $dark = $half -eq 1
        $bar = if ($dark) { C '#1C1C1C' } else { C '#EEEEEE' }
        $x0 = $half * $W / 2 + 24
        $sg.FillRectangle((New-Object System.Drawing.SolidBrush $bar), $x0, $y + 40, $W / 2 - 48, 100)
        $x = $x0 + 20
        foreach ($s in 16, 20, 24, 32) {
            $t = Render-Tray $s $dark
            $sg.DrawImage($t, $x, $y + 50, $s, $s)
            $sg.DrawImage($t, $x, $y + 50 + 36, $s * 1.5, $s * 1.5)
            $x += 80
        }
    }
    $sheet.Save($Preview)
    Write-Host "Preview: $Preview"
}
