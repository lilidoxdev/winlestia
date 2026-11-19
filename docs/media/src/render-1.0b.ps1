# Winlestia 1.0 - renders the 1.0b stills (same pipeline as render.ps1, per-scene size) -> docs/illustrations/*.webp
# Usage: powershell -ExecutionPolicy Bypass -File render-1.0b.ps1 [-Only winjets-1.0,dock-1.0]
param([string[]]$Only = @())
$ErrorActionPreference = 'Stop'
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
Add-Type -AssemblyName System.Drawing
$src = $PSScriptRoot
$out = [IO.Path]::GetFullPath((Join-Path $src '..\..\illustrations'))
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found.' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
$tmp = Join-Path ([IO.Path]::GetTempPath()) 'winlestia-render-1.0b'
New-Item -ItemType Directory -Force -Path $tmp, $out | Out-Null

# n = name = fragment ; w,h = stage size in CSS px ; amb = palette override ; wp = wallpaper
$scenes = @(
  @{ n='winjets-1.0';    w=1600; h=864;  amb='';                wp='wallpaper-aurora.svg'; title='Winjets' },
  @{ n='dock-1.0';       w=1760; h=560;  amb='tokens-teal.css'; wp='wallpaper-teal.svg';   title='Dock' },
  @{ n='lockscreen-1.0'; w=1280; h=800;  amb='';                wp='wallpaper-aurora.svg'; title='Ecran de verrouillage' },
  @{ n='overview-1.0';   w=1280; h=800;  amb='tokens-teal.css'; wp='wallpaper-teal.svg';   title='Apercu des fenetres' },
  @{ n='dictation-1.0';  w=1280; h=800;  amb='';                wp='wallpaper-aurora.svg'; title='Dictee vocale' },
  @{ n='fichiers-1.0';   w=1280; h=800;  amb='tokens-teal.css'; wp='wallpaper-teal.svg';   title='Fichiers' },
  @{ n='installer-1.0';  w=1280; h=800;  amb='tokens-warm.css'; wp='wallpaper-warm.svg';   title='Installateur' }
)
$template = [IO.File]::ReadAllText((Join-Path $src 'scene-1.0b.template.html'), $utf8)
$icons = [IO.File]::ReadAllText((Join-Path $src 'fragments\_icons.html'), $utf8)
foreach ($s in $scenes) {
  if ($Only.Count -and ($Only -notcontains $s.n)) { continue }
  $fp = Join-Path $src "fragments\$($s.n).html"
  if (-not (Test-Path $fp)) { continue }
  $body = [IO.File]::ReadAllText($fp, $utf8)
  $amb = if ($s.amb) { "<link rel=`"stylesheet`" href=`"$($s.amb)`">" } else { '' }
  $html = $template.Replace('{{TITLE}}', $s.title).Replace('{{W}}', "$($s.w)").Replace('{{H}}', "$($s.h)").Replace('{{AMBIANCE}}', $amb).Replace(
    '{{WALLPAPER}}', $s.wp).Replace('{{ICONS}}', $icons).Replace('{{BODY}}', $body)
  $page = "_$($s.n).html"
  [IO.File]::WriteAllText((Join-Path $src $page), $html, $utf8)
  $big = Join-Path $tmp "$($s.n)-big.png"
  if (Test-Path $big) { Remove-Item $big }
  $k = if ($s.w -gt 1600) { 2 } else { 3 }
  $edgeArgs = @('--headless=new','--disable-gpu','--hide-scrollbars','--log-level=3','--allow-file-access-from-files',
    "--force-device-scale-factor=$k","--window-size=$($s.w),$($s.h)",'--virtual-time-budget=12000','--run-all-compositor-stages-before-draw',
    "--user-data-dir=$tmp\profile","--screenshot=$big",([Uri](Join-Path $src $page)).AbsoluteUri)
  Start-Process -FilePath $edge -ArgumentList $edgeArgs -Wait -NoNewWindow
  if (-not (Test-Path $big)) { throw "No screenshot for $($s.n)" }
  $ow = 2000; $oh = [int][Math]::Round(2000 * $s.h / $s.w)
  $img = [Drawing.Image]::FromFile($big)
  $dst = New-Object Drawing.Bitmap $ow, $oh
  $g = [Drawing.Graphics]::FromImage($dst)
  $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'
  $g.DrawImage($img, 0, 0, $ow, $oh)
  $g.Dispose(); $img.Dispose()
  $mid = Join-Path $tmp "$($s.n)-2k.png"
  $dst.Save($mid, [Drawing.Imaging.ImageFormat]::Png); $dst.Dispose()
  & ffmpeg -v error -y -i $mid -c:v libwebp -quality 90 -compression_level 6 (Join-Path $out "$($s.n).webp")
  if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed for $($s.n)" }
  Write-Host "ok $($s.n) ${ow}x${oh}"
}
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
