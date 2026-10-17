# Builds Timetable.exe with the C# compiler that ships with Windows. Nothing to install.
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw   = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$out  = Join-Path (Split-Path -Parent $here) "Timetable.exe"
$ico  = Join-Path $here "icon.ico"

# Draw the app icon (a small calendar page) and save it as a PNG-based .ico
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = "AntiAlias"; $g.TextRenderingHint = "AntiAliasGridFit"
function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $r, $r, 180, 90); $p.AddArc($x + $w - $r, $y, $r, $r, 270, 90)
    $p.AddArc($x + $w - $r, $y + $h - $r, $r, $r, 0, 90); $p.AddArc($x, $y + $h - $r, $r, $r, 90, 90)
    $p.CloseFigure(); $p
}
$page = RoundRect 16 16 224 224 96
$g.FillPath([System.Drawing.Brushes]::White, $page)
$g.SetClip($page)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 59, 48))), 16, 16, 224, 70)
$g.ResetClip()
$colors = @(@(255,94,87), @(63,169,245), @(46,204,113), @(165,94,234), @(245,179,1), @(243,104,168))
$i = 0
foreach ($row in 0..1) { foreach ($col in 0..2) {
    $c = $colors[$i]; $i++
    $b = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($c[0], $c[1], $c[2]))
    $g.FillPath($b, (RoundRect (44 + $col * 58) (108 + $row * 58) 46 46 20))
} }
$g.Dispose()
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()
$fs = [System.IO.File]::Create($ico)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)
$bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0)
$bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$png.Length); $bw.Write([UInt32]22)
$bw.Write($png); $bw.Close()

& (Join-Path $fw "csc.exe") /nologo /target:winexe /optimize+ /platform:anycpu "/out:$out" "/win32icon:$ico" `
    "/r:$fw\WPF\PresentationFramework.dll" "/r:$fw\WPF\PresentationCore.dll" "/r:$fw\WPF\WindowsBase.dll" "/r:$fw\System.Xaml.dll" `
    (Join-Path $here "Timetable.cs")
if ($LASTEXITCODE -eq 0) { "Built $out" }
