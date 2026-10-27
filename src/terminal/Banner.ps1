# PowerShell 5.1+ / Winlestia session banner / Windows 11.
param([string]$Shell = '', [switch]$Force)
. (Join-Path $PSScriptRoot 'Colors.ps1')
Update-WinlestiaPalette -SkipReadLine
function Show-WinlestiaBanner {
    param([string]$ShellName)
    $esc=[char]27; $reset="$esc[0m"; $accent=Get-WinlestiaAnsi accent; $cyan=Get-WinlestiaAnsi secondary; $muted=Get-WinlestiaAnsi muted; $text=Get-WinlestiaAnsi foreground
    if(!$ShellName){$ShellName="PowerShell $($PSVersionTable.PSVersion.ToString())"}
    $admin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $session=if($admin){'Administrateur'}else{'Standard'}
    $osName='Windows';$build=[Environment]::OSVersion.Version.Build;$memory='';$uptime=''
    try {
        $os=Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $osName=$os.Caption -replace '^Microsoft ',''
        $memory='{0:N1} / {1:N1} Gio' -f (($os.TotalVisibleMemorySize-$os.FreePhysicalMemory)/1MB),($os.TotalVisibleMemorySize/1MB)
        $span=(Get-Date)-$os.LastBootUpTime
        $uptime=if($span.Days){"$($span.Days) j $($span.Hours) h"}else{"$($span.Hours) h $($span.Minutes) min"}
    }catch{}
    $logo=@(
        ' _       ___       __          __  _',
        '| |     / (_)___  / /__  _____/ /_(_)___ _',
        '| | /| / / / __ \/ / _ \/ ___/ __/ / __ `/',
        '| |/ |/ / / / / / /  __(__  ) /_/ / /_/ /',
        '|__/|__/_/_/ /_/_/\___/____/\__/_/\__,_/'
    )
    Write-Host ''
    foreach($line in $logo){Write-Host "  $accent$line$reset"}
    Write-Host "`n  ${muted}WINDOWS / WINLESTIA  ${cyan}·  espace de commande$reset`n"
    $rows=@(@('système',"$osName · $build"),@('session',"$env:USERNAME · $session"),@('machine',$env:COMPUTERNAME),@('shell',$ShellName))
    if($memory){$rows+=,@('mémoire',$memory)}
    if($uptime){$rows+=,@('activité',$uptime)}
    $width=59
    try{$width=[Math]::Min(68,[Math]::Max(35,$Host.UI.RawUI.WindowSize.Width-7))}catch{}
    Write-Host ('  '+$muted+'╭'+('─'*$width)+'╮'+$reset)
    foreach($row in $rows){
        $value=[string]$row[1];$limit=$width-17;if($value.Length -gt $limit){$value=$value.Substring(0,$limit-1)+'…'}
        $spaces=' '*[Math]::Max(1,$width-15-$value.Length)
        Write-Host ('  '+$muted+'│  '+$text+([string]$row[0]).PadRight(11)+$muted+'  '+$cyan+$value+$spaces+$muted+'│'+$reset)
    }
    Write-Host ('  '+$muted+'╰'+('─'*$width)+'╯'+$reset)
    Write-Host ''
}
if($Shell -or $Force){Show-WinlestiaBanner -ShellName $Shell}
