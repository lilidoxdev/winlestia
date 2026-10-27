# PowerShell 5.1+ / Configure-Policy.ps1 / Per-user interactive profile support with policy restoration.
param([Parameter(Mandatory=$true)][string]$StatePath,[switch]$Restore,[switch]$InspectOnly)
$ErrorActionPreference='Stop'
$current=[string](Get-ExecutionPolicy -Scope CurrentUser)
$machine=[string](Get-ExecutionPolicy -Scope MachinePolicy)
$user=[string](Get-ExecutionPolicy -Scope UserPolicy)
$managed=if($machine -ne 'Undefined'){$machine}else{$user}
$blocked=$managed -in @('Restricted','AllSigned')
if($InspectOnly){[pscustomobject]@{CurrentUser=$current;Managed=$managed;Blocked=$blocked}|ConvertTo-Json -Compress;return}
function Set-UserPolicy([string]$Value){
 try {Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy $Value -Force}
 catch {if([string](Get-ExecutionPolicy -Scope CurrentUser) -ne $Value){throw}}
 if([string](Get-ExecutionPolicy -Scope CurrentUser) -ne $Value){throw 'La politique du compte n a pas ete appliquee.'}
}
if($Restore){
 if(Test-Path -LiteralPath $StatePath){$saved=Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json;if($current -eq 'RemoteSigned' -and $saved.Changed){Set-UserPolicy $saved.Before}}
 return
}
if($blocked){throw 'Une strategie administrateur bloque les profils non signes. Le theme PowerShell ne peut pas etre active sur ce poste.'}
if(!(Test-Path -LiteralPath $StatePath)){
 $changed=$current -notin @('RemoteSigned','Unrestricted','Bypass')
 [pscustomobject]@{Before=$current;Changed=$changed}|ConvertTo-Json|Set-Content -LiteralPath $StatePath -Encoding UTF8
}
if($current -notin @('RemoteSigned','Unrestricted','Bypass')){Set-UserPolicy RemoteSigned}
