# Construit WinlestiaSetup.exe (autonome, fichier unique, win-x64).
# Reutilise installer\build.ps1 pour build\payload, puis le zippe avec les fichiers supplementaires que Winlestia.iss copie.
#   .\build-setup.ps1            payload.zip est place a cote de l'exe
#   .\build-setup.ps1 -Embed     payload.zip est integre a l'exe (fichier unique plus gros)
#   .\build-setup.ps1 -SkipPayloadBuild   reutilise un build\payload existant
param([switch]$Embed, [switch]$SkipPayloadBuild, [string]$Out = "")

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here -Parent
$proj = Join-Path $here 'WinlestiaSetup\WinlestiaSetup.csproj'
if (-not $Out) { $Out = Join-Path $root 'build\setup' }

if (-not $SkipPayloadBuild) { & (Join-Path $here 'build.ps1'); if ($LASTEXITCODE) { throw "build.ps1 failed ($LASTEXITCODE)" } }
$payload = Join-Path $root 'build\payload'
if (-not (Test-Path $payload)) { throw "Missing $payload. Run without -SkipPayloadBuild." }

# Dossier de staging avec la disposition que l'installateur extrait dans le dossier d'installation.
$stage = Join-Path $root 'build\payload-stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item $stage -ItemType Directory | Out-Null
foreach ($d in 'Glass-v5', 'Caelestia-Windows', 'Winlestia-Terminal') { Copy-Item (Join-Path $payload $d) (Join-Path $stage $d) -Recurse }
New-Item (Join-Path $stage 'Winjets') -ItemType Directory | Out-Null
Copy-Item (Join-Path $payload 'Winjets\builtin') (Join-Path $stage 'Winjets\builtin') -Recurse
Copy-Item (Join-Path $root 'assets\wallpapers') (Join-Path $stage 'wallpapers') -Recurse
foreach ($f in 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'src\backend\LICENSE-CAELESTIA.txt', 'src\backend\CAELESTIA-NOTICES.md') {
    Copy-Item (Join-Path $root $f) $stage
}
Copy-Item (Join-Path $here 'Close-Winlestia.ps1') $stage
Get-ChildItem $stage -Recurse -Include '__pycache__' -Directory | Remove-Item -Recurse -Force
Get-ChildItem $stage -Recurse -Include '*.pyc' -File | Remove-Item -Force

$zip = Join-Path $here 'WinlestiaSetup\payload.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("payload.zip: {0:N1} MB" -f ((Get-Item $zip).Length / 1MB))

# Le csproj integre payload.zip quand il existe ; en disposition cote a cote, il sort du dossier du projet apres publish.
if (-not $Embed) { Move-Item $zip (Join-Path $here 'WinlestiaSetup\payload.zip.tmp') -Force }
try {
    dotnet publish $proj -c Release -r win-x64 --self-contained -o $Out -nologo
    if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)" }
} finally {
    $tmp = Join-Path $here 'WinlestiaSetup\payload.zip.tmp'
    if (Test-Path $tmp) {
        if ($Embed) { Remove-Item $tmp -Force }
        else { Copy-Item $tmp (Join-Path $Out 'payload.zip') -Force; Remove-Item $tmp -Force }
    }
    if ($Embed -and (Test-Path $zip)) { Remove-Item $zip -Force }
}
Write-Host "Output: $Out"
