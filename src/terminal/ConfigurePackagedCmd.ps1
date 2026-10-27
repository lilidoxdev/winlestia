$path='HKCU:\Software\Microsoft\Command Processor'
$existing=Get-ItemProperty -LiteralPath $path -ErrorAction SilentlyContinue
$before=[pscustomobject]@{Exists=[bool]($existing.PSObject.Properties.Name -contains 'AutoRun');Value=[string]$existing.AutoRun}
if(!(Test-Path -LiteralPath '__WINLESTIA_TERMINAL__\backups\packaged-autorun-before.json')){$before | ConvertTo-Json | Set-Content -Encoding UTF8 '__WINLESTIA_TERMINAL__\backups\packaged-autorun-before.json'}
New-Item -Path $path -Force | Out-Null
Set-ItemProperty -LiteralPath $path -Name AutoRun -Type String -Value '"__WINLESTIA_TERMINAL__\clink\clink_x64.exe" inject --quiet --profile "__WINLESTIA_TERMINAL__\clink-profile"'
[pscustomobject]@{AutoRun=[string](Get-ItemProperty -LiteralPath $path).AutoRun;Applied=$true} | ConvertTo-Json | Set-Content -Encoding UTF8 '__WINLESTIA_TERMINAL__\validation\packaged-autorun.json'
