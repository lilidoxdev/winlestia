# PowerShell 5.1+ / Restore pre-Winlestia Terminal settings.
[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='High')]
param()
$root=$PSScriptRoot
$backup=(Get-Content -LiteralPath (Join-Path $root 'backup-location.txt') -Raw).Trim()
$snapshot=Get-Content -LiteralPath (Join-Path $backup 'snapshot.json') -Raw | ConvertFrom-Json
if(!$PSCmdlet.ShouldProcess('Windows Terminal et profils de la session','Rétablir la configuration sauvegardée')){return}
[IO.File]::WriteAllText((Join-Path $root 'theme-sync.stop'),'stop')
Start-Sleep -Seconds 5
if(Test-Path -LiteralPath (Join-Path $root 'backups\packaged-autorun-before.json')){
    & "$env:LOCALAPPDATA\Microsoft\WindowsApps\wt.exe" -w Winlestia-Restore new-tab powershell.exe -NoProfile -File (Join-Path $root 'RestorePackagedCmd.ps1')
    for($i=0;$i -lt 30;$i++){if(Test-Path -LiteralPath (Join-Path $root 'validation\package-restored.flag')){break};Start-Sleep -Milliseconds 300}
}
foreach($file in $snapshot.files){
    if($file.existed){Copy-Item -LiteralPath $file.backup -Destination $file.path -Force}
    elseif(Test-Path -LiteralPath $file.path){
        $text=Get-Content -LiteralPath $file.path -Raw
        $text=$text -replace '(?s)\r?\n?# >>> Winlestia Terminal >>>.*?# <<< Winlestia Terminal <<<\r?\n?',''
        [IO.File]::WriteAllText($file.path,$text,(New-Object Text.UTF8Encoding($true)))
    }
}
foreach($item in $snapshot.registry){
    $key='HKCU:\'+$item.key
    if($item.existed){New-Item -Path $key -Force | Out-Null;Set-ItemProperty -LiteralPath $key -Name $item.name -Value $item.value}
    elseif(Test-Path -LiteralPath $key){Remove-ItemProperty -LiteralPath $key -Name $item.name -ErrorAction SilentlyContinue}
}
Write-Host 'Configuration précédente rétablie. Ouvre un nouveau terminal.'
