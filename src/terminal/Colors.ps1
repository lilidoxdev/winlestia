# PowerShell 5.1+ / Couleurs Winlestia en direct, mises en cache, partagées avec CMD et Windows Terminal.
function global:Get-WinlestiaAnsi {
    param([string]$Name)
    # Les couleurs ANSI indexées permettent à Windows Terminal de recolorer les cellules existantes lors d'un changement de palette.
    $index=@{accent=34;secondary=36;muted=90;foreground=37}[$Name]
    if($index -and $env:WT_SESSION){return "$([char]27)[${index}m"}
    $hex=$global:WinlestiaPalette.$Name
    if(!$hex){$hex='#c4bafa'}
    $r=[Convert]::ToInt32($hex.Substring(1,2),16)
    $g=[Convert]::ToInt32($hex.Substring(3,2),16)
    $b=[Convert]::ToInt32($hex.Substring(5,2),16)
    "$([char]27)[38;2;$r;$g;${b}m"
}
function global:Update-WinlestiaPalette {
    param([switch]$Force,[switch]$SkipReadLine)
    $path='__WINLESTIA_TERMINAL__\palette.json'
    try {
        $stamp=[IO.File]::GetLastWriteTimeUtc($path)
        if(!$Force -and $stamp -eq $global:WinlestiaPaletteStamp){return}
        $global:WinlestiaPalette=[IO.File]::ReadAllText($path) | ConvertFrom-Json
        $global:WinlestiaPaletteStamp=$stamp
        if(!$SkipReadLine -and (Get-Module PSReadLine)){
            Set-PSReadLineOption -Colors @{
                Command=(Get-WinlestiaAnsi accent);Keyword=(Get-WinlestiaAnsi secondary)
                Operator=(Get-WinlestiaAnsi accent);Type=(Get-WinlestiaAnsi secondary)
                Comment=(Get-WinlestiaAnsi muted)
            }
        }
    }catch{<# palette absente ou illisible : on garde les couleurs courantes #>}
}
