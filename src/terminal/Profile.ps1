# PowerShell 5.1+ / Winlestia interactive theme. No output in scripts or automation.
if($env:WINLESTIA_NO_THEME -eq '1'){return}
$argsText=[Environment]::GetCommandLineArgs() -join ' '
$batch=$argsText -match '(?i)(?:^|\s)-(?:NonInteractive|NonI|Command|C|EncodedCommand|Enc|File|F)(?:\s|$)'
$stay=$argsText -match '(?i)(?:^|\s)-NoExit(?:\s|$)'
if($Host.Name -ne 'ConsoleHost' -or ($batch -and !$stay) -or [Console]::IsOutputRedirected){return}
if($global:WinlestiaLoaded){return}
$global:WinlestiaLoaded=$true
$global:WinlestiaAdmin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
try {
    Import-Module PSReadLine -MinimumVersion 2.0 -ErrorAction Stop
    $e=[char]27
    Set-PSReadLineOption -BellStyle None -EditMode Windows -Colors @{
        Command="$e[38;2;140;215;241m"; Keyword="$e[38;2;196;186;250m"; String="$e[38;2;167;220;178m"
        Variable="$e[38;2;244;187;218m"; Parameter="$e[38;2;242;211;151m"; Number="$e[38;2;242;177;140m"
        Type="$e[38;2;151;210;217m"; Operator="$e[38;2;196;186;250m"; Comment="$e[38;2;117;131;156m"
        Error="$e[38;2;243;139;157m"; Default="$e[38;2;226;232;244m"
    }
    if((Get-Command Set-PSReadLineOption).Parameters.ContainsKey('PredictionSource')){Set-PSReadLineOption -PredictionSource History -PredictionViewStyle InlineView -Colors @{InlinePrediction="$e[38;2;99;112;139m"}}
    Set-PSReadLineKeyHandler -Key Tab -Function MenuComplete
    Set-PSReadLineKeyHandler -Key UpArrow -Function HistorySearchBackward
    Set-PSReadLineKeyHandler -Key DownArrow -Function HistorySearchForward
}catch{}
. (Join-Path $PSScriptRoot 'Colors.ps1')
Update-WinlestiaPalette -Force
function global:prompt {
    $success=$?;if(!$global:WinlestiaWelcomeShown){$global:WinlestiaWelcomeShown=$true;Clear-Host;Show-WinlestiaBanner};Update-WinlestiaPalette;$e=[char]27;$reset="$e[0m";$accent=Get-WinlestiaAnsi accent;$cyan=Get-WinlestiaAnsi secondary;$muted=Get-WinlestiaAnsi muted
    $location=$ExecutionContext.SessionState.Path.CurrentLocation.Path
    if($location.StartsWith($HOME,[StringComparison]::OrdinalIgnoreCase)){$location='~'+$location.Substring($HOME.Length)}
    $badge=if($global:WinlestiaAdmin){" $e[38;2;243;139;157mADMIN$reset"}else{''}
    $symbol=if($success){"$cyan❯"}else{"$e[38;2;243;139;157m❯"}
    $clock=Get-Date -Format HH:mm
    "${muted}╭─ ${accent}$location${badge}  ${muted}·  $clock$reset`n${muted}╰─ $symbol$reset "
}
try {$Host.UI.RawUI.WindowTitle='Winlestia · PowerShell'+$(if($global:WinlestiaAdmin){' · Administrateur'})}catch{}
. '__WINLESTIA_TERMINAL__\Banner.ps1'
