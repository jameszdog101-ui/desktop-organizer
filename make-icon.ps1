# 產生整理工具共用圖示 organizer.ico (藍色圓角底 + 白色 2x2 方格)
Add-Type -AssemblyName System.Drawing
$size = 64
$bmp = New-Object Drawing.Bitmap $size, $size
$g = [Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.Clear([Drawing.Color]::Transparent)
$path = New-Object Drawing.Drawing2D.GraphicsPath
$r = 14; $d = $r * 2
$path.AddArc(2, 2, $d, $d, 180, 90); $path.AddArc($size - 2 - $d, 2, $d, $d, 270, 90)
$path.AddArc($size - 2 - $d, $size - 2 - $d, $d, $d, 0, 90); $path.AddArc(2, $size - 2 - $d, $d, $d, 90, 90); $path.CloseFigure()
$g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 33, 111, 219))), $path)
$w = New-Object Drawing.SolidBrush ([Drawing.Color]::White)
foreach ($x in 14, 35) { foreach ($y in 14, 35) { $g.FillRectangle($w, $x, $y, 15, 15) } }
$icon = [Drawing.Icon]::FromHandle($bmp.GetHicon())
$fs = [IO.File]::Create("$PSScriptRoot\organizer.ico"); $icon.Save($fs); $fs.Close()
$g.Dispose(); $bmp.Dispose()
