# Generates WinNote's icons from the line-icon design in icon-design.ps1:
#   src/WinNote/Assets/WinNote.ico    app icon (Graphite tile), 16..256 px
#   src/WinNote/Assets/TrayDark.ico   tray icon for a dark taskbar (white line glyph)
#   src/WinNote/Assets/TrayLight.ico  tray icon for a light taskbar (dark line glyph)
#   src/WinNote/Assets/*Logo.png      MSIX logos
# Run with Windows PowerShell 5.1:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
. (Join-Path $PSScriptRoot 'icon-design.ps1')

$assets = (Resolve-Path (Join-Path $PSScriptRoot '..\src\WinNote\Assets')).Path

# Multi-size .ico with PNG-compressed entries (supported since Windows Vista).
function Write-Ico([string]$path, [scriptblock]$render, [int[]]$sizes) {
    $images = foreach ($s in $sizes) { , (Get-PngBytes (& $render $s)) }
    $fs = [System.IO.File]::Create($path)
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
    Write-Host "Wrote $path"
}

Write-Ico "$assets\WinNote.ico" { param($s) Render-Variant 'graphite' $s } @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
# Tray icons: SM_CXSMICON is 16/20/24/32/40 px at 100/125/150/200/250 % scaling.
Write-Ico "$assets\TrayDark.ico"  { param($s) Render-Tray $s $true }  @(16, 20, 24, 32, 40, 48)
Write-Ico "$assets\TrayLight.ico" { param($s) Render-Tray $s $false } @(16, 20, 24, 32, 40, 48)

foreach ($logo in @(@('Square44x44Logo', 44), @('Square150x150Logo', 150), @('StoreLogo', 50))) {
    [System.IO.File]::WriteAllBytes("$assets\$($logo[0]).png", (Get-PngBytes (Render-Variant 'graphite' $logo[1])))
    Write-Host "Wrote $($logo[0]).png"
}
