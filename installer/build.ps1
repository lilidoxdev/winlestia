# Winlestia — fabrique du setup
# PowerShell 7 / Windows 11. Reproduit exactement l'archive de release :
#   backend .NET auto-suffisant  +  frontend QML  +  Python 3.12 embarqué  +  Qt 6.10
# Sortie : build\out\Winlestia-Setup-<version>.exe

$ErrorActionPreference = 'Stop'
$root    = Split-Path $PSScriptRoot -Parent
$payload = Join-Path $root 'build\payload'
$pythonVersion = '3.12.10'
$pythonUrl = "https://www.python.org/ftp/python/$pythonVersion/python-$pythonVersion-embed-amd64.zip"

Write-Host '→ Nettoyage' -ForegroundColor Cyan
Remove-Item -Recurse -Force $payload -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $payload | Out-Null

Write-Host '→ Backend .NET 8 (auto-suffisant)' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\backend\Panneaux.Glass.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false `
    -o (Join-Path $payload 'Glass-v5') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Compilation du backend échouée.' }

Write-Host '→ Winlestia Fichiers (.NET 8, auto-suffisant)' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\files\Winlestia.Files.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false `
    -o (Join-Path $payload 'Glass-v5') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Compilation de Winlestia Fichiers échouée.' }

# Fichiers attendus à côté de l’exécutable mais hors du publish
Copy-Item (Join-Path $root 'src\backend\Inventory.ps1')          (Join-Path $payload 'Glass-v5') -Force
Copy-Item (Join-Path $root 'src\backend\native\*.dll')          (Join-Path $payload 'Glass-v5') -Force
Copy-Item (Join-Path $root 'src\backend\native\*.exe')          (Join-Path $payload 'Glass-v5') -Force
# Marqueur lu par le backend : démarrage en mode Qt même sans argument
New-Item -ItemType File -Force -Path (Join-Path $payload 'Glass-v5\caelestia.enabled') | Out-Null

