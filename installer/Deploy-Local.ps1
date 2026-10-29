# Winlestia - deploiement local du build vers une copie installee (sans release, sans commit).
# Sauvegarde, ferme l'app, publie le backend, copie backend + frontend, relance.
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Winlestia'),
    [string]$BackupTag = 'visual-unify',
    [switch]$NoStart
)
$ErrorActionPreference = 'Stop'
$root  = Split-Path $PSScriptRoot -Parent
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $root "build\backups\$BackupTag-$stamp"
$stage  = Join-Path $root 'build\deploy-staging\Glass-v5'
$glass  = Join-Path $InstallDir 'Glass-v5'
$front  = Join-Path $InstallDir 'Caelestia-Windows'

Write-Host '-> Sauvegarde' -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $backup | Out-Null
robocopy $glass (Join-Path $backup 'Glass-v5') /E /NFL /NDL /NJH /NJS /NP | Out-Null
robocopy $front (Join-Path $backup 'Caelestia-Windows') /E /NFL /NDL /NJH /NJS /NP /XD '.venv' | Out-Null
$manifest = Join-Path $env:LOCALAPPDATA 'Winlestia\Explorer\manifest.json'
if (Test-Path $manifest) { Copy-Item $manifest (Join-Path $backup 'manifest.json') -Force }
Write-Host "   $backup"

Write-Host '-> Fermeture' -ForegroundColor Cyan
& (Join-Path $InstallDir 'Close-Winlestia.ps1') -AppRoot $InstallDir

Write-Host '-> Publish backend' -ForegroundColor Cyan
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'src\backend\Panneaux.Glass.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false `
    -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Compilation du backend echouee.' }
Write-Host '-> Publish Winlestia Fichiers' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\files\Winlestia.Files.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false `
    -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Compilation de Winlestia Fichiers echouee.' }
Copy-Item (Join-Path $root 'src\backend\Inventory.ps1') $stage -Force
Copy-Item (Join-Path $root 'src\backend\native\*.dll') $stage -Force
Copy-Item (Join-Path $root 'src\backend\native\*.exe') $stage -Force

Write-Host '-> Copie backend' -ForegroundColor Cyan
robocopy $stage $glass /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy backend: $LASTEXITCODE" }

Write-Host '-> Copie frontend' -ForegroundColor Cyan
robocopy (Join-Path $root 'src\frontend') $front /E /NFL /NDL /NJH /NJS /NP `
    /XD '.venv' '__pycache__' 'tests' 'validation-*' 'checkpoint-*' /XF '*.zip' '*.pyc' | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy frontend: $LASTEXITCODE" }
$global:LASTEXITCODE = 0

if (-not $NoStart) {
    Write-Host '-> Demarrage' -ForegroundColor Cyan
    Start-Process (Join-Path $glass 'Panneaux.Glass.exe') -WorkingDirectory $glass
}
Write-Host "Termine. Sauvegarde: $backup"
