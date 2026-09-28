# Generates src/WinNote/Assets/WinNote.ico (PNG-compressed entries, 16..256 px).
# Run with Windows PowerShell 5.1:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\WinNote\Assets\WinNote.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = [Math]::Max(1, [Math]::Round($s * 0.06))
    $bg = New-RoundedRect $m $m ($s - 2 * $m) ($s - 2 * $m) ([Math]::Max(2, $s * 0.22))
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s),
        [System.Drawing.Color]::FromArgb(255, 76, 194, 255), [System.Drawing.Color]::FromArgb(255, 0, 95, 184))
    $g.FillPath($brush, $bg)

    # Checkbox with tick on the first row, then two text lines.
    $white = [System.Drawing.Color]::White
    $pw = [Math]::Max(1.3, $s * 0.075)
    $pen = New-Object System.Drawing.Pen $white, $pw
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'

    $left = $s * 0.24; $right = $s * 0.76
    $box = $s * 0.24
    $y1 = $s * 0.24
    $boxPath = New-RoundedRect $left $y1 $box $box ([Math]::Max(1, $box * 0.22))
    $g.FillPath((New-Object System.Drawing.SolidBrush $white), $boxPath)
    $tick = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0, 95, 184)), ([Math]::Max(1.2, $s * 0.05))
    $tick.StartCap = 'Round'; $tick.EndCap = 'Round'; $tick.LineJoin = 'Round'
    $g.DrawLines($tick, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($left + $box * 0.22), ($y1 + $box * 0.52)),
        (New-Object System.Drawing.PointF ($left + $box * 0.45), ($y1 + $box * 0.75)),
        (New-Object System.Drawing.PointF ($left + $box * 0.82), ($y1 + $box * 0.28))))
    $g.DrawLine($pen, ($left + $box + $s * 0.10), ($y1 + $box / 2), $right, ($y1 + $box / 2))
    $g.DrawLine($pen, $left, ($s * 0.62), $right, ($s * 0.62))
    $g.DrawLine($pen, $left, ($s * 0.76), ($s * 0.60), ($s * 0.76))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$images = foreach ($s in $sizes) { , (New-IconPng $s) }

$fs = [System.IO.File]::Create((Resolve-Path (Split-Path $out)).Path + '\WinNote.ico')
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Host "Wrote $out"

# PNG logos referenced by Package.appxmanifest (only used if the app is packaged as MSIX).
$assetDir = (Resolve-Path (Split-Path $out)).Path
foreach ($logo in @(@('Square44x44Logo', 44), @('Square150x150Logo', 150), @('StoreLogo', 50))) {
    [System.IO.File]::WriteAllBytes("$assetDir\$($logo[0]).png", (New-IconPng $logo[1]))
    Write-Host "Wrote $($logo[0]).png"
}