# Un lancement de test pendant la fabrication laisserait catalogue, journal et réglages
# personnels dans le payload : ils ne doivent jamais partir chez quelqu'un d'autre.
foreach ($leak in 'catalog.json', 'preferences-glass.json', 'taskbar-session.json', 'taskbar-state.json') {
    Remove-Item -Force (Join-Path (Join-Path $payload 'Glass-v5') $leak) -ErrorAction SilentlyContinue
}
Get-ChildItem (Join-Path $payload 'Glass-v5') -Filter '*.log' -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host '→ Frontend QML' -ForegroundColor Cyan
$frontDest = Join-Path $payload 'Caelestia-Windows'
robocopy (Join-Path $root 'src\frontend') $frontDest /E /NFL /NDL /NJH /NJS /NP `
    /XD '.venv' 'validation-*' 'checkpoint-*' '__pycache__' '/XF' '*.zip' '*.pyc' | Out-Null

Write-Host '→ Python embarqué' -ForegroundColor Cyan
$pyDir = Join-Path $frontDest '.venv\Scripts'
New-Item -ItemType Directory -Force -Path $pyDir | Out-Null
$zip = Join-Path $env:TEMP "python-$pythonVersion-embed-amd64.zip"
if (-not (Test-Path $zip)) { Invoke-WebRequest $pythonUrl -OutFile $zip }
Expand-Archive -Path $zip -DestinationPath $pyDir -Force
# Le backend lance `.venv\Scripts\pythonw.exe` : la distribution embarquée vit donc dans Scripts.
# `..\..` est indispensable : sans lui, un `._pth` retire le dossier du script de sys.path
# et frontend.py ne trouve plus ses propres modules (`interaction`, `search`, …).
@('python312.zip', '.', '..\..', 'Lib\site-packages', 'import site') -join "`r`n" |
    Set-Content -Path (Join-Path $pyDir 'python312._pth') -Encoding ASCII

Write-Host '→ Dépendances Python' -ForegroundColor Cyan
python -m pip install --target (Join-Path $pyDir 'Lib\site-packages') `
    --no-warn-script-location --disable-pip-version-check --only-binary=:all: `
    -r (Join-Path $root 'src\frontend\requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Installation des dépendances Python échouée.' }

# Allègement : modules Qt que le frontend n’importe jamais
$site = Join-Path $pyDir 'Lib\site-packages\PySide6'
foreach ($dir in 'resources', 'translations', 'QtAsyncio', 'include', 'scripts') {
    Remove-Item -Recurse -Force (Join-Path $site $dir) -ErrorAction SilentlyContinue
}
foreach ($qml in 'QtWebEngine', 'Qt3D*', 'QtCharts', 'QtQuick3D', 'QtMultimedia', 'QtPdf', 'QtDataVisualization', 'QtGraphs') {
    Remove-Item -Recurse -Force (Join-Path $site "qml\$qml") -ErrorAction SilentlyContinue
}
Get-ChildItem $site -Filter 'Qt6WebEngine*.dll'   | Remove-Item -Force
Get-ChildItem $site -Filter 'Qt6Pdf.dll'          | Remove-Item -Force
Get-ChildItem $site -Filter 'Qt63D*.dll'          | Remove-Item -Force
Get-ChildItem $site -Filter 'Qt6Quick3D*.dll'     | Remove-Item -Force
Get-ChildItem $site -Filter 'Qt6Charts*.dll'      | Remove-Item -Force
Get-ChildItem $site -Filter 'Qt6Multimedia*.dll'  | Remove-Item -Force
Get-ChildItem $site -Filter '*.pyd' | Where-Object { $_.Name -match 'WebEngine|Pdf|3D|Quick3D|Charts|Multimedia|DataVisualization|Graphs' } | Remove-Item -Force

Write-Host '→ Vérification du runtime embarqué' -ForegroundColor Cyan
& (Join-Path $pyDir 'python.exe') -c "import PySide6, numpy, PIL, material_color_utilities; print('runtime ok', PySide6.__version__)"
if ($LASTEXITCODE -ne 0) { throw 'Le runtime Python embarqué ne démarre pas.' }

# Le contrôle qui manquait : le frontend doit s'importer depuis le payload, pas
# seulement depuis le venv de développement. Lancé comme un script, pour que
# sys.path soit exactement celui de l'installation.
Write-Host '→ Vérification du frontend dans le payload' -ForegroundColor Cyan
$probe = Join-Path $frontDest '_probe.py'
Set-Content -LiteralPath $probe -Encoding ASCII -Value @'
import interaction, search, live_model, capture_native, wallpaper_theme, native_glass, surface_geometry
from PySide6.QtQuick import QQuickWindow
print('frontend ok')
'@
& (Join-Path $pyDir 'python.exe') $probe
$probeCode = $LASTEXITCODE
Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
if ($probeCode -ne 0) { throw "Le frontend ne s'importe pas depuis le payload : vérifie python312._pth." }

Write-Host 'Terminal Winlestia' -ForegroundColor Cyan
$terminalDest = Join-Path $payload 'Winlestia-Terminal'
New-Item -ItemType Directory -Force -Path $terminalDest | Out-Null
foreach ($file in 'Profile.ps1','Banner.ps1','Colors.ps1','ConfigurePackagedCmd.ps1','Configure-Policy.ps1','sync_theme.py','install.py','theme-template.json','winlestia.ico') {
    Copy-Item (Join-Path $root "src\terminal\$file") $terminalDest -Force
}
foreach ($dir in 'clink','clink-profile') {
    robocopy (Join-Path $root "src\terminal\$dir") (Join-Path $terminalDest $dir) /E /NFL /NDL /NJH /NJS /NP /XF '*.log' '*history*' '*.tmp' 'clink_errorlevel*' | Out-Null
}
Write-Host '→ Compilation du setup' -ForegroundColor Cyan
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 introuvable : winget install JRSoftware.InnoSetup' }
& $iscc /Qp (Join-Path $PSScriptRoot 'Winlestia.iss')
if ($LASTEXITCODE -ne 0) { throw 'Compilation Inno Setup échouée.' }

Write-Host "`nTerminé : build\out\Winlestia-Setup-0.8.0.exe" -ForegroundColor Green
