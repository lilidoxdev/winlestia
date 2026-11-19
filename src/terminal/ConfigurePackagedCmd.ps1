$path='HKCU:\Software\Microsoft\Command Processor'
$existing=Get-ItemProperty -LiteralPath $path -ErrorAction SilentlyContinue
$before=[pscustomobject]@{Exists=[bool]($existing.PSObject.Properties.Name -contains 'AutoRun');Value=[string]$existing.AutoRun}
$beforeFile='__WINLESTIA_TERMINAL__\backups\packaged-autorun-before.json'
if(!(Test-Path -LiteralPath $beforeFile)){$before | ConvertTo-Json | Set-Content -Encoding UTF8 $beforeFile}
New-Item -Path $path -Force | Out-Null
$autoRun='"__WINLESTIA_TERMINAL__\clink\clink_x64.exe" inject --quiet --profile "__WINLESTIA_TERMINAL__\clink-profile"'
Set-ItemProperty -LiteralPath $path -Name AutoRun -Type String -Value $autoRun
$result=[pscustomobject]@{AutoRun=[string](Get-ItemProperty -LiteralPath $path).AutoRun;Applied=$true}
$result | ConvertTo-Json | Set-Content -Encoding UTF8 '__WINLESTIA_TERMINAL__\validation\packaged-autorun.json'
