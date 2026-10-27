# PowerShell 5.1 / Close-Winlestia.ps1 / Scoped installer cleanup, never other Python applications.
param([Parameter(Mandatory=$true)][string]$AppRoot,[switch]$ListOnly)
$ErrorActionPreference='Stop'
$installRoot=[IO.Path]::GetFullPath($AppRoot).TrimEnd('\')+'\'
if($installRoot.Length -lt 12 -or $installRoot -eq [IO.Path]::GetPathRoot($installRoot)){throw 'Unexpected installation root'}
$targets=@(Get-CimInstance Win32_Process | Where-Object {
 $_.ExecutablePath -and $_.ExecutablePath.StartsWith($installRoot,[StringComparison]::OrdinalIgnoreCase) -and
 (($_.Name -eq 'Panneaux.Glass.exe') -or ($_.Name -in @('python.exe','pythonw.exe') -and $_.CommandLine -match '(frontend\.py|sync_theme\.py)'))
})
if($ListOnly){$targets | Select-Object ProcessId,Name;return}
$backend=Join-Path $installRoot 'Glass-v5\Panneaux.Glass.exe'
if(Test-Path -LiteralPath $backend){$quit=Start-Process -FilePath $backend -ArgumentList '--quit' -WindowStyle Hidden -PassThru;$quit.WaitForExit(1000) | Out-Null}
$until=[DateTime]::UtcNow.AddSeconds(4)
do{ $remaining=@($targets | Where-Object {Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue});if(!$remaining){break};Start-Sleep -Milliseconds 100 }while([DateTime]::UtcNow -lt $until)
foreach($target in $remaining){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($target.ProcessId)";if($live -and $live.ExecutablePath -eq $target.ExecutablePath){Stop-Process -Id $target.ProcessId -Force -ErrorAction SilentlyContinue}}
