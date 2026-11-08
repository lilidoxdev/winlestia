# Winlestia - renders the HTML/CSS illustrations (motion-design stills, not screen captures).
#   stills  -> docs/illustrations/*.webp (2000 px wide, quality 90)
#   banner  -> docs/media/banner.webp   (2000 px wide)
# Edge headless at 4x device scale (5120x3200), then bicubic downscale with System.Drawing.
# Usage: powershell -ExecutionPolicy Bypass -File render.ps1 [-Only dashboard,media,banner]
#   -Shots : renders only the aurora-palette variants used by the videos into video/shots/ (generated, not committed)
param([string[]]$Only = @(), [switch]$Shots)
$ErrorActionPreference = 'Stop'
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
Add-Type -AssemblyName System.Drawing
try { (Get-Process -Id $PID).PriorityClass = 'BelowNormal' } catch {}
$src   = $PSScriptRoot
$out   = [IO.Path]::GetFullPath((Join-Path $src '..\..\illustrations'))
$media = [IO.Path]::GetFullPath((Join-Path $src '..'))
$edge  = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found.' }
$utf8  = New-Object System.Text.UTF8Encoding($false)
$tmp   = Join-Path ([IO.Path]::GetTempPath()) 'winlestia-render'
New-Item -ItemType Directory -Force -Path $tmp, $out | Out-Null

# name, fragments (joined), palette override, wallpaper, window title
$scenes = @(
  @{ n='desktop';      frag=@('desktop');     amb='';                wp='wallpaper-aurora.svg'; title='Desktop' },
  @{ n='dashboard';    page='dashboard.html' },
  @{ n='media';        frag=@('media');       amb='';                wp='wallpaper-aurora.svg'; title='Media' },
  @{ n='performance';  frag=@('performance'); amb='';                wp='wallpaper-aurora.svg'; title='Performance' },
  @{ n='workspaces';   frag=@('workspaces');  amb='tokens-warm.css'; wp='wallpaper-warm.svg';   title='Workspaces' },
  @{ n='dock-palette'; frag=@('dock');        amb='tokens-warm.css'; wp='wallpaper-warm.svg';   title='Dock and quick command' },
  @{ n='tools';        frag=@('outils');      amb='tokens-teal.css'; wp='wallpaper-teal.svg';   title='Tools' },
  @{ n='settings';     frag=@('reglages');    amb='tokens-teal.css'; wp='wallpaper-teal.svg';   title='Settings' },
  @{ n='terminal';     frag=@('terminal');    amb='tokens-teal.css'; wp='wallpaper-teal.svg';   title='Terminal' },
  @{ n='wallpaper';    frag=@('wallpaper');   amb='';                wp='wallpaper-aurora.svg'; title='Wallpaper switcher' },
  @{ n='panels';       frag=@('panels');      amb='';                wp='wallpaper-aurora.svg'; title='Quick controls' },
  @{ n='v-workspaces'; frag=@('workspaces');  amb='';                wp='wallpaper-aurora.svg'; title='Workspaces'; shot=$true },
  @{ n='v-workspaces-warm'; frag=@('workspaces'); amb='tokens-warm.css'; wp='wallpaper-warm.svg'; title='Workspaces'; shot=$true },
  @{ n='v-workspaces-teal'; frag=@('workspaces'); amb='tokens-teal.css'; wp='wallpaper-teal.svg'; title='Workspaces'; shot=$true },
  @{ n='v-dock-palette'; frag=@('dock');      amb='';                wp='wallpaper-aurora.svg'; title='Dock and quick command';
  shot=$true },
  @{ n='v-tools';      frag=@('outils');      amb='';                wp='wallpaper-aurora.svg'; title='Tools'; shot=$true },
  @{ n='v-settings';   frag=@('reglages');    amb='';                wp='wallpaper-aurora.svg'; title='Settings'; shot=$true },
  @{ n='banner';       page='banner.html';    w=1280; h=640; scale=2; dest=$media }
)
$template = [IO.File]::ReadAllText((Join-Path $src 'scene.template.html'), $utf8)
$icons    = [IO.File]::ReadAllText((Join-Path $src 'fragments\_icons.html'), $utf8)

foreach ($s in $scenes) {
  if ($Only.Count -and ($Only -notcontains $s.n)) { continue }
  if ($Shots -and -not $s.shot -and $s.n -notin @('dashboard','performance','wallpaper')) { continue }
  if (-not $Shots -and $s.shot) { continue }
  $w = if ($s.w) { $s.w } else { 1280 }; $h = if ($s.h) { $s.h } else { 800 }
  $k = if ($s.scale) { $s.scale } else { 4 }
  $dest = if ($s.dest) { $s.dest } else { $out }
  if ($Shots) { $dest = Join-Path $src 'video\shots'; New-Item -ItemType Directory -Force -Path $dest | Out-Null }
  if ($s.frag) {   # compose page = template + fragments
    $body = ($s.frag | ForEach-Object { [IO.File]::ReadAllText((Join-Path $src "fragments\$_.html"), $utf8) }) -join "`n"
    $amb  = if ($s.amb) { "<link rel=`"stylesheet`" href=`"$($s.amb)`">" } else { '' }
    $html = $template.Replace('{{TITLE}}', $s.title).Replace('{{AMBIANCE}}', $amb).Replace('{{WALLPAPER}}', $s.wp).Replace('{{ICONS}}',
        $icons).Replace('{{BODY}}', $body)
    $page = "_$($s.n).html"
    [IO.File]::WriteAllText((Join-Path $src $page), $html, $utf8)
  } else { $page = $s.page }
  $url = ([Uri](Join-Path $src $page)).AbsoluteUri
  $big = Join-Path $tmp "$($s.n)-big.png"
  if (Test-Path $big) { Remove-Item $big }
  $edgeArgs = @('--headless=new','--disable-gpu','--hide-scrollbars','--log-level=3','--allow-file-access-from-files',
    "--force-device-scale-factor=$k","--window-size=$w,$h",'--virtual-time-budget=5000',
    "--user-data-dir=$tmp\profile","--screenshot=$big",$url)
  Start-Process -FilePath $edge -ArgumentList $edgeArgs -Wait -NoNewWindow
  if (-not (Test-Path $big)) { throw "No screenshot for $($s.n)" }
  $ow = $w * 2; $oh = $h * 2
  $img = [Drawing.Image]::FromFile($big)
  $dst = New-Object Drawing.Bitmap $ow, $oh
  $g = [Drawing.Graphics]::FromImage($dst)
  $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality';
  $g.CompositingQuality = 'HighQuality'
  $g.DrawImage($img, 0, 0, $ow, $oh)
  $g.Dispose(); $img.Dispose()
  if ($Shots) { $dst.Save((Join-Path $dest "$($s.n).png"), [Drawing.Imaging.ImageFormat]::Png); $dst.Dispose() }
  else {
    $mid = Join-Path $tmp "$($s.n)-2x.png"
    $dst.Save($mid, [Drawing.Imaging.ImageFormat]::Png); $dst.Dispose()
    $webp = Join-Path $dest "$($s.n).webp"
    & ffmpeg -v error -y -i $mid -vf scale=2000:-1:flags=lanczos -c:v libwebp -quality 90 -compression_level 6 $webp
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed for $($s.n)" }
  }
  Write-Host "ok $($s.n)"
}
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
