# PowerShell 5.1 / Uninstall-Winlestia.ps1 / Script de repli : desinstalle Winlestia sans Inno Setup ni WinlestiaSetup.exe.
# Usage : Uninstall-Winlestia.ps1 [-KeepData | -RemoveData] [-DryRun] [-InstallDir <dossier>]
#   -KeepData    (defaut) conserve %LOCALAPPDATA%\Winlestia et %LOCALAPPDATA%\Panneaux.
#   -RemoveData  envoie les dossiers de donnees a la Corbeille (jamais de suppression definitive).
#   -DryRun      affiche ce qui serait fait, ne modifie rien et ne lance aucun processus.
param(
    [switch]$KeepData,
    [switch]$RemoveData,
    [switch]$DryRun,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Winlestia')
)
$ErrorActionPreference = 'Continue'
if ($KeepData -and $RemoveData) { Write-Error 'Choisir -KeepData ou -RemoveData, pas les deux.'; exit 2 }

# Garde-fou : seul %LOCALAPPDATA%\Programs\Winlestia est accepte (chemin normalise, sans casse) ;
# variable vide ou racine de lecteur refusees.
if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA) -or [string]::IsNullOrWhiteSpace($env:TEMP) -or
    [string]::IsNullOrWhiteSpace($InstallDir)) {
    Write-Error 'Variable d environnement ou InstallDir vide.'; exit 2
}
$localRoot = [IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\')
if ($localRoot -eq [IO.Path]::GetPathRoot($localRoot).TrimEnd('\')) { Write-Error 'LOCALAPPDATA est une racine de lecteur.'; exit 2 }
$expectedRoot = $localRoot + '\Programs\Winlestia'
$installRoot = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
if (-not $installRoot.Equals($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Error "Dossier d'installation refuse : $installRoot (attendu : $expectedRoot)"; exit 2
}
$installRoot = $expectedRoot
$mode = if ($DryRun) { '[SIMULATION] ' } else { '' }
$failures = 0
$leftovers = @()
$logFile = Join-Path $env:TEMP 'WinlestiaUninstall.log'
function Log([string]$Text) {
    if (-not $DryRun) { Add-Content -LiteralPath $logFile -Value ((Get-Date -Format o) + ' ' + $Text) -ErrorAction SilentlyContinue }
}

function Step([string]$Text, [scriptblock]$Action) {
    if ($DryRun) { Write-Host "$mode$Text"; return }
    Write-Host "-> $Text" -ForegroundColor Cyan
    Log $Text
    try { & $Action }
    catch { $script:failures++; Write-Warning "Echec : $Text ($($_.Exception.Message))"; Log "ECHEC : $Text ($($_.Exception.Message))" }
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WinlestiaTaskbar {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    public static bool Visible() { IntPtr h = FindWindow("Shell_TrayWnd", null); return h != IntPtr.Zero && IsWindowVisible(h); }
}
'@

# 1. Barre des taches : le chemin de restauration du projet d'abord, explorer.exe en dernier recours.
Step 'Restaurer la barre des taches Windows' {
    $glass = Join-Path $installRoot 'Glass-v5'
    $backend = Join-Path $glass 'Panneaux.Glass.exe'
    $rescue = Join-Path $glass 'RETABLIR_BARRE_WINDOWS.exe'
    if (Test-Path -LiteralPath $backend) { Start-Process -FilePath $backend -ArgumentList '--restore-taskbar' -WindowStyle Hidden -Wait }
    elseif (Test-Path -LiteralPath $rescue) { Start-Process -FilePath $rescue -WindowStyle Hidden -Wait }
    elseif (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
}

# 2. Tache planifiee de session.
Step 'Supprimer la tache planifiee WinlestiaShell' {
    & "$env:SystemRoot\System32\schtasks.exe" /delete /tn WinlestiaShell /f 2>&1 | Out-Null
}

# 3. Processus Winlestia (limites au dossier d'installation par Close-Winlestia.ps1).
$closer = Join-Path $installRoot 'Close-Winlestia.ps1'
if (-not (Test-Path -LiteralPath $closer)) { $closer = Join-Path $PSScriptRoot 'Close-Winlestia.ps1' }
Step 'Arreter les processus Winlestia' {
    & $closer -AppRoot $installRoot
}

# 4. Associations et integrations posees a l'installation (Inno : [UninstallRun]).
$files = Join-Path $installRoot 'Glass-v5\Winlestia.Files.exe'
Step 'Rendre le gestionnaire de dossiers a Windows (Winlestia.Files --restore)' {
    if (Test-Path -LiteralPath $files) { Start-Process -FilePath $files -ArgumentList '--restore' -WindowStyle Hidden -Wait }
}
$python = Join-Path $installRoot 'Caelestia-Windows\.venv\Scripts\python.exe'
$terminal = Join-Path $installRoot 'Winlestia-Terminal\install.py'
Step 'Restaurer le terminal (install.py --restore)' {
    if ((Test-Path -LiteralPath $python) -and (Test-Path -LiteralPath $terminal)) {
        Start-Process -FilePath $python -ArgumentList @('"' + $terminal + '"', '--restore') -WindowStyle Hidden -Wait
    }
}
$sensors = Join-Path $installRoot 'Glass-v5\Sensors\Winlestia.Sensors.exe'
$sensorsInstalled = Join-Path $env:ProgramFiles 'Winlestia\Sensors\Winlestia.Sensors.exe'
Step 'Retirer le service de capteurs eleve (demande UAC ; PawnIO conserve)' {
    if ((Test-Path -LiteralPath $sensors) -and (Test-Path -LiteralPath $sensorsInstalled)) {
        Start-Process -FilePath $sensors -ArgumentList 'uninstall' -Verb RunAs -WindowStyle Hidden -Wait
    }
}

# 5. Registre (HKCU uniquement).
Step 'Supprimer la valeur Run "Panneaux"' {
    Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Panneaux' -ErrorAction SilentlyContinue
}
Step 'Retirer le dossier Glass-v5 du PATH utilisateur' {
    $key = 'HKCU:\Environment'
    $current = (Get-ItemProperty -LiteralPath $key -Name Path -ErrorAction SilentlyContinue).Path
    if ($current) {
        $glass = Join-Path $installRoot 'Glass-v5'
        $alt = if ($glass.StartsWith($localRoot, [StringComparison]::OrdinalIgnoreCase)) {
            '%localappdata%' + $glass.Substring($localRoot.Length)
        } else { $glass }
        $kept = @($current -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ne $glass -and $_.TrimEnd('\') -ne $alt })
        if ($kept.Count -ne @($current -split ';' | Where-Object { $_ }).Count) {
            Set-ItemProperty -LiteralPath $key -Name Path -Value ($kept -join ';') -Type ExpandString
        }
    }
}
Step 'Supprimer l entree "Applications installees" (cle Uninstall HKCU)' {
    $uninstall = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    $inno = Join-Path $uninstall '{8E2B7C41-5F1A-4F7B-9C3D-6A2E0D5B7C18}_is1'
    if (Test-Path -LiteralPath $inno) { Remove-Item -LiteralPath $inno -Recurse -Force }
    # Nouvel installateur : toute cle HKCU dont le nom affiche est exactement Winlestia.
    foreach ($k in @(Get-ChildItem -LiteralPath $uninstall -ErrorAction SilentlyContinue)) {
        if ((Get-ItemProperty -LiteralPath $k.PSPath -ErrorAction SilentlyContinue).DisplayName -eq 'Winlestia') {
            Remove-Item -LiteralPath $k.PSPath -Recurse -Force
        }
    }
}

# 6. Raccourcis.
$programs = [Environment]::GetFolderPath('Programs')
$desktop = [Environment]::GetFolderPath('DesktopDirectory')
$shortcuts = @(
    (Join-Path $programs 'Winlestia.lnk'),
    (Join-Path $programs ('Restaurer la barre des t' + [char]0xE2 + 'ches Windows.lnk')),
    (Join-Path $programs 'Restore the Windows taskbar.lnk'),
    (Join-Path $programs 'Restaurar la barra de tareas de Windows.lnk'),
    (Join-Path $programs ('D' + [char]0xE9 + 'sinstaller Winlestia.lnk')),
    (Join-Path $programs 'Uninstall Winlestia.lnk'),
    (Join-Path $programs 'Desinstalar Winlestia.lnk'),
    (Join-Path $desktop 'Winlestia.lnk')
)
Step 'Supprimer les raccourcis du menu Demarrer et du Bureau' {
    foreach ($s in $shortcuts) { if (Test-Path -LiteralPath $s) { Remove-Item -LiteralPath $s -Force } }
}

# 7. Dossier d'installation. Le script peut tourner depuis ce dossier : on se place ailleurs d'abord.
Step "Supprimer le dossier d'installation ($installRoot)" {
    Set-Location -LiteralPath $env:TEMP
    if (Test-Path -LiteralPath $installRoot) { Remove-Item -LiteralPath $installRoot -Recurse -Force }
}

# 8. Donnees utilisateur : Corbeille uniquement, jamais de suppression definitive.
# Noms exacts utilises par le backend (Store : Panneaux ; Files, Explorer, terminal-backup : Winlestia) et par Qt
# (app_main.py : setApplicationName 'Panneaux - Caelestia Windows', avec un point median U+00B7).
if ($RemoveData) {
    $qtName = 'Panneaux ' + [char]0xB7 + ' Caelestia Windows'
    $dataDirs = @(foreach ($name in @('Winlestia', 'Panneaux', $qtName)) {
        $candidate = Join-Path $localRoot $name
        if (Test-Path -LiteralPath $candidate -PathType Container) { $candidate }
    })
    if ($DryRun -and -not $dataDirs) { Write-Host "$mode(aucun dossier de donnees trouve)" }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WinlestiaRecycle {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT {
        public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags;
        public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);
    // FO_DELETE | FOF_ALLOWUNDO (0x40) | FOF_NOCONFIRMATION (0x10) | FOF_WANTNUKEWARNING (0x4000) : si la Corbeille ne peut pas
    // recevoir le dossier, Windows avertit au lieu de le detruire ; un refus de l'utilisateur annule l'operation.
    // Structure a 64 bits uniquement (alignement 8) : l'appelant verifie [IntPtr]::Size.
    public static int Send(string path) {
        var op = new SHFILEOPSTRUCT { wFunc = 3, pFrom = path + "\0\0", fFlags = 0x40 | 0x10 | 0x4000 };
        int rc = SHFileOperationW(ref op);
        return op.fAnyOperationsAborted && rc == 0 ? 1223 : rc;
    }
}
'@
    foreach ($dir in $dataDirs) {
        Step "Envoyer a la Corbeille : $dir" {
            $drive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($dir))
            if ([IntPtr]::Size -ne 8) { throw 'PowerShell 32 bits : envoi a la Corbeille non pris en charge' }
            if ($drive.DriveType -ne [IO.DriveType]::Fixed) { throw "lecteur non local ($($drive.DriveType)) : pas de Corbeille garantie" }
            $rc = [WinlestiaRecycle]::Send($dir)
            if ($rc -ne 0 -or (Test-Path -LiteralPath $dir)) { throw "Corbeille impossible (code $rc)" }
        }
        if (-not $DryRun -and (Test-Path -LiteralPath $dir)) { $leftovers += $dir }
    }
    Step 'Supprimer la cle HKCU\Software\Winlestia (profil)' {
        Remove-Item -LiteralPath 'HKCU:\Software\Winlestia' -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "$mode Donnees conservees : $localRoot\Winlestia, $localRoot\Panneaux"
}
# 9. Invariant : jamais d'utilisateur sans barre des taches.
$taskbarOk = $true
Step 'Verifier explorer.exe et la barre des taches' {
    function Wait-Bar([int]$Seconds) {
        $until = [DateTime]::UtcNow.AddSeconds($Seconds)
        while (-not [WinlestiaTaskbar]::Visible() -and [DateTime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 300 }
        return [WinlestiaTaskbar]::Visible()
    }
    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
    if (-not (Wait-Bar 8)) {
        # explorer tourne mais sans barre : arret puis relance comme shell (Windows le relance seul, sinon on le lance).
        Log 'Barre invisible : redemarrage d explorer.exe'
        Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
        if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
        if (-not (Wait-Bar 10)) { $script:taskbarOk = $false; throw 'Shell_TrayWnd toujours invisible apres le redemarrage d explorer.exe' }
    }
}
if ($DryRun) { Write-Host "$mode Termine, rien n'a ete modifie."; exit 0 }
if ($leftovers.Count -gt 0) {
    Write-Warning ('Dossiers de donnees laisses en place (Corbeille indisponible) : ' + ($leftovers -join ' ; '))
    Log ('Dossiers laisses : ' + ($leftovers -join ' ; '))
}
if (-not $taskbarOk) { Write-Warning 'La barre des taches reste invisible.'; exit 4 }
if ($failures -gt 0) { Write-Warning "$failures etape(s) en echec. Journal : $logFile"; exit 1 }
Write-Host 'Winlestia est desinstalle.'
exit 0
