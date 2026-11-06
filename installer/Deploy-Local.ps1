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
$filesStage = Join-Path $root 'build\deploy-staging\Files'
$glass  = Join-Path $InstallDir 'Glass-v5'
$front  = Join-Path $InstallDir 'Caelestia-Windows'

# Fusionne un publish secondaire dans le dossier principal : ajoute les fichiers absents et les assemblies
# gérées dont l'AssemblyVersion est strictement supérieure ; Winlestia.Files.* est toujours copié.
function Merge-Publish([string]$Source, [string]$Dest) {
    $srcRoot = (Resolve-Path $Source).Path.TrimEnd('\')
    foreach ($f in Get-ChildItem $srcRoot -Recurse -File) {
        $rel = $f.FullName.Substring($srcRoot.Length).TrimStart('\')
        $target = Join-Path $Dest $rel
        $copy = $false
        if ($f.Name -like 'Winlestia.Files.*' -or -not (Test-Path $target)) { $copy = $true }
        else {
            try {
                $new = [Reflection.AssemblyName]::GetAssemblyName($f.FullName).Version
                $old = [Reflection.AssemblyName]::GetAssemblyName($target).Version
                if ($new -gt $old) { $copy = $true }
            } catch { }  # natif ou illisible : on garde la copie du backend
        }
        if ($copy) {
            New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
            Copy-Item $f.FullName $target -Force
        }
    }
}

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
    -o $filesStage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Compilation de Winlestia Fichiers echouee.' }
Merge-Publish $filesStage $stage
Remove-Item -Recurse -Force $filesStage -ErrorAction SilentlyContinue
Write-Host '-> Publish winlestia.exe' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\cli\winlestia.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Compilation de winlestia.exe echouee.' }
Copy-Item (Join-Path $root 'src\backend\Inventory.ps1') $stage -Force
Copy-Item (Join-Path $root 'src\backend\native\*.dll') $stage -Force
Copy-Item (Join-Path $root 'src\backend\native\*.exe') $stage -Force

Write-Host '-> Copie backend' -ForegroundColor Cyan
robocopy $stage $glass /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy backend: $LASTEXITCODE" }
# robocopy saute un fichier de même taille et même date même si son contenu diffère (Whisper.net net8 vs net10 : la DLL net10 restait
# en place et cassait la dictée). Passe de rattrapage par empreinte.
Get-ChildItem $stage -File -Recurse | ForEach-Object {
    $target = Join-Path $glass $_.FullName.Substring($stage.Length).TrimStart('\')
    if (-not (Test-Path $target) -or (Get-FileHash $_.FullName).Hash -ne (Get-FileHash $target).Hash) {
        Copy-Item $_.FullName $target -Force
        Write-Host "   recopie (contenu différent) : $($_.Name)"
    }
}

Write-Host '-> Verre natif Qt' -ForegroundColor Cyan
$qtGlass = Join-Path $root 'src\frontend\QtGlassBackdrop.dll'
if (-not (Test-Path $qtGlass)) {
    cmd /c (Join-Path $root 'src\frontend\build-qt-glass.cmd')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $qtGlass)) { throw 'QtGlassBackdrop.dll manquant : compilation (src\frontend\build-qt-glass.cmd) echouee.' }
}

Write-Host '-> Copie frontend' -ForegroundColor Cyan
robocopy (Join-Path $root 'src\frontend') $front /E /NFL /NDL /NJH /NJS /NP `
    /XD '.venv' '__pycache__' 'tests' 'validation-*' 'checkpoint-*' /XF '*.zip' '*.pyc' | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy frontend: $LASTEXITCODE" }
$global:LASTEXITCODE = 0

if (-not $NoStart) {
    Write-Host '-> Demarrage' -ForegroundColor Cyan
    schtasks /run /tn WinlestiaShell | Out-Null  # same entry point as login autostart (--qt-shell)
}
Write-Host "Termine. Sauvegarde: $backup"
