# Generates src/files/Assets/winlestia-files.ico (16..256, PNG-compressed) and optionally a 256 px PNG preview.
param([string]$Out = (Join-Path $PSScriptRoot '..\src\files\Assets\winlestia-files.ico'), [string]$Png = '')
Add-Type -AssemblyName System.Drawing
function Round([System.Drawing.RectangleF]$r, [float]$rad) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath; $d = $rad * 2
  $p.AddArc($r.X, $r.Y, $d, $d, 180, 90); $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
  $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90); $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90); $p.CloseFigure(); return $p
}
function Draw([int]$s) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
  $u = $s / 256.0
  # back panel + tab (deep tone), front panel (light gradient), thin highlight
  $backBr = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 196, 92, 96))
  $tab = Round ([System.Drawing.RectangleF]::new(24*$u, 44*$u, 100*$u, 56*$u)) (16*$u); $g.FillPath($backBr, $tab)
  $back = Round ([System.Drawing.RectangleF]::new(24*$u, 68*$u, 208*$u, 152*$u)) (24*$u); $g.FillPath($backBr, $back)
  $frontRect = [System.Drawing.RectangleF]::new(24*$u, 96*$u, 208*$u, 128*$u)
  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $frontRect, ([System.Drawing.Color]::FromArgb(255, 255, 158, 150)), ([System.Drawing.Color]::FromArgb(255, 226, 98, 108)), 90
  $front = Round $frontRect (24*$u); $g.FillPath($grad, $front)
  if ($s -ge 32) { $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 255, 255, 255)), ([Math]::Max(1, 3*$u)); $g.DrawArc($pen, 30*$u, 102*$u, 48*$u, 48*$u, 180, 90) }
  $g.Dispose(); return $bmp
}
$sizes = 16, 20, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) { $b = Draw $s; $m = New-Object System.IO.MemoryStream; $b.Save($m, [System.Drawing.Imaging.ImageFormat]::Png); if ($s -eq 256 -and $Png) { $b.Save($Png, [System.Drawing.Imaging.ImageFormat]::Png) }; $b.Dispose(); ,$m.ToArray() }
New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
$f = [System.IO.File]::Create($Out); $w = New-Object System.IO.BinaryWriter $f
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$off = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) { $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
  $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$off); $off += $pngs[$i].Length }
foreach ($p in $pngs) { $w.Write($p) }
$w.Close(); $f.Close(); "wrote $Out"
