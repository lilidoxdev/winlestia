# language: PowerShell, file: Install.ps1, target: Windows 11, reversible Glass integration
$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
$taskExecutable=Join-Path $taskRoot 'Panneaux.Glass.exe'
$taskParent=Split-Path -Parent $taskRoot
$taskPrograms=[Environment]::GetFolderPath('Programs')
$taskDesktop=[Environment]::GetFolderPath('Desktop')
$taskDocuments=[Environment]::GetFolderPath('MyDocuments')
$taskBackup=Join-Path $taskRoot 'avant-installation'
New-Item -ItemType Directory -Path $taskBackup -Force | Out-Null
if(-not (Test-Path -LiteralPath $taskExecutable)){throw "Compiler Glass avant son activation."}
$taskLinks=@((Join-Path $taskDesktop 'Panneaux.lnk'),(Join-Path $taskPrograms 'Panneaux - Espace personnel.lnk'),(Join-Path $taskPrograms 'Panneaux Glass.lnk'))
$taskShell=New-Object -ComObject WScript.Shell
foreach($taskPath in $taskLinks) {
    if(Test-Path -LiteralPath $taskPath) {
        $taskCopy=Join-Path $taskBackup (([IO.Path]::GetFileNameWithoutExtension($taskPath))+'-'+([IO.Path]::GetDirectoryName($taskPath).GetHashCode())+'.lnk')
        if(-not (Test-Path -LiteralPath $taskCopy)){Copy-Item -LiteralPath $taskPath -Destination $taskCopy}
    }
    $taskLink=$taskShell.CreateShortcut($taskPath)
    $taskLink.TargetPath=$taskExecutable
    $taskLink.Arguments='--show'
    $taskLink.WorkingDirectory=$taskRoot
    $taskLink.WindowStyle=1
    $taskLink.Description='Panneaux Glass — espace personnel de dj'
    $taskLink.IconLocation=(Join-Path $taskRoot 'glass.ico')+',0'
    $taskLink.Save()
}
$taskRescue=Join-Path $taskDocuments 'SECOURS_BUREAU.exe'
Copy-Item -LiteralPath (Join-Path $taskRoot 'SECOURS_BUREAU.exe') -Destination $taskRescue -Force
foreach($taskRescueChoice in @(@{Name='Secours - Bureau organisé';Arguments=''},@{Name="Secours - Positions d'origine";Arguments='--exact'})) {
    $taskLink=$taskShell.CreateShortcut((Join-Path $taskPrograms ($taskRescueChoice.Name+'.lnk')))
    $taskLink.TargetPath=$taskRescue
    $taskLink.Arguments=$taskRescueChoice.Arguments
    $taskLink.WindowStyle=1
    $taskLink.WorkingDirectory=$taskRoot
    $taskLink.Description='Retrouver le Bureau Windows et arrêter Panneaux'
    $taskLink.Save()
}
$taskLauncher=Join-Path $taskParent 'Lancer_Panneaux.cmd'
$taskLauncherBackup=Join-Path $taskBackup 'Lancer_Panneaux.cmd'
if((Test-Path -LiteralPath $taskLauncher) -and -not (Test-Path -LiteralPath $taskLauncherBackup)){Copy-Item -LiteralPath $taskLauncher -Destination $taskLauncherBackup}
$taskLauncherContent='@echo off'+"`r`n"+'start "" "%~dp0Glass-v5\Panneaux.Glass.exe" --show'+"`r`n"+'exit /b'+"`r`n"
[IO.File]::WriteAllText($taskLauncher,$taskLauncherContent,[Text.Encoding]::ASCII)
Set-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Panneaux -Value ('"'+$taskExecutable+'"')
$taskApproval='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$taskApproved=(Get-ItemProperty -LiteralPath $taskApproval -Name Panneaux -ErrorAction SilentlyContinue).Panneaux
if($taskApproved -and $taskApproved[0] -eq 3){$taskApproved[0]=2;Set-ItemProperty -LiteralPath $taskApproval -Name Panneaux -Value $taskApproved}
$taskVisibility=Start-Process -FilePath (Join-Path $taskRoot 'SECOURS_BUREAU.exe') -ArgumentList '--hide','--silent' -WindowStyle Hidden -Wait -PassThru
if($taskVisibility.ExitCode -ne 0){throw "Le Bureau Windows ne masque pas ses icônes."}
Write-Output 'Glass activé : bord droit, démarrage automatique et secours indépendant.'
