# Winlestia - renders every scene to docs/screenshots/*.png (2560x1600).
# Edge headless at 4x device scale (5120x3200), then bicubic downscale with System.Drawing.
# Usage: powershell -ExecutionPolicy Bypass -File render.ps1 [-Only 02-media,03-performance]
param([string[]]$Only = @())
$ErrorActionPreference = 'Stop'
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
Add-Type -AssemblyName System.Drawing
$src   = $PSScriptRoot
$out   = [IO.Path]::GetFullPath((Join-Path $src '..\..\screenshots'))
$edge  = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found.' }
$utf8  = New-Object System.Text.UTF8Encoding($false)
$tmp   = Join-Path ([IO.Path]::GetTempPath()) 'winlestia-render'
New-Item -ItemType Directory -Force -Path $tmp, $out | Out-Null

# name, fragment (null = hand-written page), palette override, wallpaper, window title
$scenes = @(
  @{ n='01-dashboard';           page='dashboard.html' },
  @{ n='02-media';               frag='media';        amb='';                  wp='wallpaper-aurora.svg'; title='Media' },
  @{ n='03-performance';         frag='performance';  amb='';                  wp='wallpaper-aurora.svg'; title='Performance' },
  @{ n='04-espaces';             frag='espaces';      amb='tokens-warm.css';   wp='wallpaper-warm.svg';   title='Espaces' },
  @{ n='05-dock-commande-rapide';frag='dock';         amb='tokens-warm.css';   wp='wallpaper-warm.svg';   title='Dock et commande rapide' },
  @{ n='06-boite-a-outils';      frag='outils';       amb='tokens-teal.css';   wp='wallpaper-teal.svg';   title='Atelier' },
  @{ n='07-reglages';            frag='reglages';     amb='tokens-teal.css';   wp='wallpaper-teal.svg';   title='Reglages' },
  @{ n='08-terminal';            frag='terminal';     amb='tokens-teal.css';   wp='wallpaper-teal.svg';   title='Terminal' }
)
$template = [IO.File]::ReadAllText((Join-Path $src 'scene.template.html'), $utf8)
$icons    = [IO.File]::ReadAllText((Join-Path $src 'fragments\_icons.html'), $utf8)

foreach ($s in $scenes) {
  if ($Only.Count -and ($Only -notcontains $s.n)) { continue }
  if ($s.frag) {   # compose page = template + fragment
    $body = [IO.File]::ReadAllText((Join-Path $src "fragments\$($s.frag).html"), $utf8)
    $amb  = if ($s.amb) { "<link rel=`"stylesheet`" href=`"$($s.amb)`">" } else { '' }
    $html = $template.Replace('{{TITLE}}', $s.title).Replace('{{AMBIANCE}}', $amb).Replace('{{WALLPAPER}}', $s.wp).Replace('{{ICONS}}', $icons).Replace('{{BODY}}', $body)
    $page = "$($s.n).html"
    [IO.File]::WriteAllText((Join-Path $src $page), $html, $utf8)
  } else { $page = $s.page }
  $url = ([Uri](Join-Path $src $page)).AbsoluteUri
  $big = Join-Path $tmp "$($s.n)-4x.png"
  if (Test-Path $big) { Remove-Item $big }
  $edgeArgs = @('--headless=new','--disable-gpu','--hide-scrollbars','--log-level=3','--allow-file-access-from-files',
    '--force-device-scale-factor=4','--window-size=1280,800','--virtual-time-budget=5000',
    "--user-data-dir=$tmp\profile","--screenshot=$big",$url)
  Start-Process -FilePath $edge -ArgumentList $edgeArgs -Wait -NoNewWindow
  if (-not (Test-Path $big)) { throw "No screenshot for $($s.n)" }
  $img = [Drawing.Image]::FromFile($big)
  $dst = New-Object Drawing.Bitmap 2560, 1600
  $g = [Drawing.Graphics]::FromImage($dst)
  $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
  $g.DrawImage($img, 0, 0, 2560, 1600)
  $g.Dispose(); $img.Dispose()
  $dst.Save((Join-Path $out "$($s.n).png"), [Drawing.Imaging.ImageFormat]::Png); $dst.Dispose()
  Write-Host "ok $($s.n)"
}
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
