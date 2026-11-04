# Test-time parity helper: extracts ONLY the Clean-Name function text from Inventory.ps1 (never runs the script) and
# applies it to the inputs. Keep in sync with src/backend/Platform/AppNameNormalizer.cs.
param([Parameter(Mandatory)][string]$InventoryPath,[Parameter(Mandatory)][string]$InputPath,[Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
$text=[IO.File]::ReadAllText($InventoryPath,[Text.Encoding]::UTF8)
$m=[regex]::Match($text,'(?ms)^function Clean-Name\(.*?\) \{.*?^\}')
if(-not $m.Success){ throw 'Clean-Name not found in Inventory.ps1' }
Invoke-Expression $m.Value
$inputs=[IO.File]::ReadAllText($InputPath,[Text.Encoding]::UTF8) | ConvertFrom-Json
$out=@(); foreach($i in @($inputs)){ $out+=[string](Clean-Name ([string]$i)) }
$json=ConvertTo-Json -InputObject $out -Compress
[IO.File]::WriteAllText($OutputPath,$json,(New-Object Text.UTF8Encoding($false)))
