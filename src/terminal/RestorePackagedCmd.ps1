# Invoked in Windows Terminal's packaged context by Restore.ps1.
$root=$PSScriptRoot
$backup=Get-Content -LiteralPath (Join-Path $root 'backups\packaged-autorun-before.json') -Raw | ConvertFrom-Json
$key='HKCU:\Software\Microsoft\Command Processor'
# Only undo our own value: an AutoRun changed since the install is left alone.
$written=$null;$file=Join-Path $root 'validation\packaged-autorun.json'
if(Test-Path -LiteralPath $file){$written=(Get-Content -LiteralPath $file -Raw | ConvertFrom-Json).AutoRun}
$current=[string](Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue).AutoRun
$inj='"'+$root+'\clink\clink_x64.exe" inject --quiet --profile "'+$root+'\clink-profile"'
if(-not $written -and $current.Contains($inj)){
    # No record of what we wrote: remove only our own segment.
    $rest=$current.Replace(' & '+$inj,'').Replace($inj+' & ','').Replace($inj,'').Trim()
    if($rest){Set-ItemProperty -LiteralPath $key -Name AutoRun -Value $rest}
    else{Remove-ItemProperty -LiteralPath $key -Name AutoRun -ErrorAction SilentlyContinue}
}
elseif($written -and $current -eq $written){
    if($backup.Exists){Set-ItemProperty -LiteralPath $key -Name AutoRun -Value $backup.Value}
    else{Remove-ItemProperty -LiteralPath $key -Name AutoRun -ErrorAction SilentlyContinue}
}
[IO.File]::WriteAllText((Join-Path $root 'validation\package-restored.flag'),'restored')
