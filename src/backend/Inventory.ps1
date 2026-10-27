# language: PowerShell 5.1, file: Inventory.ps1, target: Windows 11, local catalogue
param([string]$OutputPath = (Join-Path $PSScriptRoot 'catalog.json'))
$ErrorActionPreference = 'Stop'
$catalog = New-Object 'System.Collections.Generic.List[object]'
$seen = @{}
$seenAppNames = @{}
$diagnostics = New-Object 'System.Collections.Generic.List[string]'
$profileRoot = [Environment]::GetFolderPath('UserProfile')
$documentRoot = [Environment]::GetFolderPath('MyDocuments')
$desktopRoot = [Environment]::GetFolderPath('Desktop')
$shortcutShell = New-Object -ComObject WScript.Shell
function Add-Entry([string]$Name,[string]$Target,[string]$Section,[string]$Category,[string]$IconPath='', [string]$Key='') {
    $Name=Clean-Name $Name
    if(-not $Name -or -not $Target) { return }
    if(-not $Key) {
        $Key = $Target.ToLowerInvariant()
        # Keep identifiers stable when an existing organised folder changes location.
        foreach($folder in @('02_Logiciels_et_Outils','04_Documents_Administratifs')) {
            $currentPrefix=Join-Path $desktopRoot $folder
            if($Target.Equals($currentPrefix,[StringComparison]::OrdinalIgnoreCase) -or $Target.StartsWith($currentPrefix+'\',[StringComparison]::OrdinalIgnoreCase)) {
                $Key=((Join-Path $documentRoot $folder)+$Target.Substring($currentPrefix.Length)).ToLowerInvariant()
            }
        }
    }
    if($seen.ContainsKey($Key)) { return }
    if($Section -in @('Applications','Jeux')) {
        $nameKey=($Section+':'+$Name).ToLowerInvariant()
        if($seenAppNames.ContainsKey($nameKey)) {return}
        $seenAppNames[$nameKey]=$true
    }
    $seen[$Key] = $true
    $catalog.Add([pscustomobject]@{Id=$Key;Name=$Name;Target=$Target;Section=$Section;Category=$Category;IconPath=$IconPath;SearchText=($Name+' '+$Category+' '+$Target)})
}
function Clean-Name([string]$Name) {
    return ($Name -replace '(?i)\.exe\s*-\s*Raccourci$','' -replace '(?i)\s*-\s*Raccourci$','' -replace '(?i)^Lancer\s+','' -replace '(?i)^VLC Media Player\.exe$','VLC Media Player' -replace 'Â?®|™','' -replace '(?i)\s*-\s*Windows10$','' -replace '^CrimsonDesert$','Crimson Desert' -replace '^HogwartsLegacy$','Hogwarts Legacy').Trim()
}
function Classify-App([string]$Name,[string]$Target) {
    $text = $Name+' '+$Target
    if($Name -match '^Roblox Studio$') {return @('Applications','Développement et compilation')}
    if($Name -match '(?i)^(Steam|Battle.net|Epic Games Launcher|EA|Ubisoft Connect|Client Riot|Riot Client|Hydra|XBOX|Minecraft Launcher)$') { return @('Applications','Lanceurs de jeux') }
    if($text -match '(?i)SteamVR|TrinusPSVR|MuMu|VirtualBox|Portail de réalité mixte') { return @('Applications','Émulation et réalité virtuelle') }
    if($text -match '(?i)Wand|WeMod|SteamTools|LuaTools|Content Manager|Satisfactory (Mod Manager|Modeler)|vibranceGUI') { return @('Applications','Outils de jeu et mods') }
    if($text -match '(?i)rungameid|uplay://|com\.epicgames|\\steamapps\\common\\|\\Riot Games\\|osu!|CrimsonDesert|HogwartsLegacy|Rust\.exe|Neverness|AION|League of Legends|Teamfight Tactics|VALORANT|Roblox|FragPunk|Aniimo|Planet Coaster|Solitaire|Assetto.?Corsa.?Evo|Overwatch|SnowRunner') {
        $kind = 'Autres jeux'
        if($text -match '(?i)Apex|Marvel Rivals|Overwatch|Deadlock|Rust|VALORANT|Rainbow|Counter.Strike|FragPunk') { $kind='FPS et compétitif' }
        elseif($text -match '(?i)Assetto|Riders|Rocket League|Forza|Crew|Content Manager|SnowRunner') { $kind='Course et sport' }
        elseif($text -match '(?i)Hogwarts|Crimson|Neverness|AION|Genshin|Aniimo') { $kind='Aventure et RPG' }
        elseif($text -match '(?i)osu!') { $kind='Rythme' }
        elseif($text -match '(?i)Ambitions|Satisfactory|Cities|Coaster|Teamfight Tactics|League of Legends') { $kind='Gestion et stratégie' }
        return @('Jeux',$kind)
    }
    if($text -match '(?i)Antigravity|Freebuff|Grok|Hermes|OpenCode|ZCode|mecagent|Ademi|ChatGPT|Codex|Claude|Cursor|Copilot|Harvest Bot') { return @('Applications','IA et assistants de code') }
    if($text -match '(?i)Python|Anaconda|Jupyter|Spyder') { return @('Applications','Python et données') }
    if($text -match '(?i)Visual Studio|Arduino|CMake|Git |Git\\|Docker|Node\.js|WSL|MSYS|MetaEditor|Roblox Studio') { return @('Applications','Développement et compilation') }
    if($text -match '(?i)Process Hacker|HxD|PE Viewer|WinDbg|Global Flags|Application Verifier|Extreme Injector|Vyra|GPUView|Performance Analyzer|Performance Recorder') { return @('Applications','Reverse engineering et diagnostic') }
    if($text -match '(?i)Blender|SOLIDWORKS|eDrawings|Visualize|Treehouse|Toolbox|Costing') { return @('Applications','3D et conception') }
    if($text -match '(?i)OBS|Recordly|VLC|Spotify|Media Player|Photos|Clipchamp|Dubbing|Films et TV|Lecteur multimédia|Capture d.écran|DTS CUSTOM|Realtek Audio') { return @('Applications','Audio, vidéo et capture') }
    if($text -match '(?i)Chrome|Firefox|Edge|Brave|Tor Browser|Browser') { return @('Applications','Navigateurs') }
    if($text -match '(?i)Discord|Telegram|Mail|Outlook|Zoom|Skype|Marl.Bot|Teams|People|Mobile connecté') { return @('Applications','Communication') }
    if($text -match '(?i)Word|Excel|PowerPoint|Access$|Publisher|OneNote|Sticky|Office|Notepad|Bloc.notes|Calculator|Calculatrice|Acrobat|Pepperstone|MetaTrader|Calendar|To Do') { return @('Applications','Bureautique et finance') }
    if($text -match '(?i)Proton|OneDrive|MEGA|Torrent|Windscribe|7.Zip|WinRAR|Paragon|Linux Reader|File Explorer|Files$|DiskInternals') { return @('Applications','Fichiers, stockage et réseau') }
    if($text -match '(?i)NVIDIA|Logitech|Windows|Control Panel|Task Manager|Registry|System|services|Remote Desktop|Recovery|Management|Monitor|Magnify|Narrator|LiveCaptions|VoiceAccess|Character Map|Disk Cleanup|dfrgui|Steps Recorder|Armoury|Assistance rapide|HP Smart|iCUE|Game Bar|Météo|Actualités|Forfaits|Family|Run$|Power Automate|Java') { return @('Applications','Windows et périphériques') }
    if($text -match '(?i)Paint|Wink') {return @('Applications','Image et graphisme')}
    return @('Applications','Autres logiciels')
}
# Installed Steam games: manifests, libraryfolders and real installation directories.
$steamRoots = New-Object 'System.Collections.Generic.List[string]'
foreach($steamKey in @('HKCU:\Software\Valve\Steam','HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
    $steamInfo=Get-ItemProperty -LiteralPath $steamKey -ErrorAction SilentlyContinue
    if($steamInfo.SteamPath) { $steamRoots.Add($steamInfo.SteamPath) }
    if($steamInfo.InstallPath) { $steamRoots.Add($steamInfo.InstallPath) }
}
$steamRoots.Add('C:\Program Files (x86)\Steam')
foreach($steamRoot in @($steamRoots.ToArray())) {
    $libraryFile=Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
    if(Test-Path -LiteralPath $libraryFile) {
        foreach($match in [regex]::Matches((Get-Content -LiteralPath $libraryFile -Raw),'"path"\s+"([^"]+)"')) { $steamRoots.Add($match.Groups[1].Value.Replace('\\','\')) }
    }
}
$steamInstalled=@{}
foreach($steamRoot in @($steamRoots.ToArray() | Select-Object -Unique)) {
    foreach($manifest in @(Get-ChildItem -LiteralPath (Join-Path $steamRoot 'steamapps') -Filter 'appmanifest_*.acf' -ErrorAction SilentlyContinue)) {
        try {
            $raw=Get-Content -LiteralPath $manifest.FullName -Raw -Encoding UTF8
            $appId=[regex]::Match($raw,'"appid"\s+"([^"]+)"').Groups[1].Value
            $gameName=[regex]::Match($raw,'"name"\s+"([^"]+)"').Groups[1].Value
            $installDir=[regex]::Match($raw,'"installdir"\s+"([^"]+)"').Groups[1].Value
            $flags=[regex]::Match($raw,'"StateFlags"\s+"([^"]+)"').Groups[1].Value
            $gameDir=Join-Path $steamRoot ('steamapps\common\'+$installDir)
            if(-not (Test-Path -LiteralPath $gameDir) -or (([int]$flags -band 4) -ne 4)) { continue }
            $steamInstalled[$appId]=$true
            $iconFile=Get-ChildItem -LiteralPath (Join-Path $steamRoot 'steam\games') -Filter '*.ico' -ErrorAction SilentlyContinue | Where-Object { $_.Name -like ($appId+'*') } | Select-Object -First 1
            $iconPath=if($iconFile){$iconFile.FullName}else{Join-Path $steamRoot 'steam.exe'}
            $kind=Classify-App $gameName ('steam://rungameid/'+$appId)
            if($gameName -match 'Steamworks|Proton|Redistributable') { continue }
            if($gameName -match 'SteamVR') { $kind=@('Applications','Émulation et réalité virtuelle') }
            Add-Entry $gameName ('steam://rungameid/'+$appId) $kind[0] $kind[1] $iconPath ('steam:'+$appId)
        } catch { $diagnostics.Add('Steam: '+$_.Exception.Message) }
    }
}
$shortcutRoots=@($desktopRoot,[Environment]::GetFolderPath('CommonDesktopDirectory'),[Environment]::GetFolderPath('StartMenu'),[Environment]::GetFolderPath('CommonStartMenu'))
$ubisoftInstalled=@{}
foreach($install in @(Get-ChildItem -LiteralPath 'HKLM:\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs','HKLM:\SOFTWARE\Ubisoft\Launcher\Installs' -ErrorAction SilentlyContinue)) {
    $installInfo=Get-ItemProperty -LiteralPath $install.PSPath -ErrorAction SilentlyContinue
    if($installInfo.InstallDir -and (Test-Path -LiteralPath $installInfo.InstallDir)) {$ubisoftInstalled[$install.PSChildName]=$true}
}
foreach($shortcutRoot in $shortcutRoots) {
    if(-not (Test-Path -LiteralPath $shortcutRoot)) {continue}
    $recursive=$shortcutRoot -notin @($desktopRoot,[Environment]::GetFolderPath('CommonDesktopDirectory'))
    $files=if($recursive){Get-ChildItem -LiteralPath $shortcutRoot -File -Recurse -ErrorAction SilentlyContinue}else{Get-ChildItem -LiteralPath $shortcutRoot -File -ErrorAction SilentlyContinue}
    foreach($file in $files) {
        if($file.Extension -notin '.lnk','.url','.exe') {continue}
        $name=Clean-Name $file.BaseName
        if($name -match '(?i)uninstall|désinstall|documentation|\bhelp\b|\baide\b|release notes|changelog|readme|licen[cs]e|manuels?|pydoc|module docs|online documentation|Install Additional|update|mises? à jour|disponibilité de mises|Panneaux|^Secours -|Swish Panel|Navigation privée|Windows Kits Feedback|Donate|Support Center|EA Error Reporter') {continue}
        try {
            $target=$file.FullName; $iconPath=$target; $identity=$null; $arguments=''
            if($file.Extension -eq '.lnk') {
                $link=$shortcutShell.CreateShortcut($file.FullName)
                $resolved=[Environment]::ExpandEnvironmentVariables($link.TargetPath)
                $arguments=$link.Arguments
                if($resolved -and -not (Test-Path -LiteralPath $resolved) -and $resolved -notmatch '^shell:') {continue}
                $iconPath=$link.IconLocation -replace ',\s*-?\d+$',''
                if(-not $iconPath -or -not (Test-Path -LiteralPath $iconPath)) {$iconPath=$resolved}
                $identity=if($resolved){'app:'+($resolved+'|'+$arguments).ToLowerInvariant()}else{'name:'+$name.ToLowerInvariant()}
                # Squirrel launchers can have different executable paths for the same app.
                if($name -match '^(Discord|Wand|Wand \(WeMod\)|Vyra( \(2\))?|Firefox|WinRAR|Ubisoft Connect|Steam|Content Manager)$') { $identity='named:'+($name -replace ' \(WeMod\)| \(2\)','').ToLowerInvariant() }
                $kind=Classify-App $name ($resolved+' '+$arguments)
            } elseif($file.Extension -eq '.url') {
                $urlLine=Get-Content -LiteralPath $file.FullName -Encoding UTF8 | Where-Object {$_ -match '^URL='} | Select-Object -First 1
                if(-not $urlLine){continue}
                $url=$urlLine.Substring(4)
                if($url -match '^uplay://launch/(\d+)' -and -not $ubisoftInstalled.ContainsKey($Matches[1])) {continue}
                if($url -match '^https?://' -and $shortcutRoot -notin @($desktopRoot,[Environment]::GetFolderPath('CommonDesktopDirectory'))) {continue}
                $iconLine=Get-Content -LiteralPath $file.FullName | Where-Object {$_ -match '^IconFile='} | Select-Object -First 1
                if($iconLine){$iconPath=$iconLine.Substring(9)}
                if($url -match '^steam://rungameid/(\d+)') {
                    if(-not $steamInstalled.ContainsKey($Matches[1])){continue}
                    $identity='steam:'+$Matches[1]
                    # Prefer shortcut icon to a generic Steam icon in the manifest record.
                    $existing=$catalog | Where-Object {$_.Id -eq $identity} | Select-Object -First 1
                    if($existing -and (Test-Path -LiteralPath $iconPath)){$existing.IconPath=$iconPath}
                } else {$identity='url:'+$url.ToLowerInvariant()}
                $kind=Classify-App $name $url
            } else { $kind=Classify-App $name $target; $identity='app:'+($target+'|').ToLowerInvariant() }
            Add-Entry $name $target $kind[0] $kind[1] $iconPath $identity
        } catch {$diagnostics.Add($file.Name+': '+$_.Exception.Message)}
    }
}
# Epic manifests cover installed games without desktop shortcuts.
foreach($manifest in @(Get-ChildItem -LiteralPath 'C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests' -Filter '*.item' -ErrorAction SilentlyContinue)) {
    try {
        $epic=Get-Content -LiteralPath $manifest.FullName -Raw | ConvertFrom-Json
        if($epic.bIsIncompleteInstall -or -not $epic.LaunchExecutable -or -not (Test-Path -LiteralPath (Join-Path $epic.InstallLocation $epic.LaunchExecutable))) {continue}
        $kind=Classify-App $epic.DisplayName ('com.epicgames '+$epic.InstallLocation)
        $uri='com.epicgames.launcher://apps/'+[uri]::EscapeDataString($epic.AppName)+'?action=launch&silent=true'
        if(-not ($catalog | Where-Object {$_.Name -eq $epic.DisplayName})) {Add-Entry $epic.DisplayName $uri $kind[0] $kind[1] (Join-Path $epic.InstallLocation $epic.LaunchExecutable) ('epic:'+$epic.AppName)}
    } catch {$diagnostics.Add('Epic: '+$_.Exception.Message)}
}
# Packaged applications (including Codex), without duplicating Win32 shortcuts.
$epicLauncher='C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe'
if(Test-Path -LiteralPath $epicLauncher) {
    foreach($pending in @(Get-ChildItem -LiteralPath 'C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\Pending' -Filter '*.item' -ErrorAction SilentlyContinue)) {
        try {
            $game=Get-Content -LiteralPath $pending.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            if($game.bIsIncompleteInstall -and (Test-Path -LiteralPath $game.InstallLocation)) {
                Add-Entry ((Clean-Name $game.DisplayName)+' — installation à terminer') $epicLauncher 'Jeux' 'Installations à terminer dans Epic' $epicLauncher ('epic-pending:'+$game.AppName)
            }
        }catch{$diagnostics.Add('Epic en attente: '+$_.Exception.Message)}
    }
}
try {
    $packageMap=@{}
    foreach($package in @(Get-AppxPackage -ErrorAction Stop)) {if($package.InstallLocation -and (Test-Path -LiteralPath $package.InstallLocation)) {$packageMap[$package.PackageFamilyName]=$package}}
    foreach($app in @(Get-StartApps -ErrorAction Stop)) {
        if($app.AppID -notmatch '!' -or $app.Name -match '(?i)Windows Security|Sécurité Windows|Xbox Game Bar|Experience|Parental|Get Help|Obtenir de l.aide|Microsoft Store|Mixed Reality|Web Media|App Installer|Dev Home') {continue}
        $family=$app.AppID.Split('!')[0]
        if(-not $packageMap.ContainsKey($family)) {continue}
        if($catalog | Where-Object {$_.Name -eq $app.Name}) {continue}
        $kind=Classify-App $app.Name $app.AppID
        $package=$packageMap[$family]
        $iconPath=''
        $configPath=Join-Path $package.InstallLocation 'MicrosoftGame.config'
        if(Test-Path -LiteralPath $configPath) {
            [xml]$gameConfig=Get-Content -LiteralPath $configPath -Raw -Encoding UTF8
            $gameExecutable=$gameConfig.Game.ExecutableList.Executable | Select-Object -First 1
            if($gameExecutable.Name){$iconPath=Join-Path $package.InstallLocation $gameExecutable.Name;if(-not (Test-Path -LiteralPath $iconPath)){continue}}
        } else {
            try {
                [xml]$appManifest=Get-Content -LiteralPath (Join-Path $package.InstallLocation 'AppxManifest.xml') -Raw -Encoding UTF8
                $application=$appManifest.Package.Applications.Application | Where-Object {$_.Id -eq $app.AppID.Split('!')[1]} | Select-Object -First 1
                if($application.Executable -and $application.Executable -notmatch '^\$'){$iconPath=Join-Path $package.InstallLocation $application.Executable}
            }catch{}
        }
        try {
            [xml]$logoManifest=Get-Content -LiteralPath (Join-Path $package.InstallLocation 'AppxManifest.xml') -Raw -Encoding UTF8
            $logoApp=$logoManifest.Package.Applications.Application | Where-Object {$_.Id -eq $app.AppID.Split('!')[1]} | Select-Object -First 1
            $logo=$logoApp.VisualElements.Square44x44Logo
            if(-not $logo){$logo=$logoApp.VisualElements.Square150x150Logo}
            if($logo -and $logo -notmatch '^ms-resource:') {
                $logoFull=Join-Path $package.InstallLocation $logo
                $logoDirectory=Split-Path -Parent $logoFull
                $logoStem=[IO.Path]::GetFileNameWithoutExtension($logoFull)
                $bestLogo=Get-ChildItem -LiteralPath $logoDirectory -Filter ($logoStem+'*.png') -File -ErrorAction SilentlyContinue |
                    Sort-Object @{Expression={if($_.Name -match 'targetsize-256.*altform-unplated'){0}elseif($_.Name -match 'targetsize-256'){1}elseif($_.Name -match 'scale-400'){2}elseif($_.Name -match 'scale-200'){3}else{4}}},@{Expression='Length';Descending=$true} | Select-Object -First 1
                if($bestLogo){$iconPath=$bestLogo.FullName}
            }
        }catch{}
        Add-Entry $app.Name ('shell:AppsFolder\'+$app.AppID) $kind[0] $kind[1] $iconPath ('uwp:'+$app.AppID)
    }
} catch {$diagnostics.Add('Applications Windows: '+$_.Exception.Message)}
# Known portable games and Xbox content not exposed by the Start menu.
foreach($game in @(
    @{Name='Assetto Corsa EVO';Path='E:\asseto corsa evo\Assetto.Corsa.EVO-InsaneRamZes\AssettoCorsaEVO.exe'},
    @{Name='SnowRunner';Path='E:\Game Pass\SnowRunner - Windows10\Content\SnowRunner.exe'},
    @{Name='Overwatch';Path='E:\Overwatch\_retail_\Overwatch.exe'}
)) {
    if(Test-Path -LiteralPath $game.Path) {$kind=Classify-App $game.Name $game.Path;$gameIcon=Get-ChildItem -LiteralPath (Split-Path -Parent $game.Path) -Filter '*.ico' -File -ErrorAction SilentlyContinue | Select-Object -First 1;$gameIconPath=if($gameIcon){$gameIcon.FullName}else{$game.Path};Add-Entry $game.Name $game.Path $kind[0] $kind[1] $gameIconPath}
}
function Add-Folder([string]$Path,[string]$Name,[string]$Section,[string]$Category) {
    if(Test-Path -LiteralPath $Path -PathType Container) { Add-Entry $Name $Path $Section $Category $Path }
}
function Add-Children([string]$Path,[string]$Section,[string]$Category) {
    foreach($folder in @(Get-ChildItem -LiteralPath $Path -Directory -ErrorAction SilentlyContinue)) {
        if($folder.Name -match '^\.|^(node_modules|venv|env|__pycache__|\.git|bin|obj|dist|build|target)$' -or ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint)) {continue}
        Add-Folder $folder.FullName $folder.Name $Section $Category
    }
}
Add-Folder $documentRoot 'Documents' 'Dossiers' 'Accès directs'
Add-Folder $desktopRoot 'Bureau — tous les fichiers' 'Dossiers' 'Accès directs'
Add-Folder (Join-Path $profileRoot 'Downloads') 'Téléchargements' 'Dossiers' 'Accès directs'
Add-Folder $profileRoot 'Dossier personnel' 'Dossiers' 'Accès directs'
foreach($drive in [IO.DriveInfo]::GetDrives()) {if($drive.IsReady -and $drive.DriveType -eq 'Fixed'){Add-Folder $drive.RootDirectory.FullName ('Disque '+$drive.Name.TrimEnd('\')) 'Dossiers' 'Disques'}}
$tools=Join-Path $documentRoot '02_Logiciels_et_Outils'
if(-not (Test-Path -LiteralPath $tools)) {$tools=Join-Path $desktopRoot '02_Logiciels_et_Outils'}
Add-Folder (Join-Path $tools 'Cheats_et_Scripts') 'Cheats et scripts' 'Projets' 'Cheats et scripts'
Add-Children (Join-Path $tools 'Cheats_et_Scripts') 'Projets' 'Cheats et scripts'
Add-Children (Join-Path $tools 'Cheats_et_Scripts\My ch') 'Projets' 'My ch — projets'
Add-Folder (Join-Path $tools 'Reverse_Engineering') 'Reverse engineering' 'Projets' 'Reverse engineering'
Add-Children (Join-Path $tools 'Reverse_Engineering') 'Projets' 'Reverse engineering'
Add-Folder (Join-Path $tools 'Discord_et_Telegram') 'Bots Discord et Telegram' 'Projets' 'Bots et automatisation'
Add-Children (Join-Path $tools 'Discord_et_Telegram') 'Projets' 'Bots et automatisation'
Add-Folder (Join-Path $tools 'Frequtrade') 'Trading et Freqtrade' 'Projets' 'Trading'
Add-Children (Join-Path $tools 'Frequtrade') 'Projets' 'Trading'
Add-Folder (Join-Path $documentRoot '03_Projets_IA') 'Projets IA' 'Projets' 'Intelligence artificielle'
Add-Children (Join-Path $documentRoot '03_Projets_IA') 'Projets' 'Intelligence artificielle'
foreach($folderName in @('Codex','Claude','Arduino')) {
    Add-Folder (Join-Path $documentRoot $folderName) $folderName 'Projets' 'Développement'
    Add-Children (Join-Path $documentRoot $folderName) 'Projets' 'Développement'
}
Add-Children (Join-Path $documentRoot 'Claude\Projects') 'Projets' 'Développement'
Add-Children (Join-Path $desktopRoot '03_Projets') 'Projets' 'Projets du Bureau'
foreach($folder in @(Get-ChildItem -LiteralPath $desktopRoot -Directory -ErrorAction SilentlyContinue)) {
    if($folder.Name -notmatch '^0[2345]_') {Add-Folder $folder.FullName $folder.Name 'Projets' 'Projets du Bureau'}
}
$adminRoot=Join-Path $documentRoot '04_Documents_Administratifs'
if(-not (Test-Path -LiteralPath $adminRoot)) {$adminRoot=Join-Path $desktopRoot '04_Documents_Administratifs'}
Add-Folder $adminRoot 'Administratif' 'Administratif' 'Tous les documents'
Add-Children $adminRoot 'Administratif' 'Dossiers administratifs'
foreach($folder in @(Get-ChildItem -LiteralPath $adminRoot -Directory -ErrorAction SilentlyContinue)) {Add-Children $folder.FullName 'Administratif' $folder.Name}
Add-Children (Join-Path $desktopRoot '05_Administratif') 'Administratif' 'Administratif du Bureau'
$mediaRoot=Join-Path $documentRoot '05_Medias'
Add-Folder $mediaRoot 'Médias' 'Médias' 'Bibliothèque'
foreach($mediaName in @('Images','Videos','Musique')) {
    $label=if($mediaName -eq 'Videos'){'Vidéos'}else{$mediaName}
    Add-Folder (Join-Path $mediaRoot $mediaName) $label 'Médias' $label
    Add-Children (Join-Path $mediaRoot $mediaName) 'Médias' $label
}
Add-Children (Join-Path $desktopRoot '04_Medias') 'Médias' 'Médias du Bureau'
Add-Folder (Join-Path $tools 'Modeles_3D') 'Modèles 3D' 'Médias' 'Ressources de création'
Add-Folder (Join-Path $tools 'Modeles_IA') 'Modèles IA' 'Projets' 'Intelligence artificielle'
foreach($folderName in @('Installateurs','Installateurs_et_Archives','Autres')) {Add-Folder (Join-Path $tools $folderName) ($folderName -replace '_',' ') 'Dossiers' 'Archives et outils'}
Add-Folder (Join-Path $documentRoot '01_Jeux') 'Sauvegardes et configurations des jeux' 'Dossiers' 'Données des jeux'
Add-Children (Join-Path $documentRoot '01_Jeux') 'Dossiers' 'Données des jeux'
Add-Folder (Join-Path $documentRoot 'MuMuSharedFolder') 'MuMu — fichiers partagés' 'Dossiers' 'Émulation'
$result=[pscustomobject]@{Version=5;GeneratedAt=(Get-Date -Format 'o');Entries=@($catalog.ToArray() | Sort-Object Section,Category,Name);Diagnostics=$diagnostics.ToArray()}
$tempPath=$OutputPath+'.tmp'
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $tempPath -Encoding UTF8
if(Test-Path -LiteralPath $OutputPath) {[IO.File]::Replace($tempPath,$OutputPath,($OutputPath+'.bak'))} else {[IO.File]::Move($tempPath,$OutputPath)}
Write-Output ('Catalogue : '+$catalog.Count+' elements ; diagnostics : '+$diagnostics.Count)
