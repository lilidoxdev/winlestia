# Invoked in Windows Terminal's packaged context by Restore.ps1.
$root=$PSScriptRoot
$backup=Get-Content -LiteralPath (Join-Path $root 'backups\packaged-autorun-before.json') -Raw | ConvertFrom-Json
$key='HKCU:\Software\Microsoft\Command Processor'
if($backup.Exists){Set-ItemProperty -LiteralPath $key -Name AutoRun -Value $backup.Value}
else{Remove-ItemProperty -LiteralPath $key -Name AutoRun -ErrorAction SilentlyContinue}
[IO.File]::WriteAllText((Join-Path $root 'validation\package-restored.flag'),'restored')
