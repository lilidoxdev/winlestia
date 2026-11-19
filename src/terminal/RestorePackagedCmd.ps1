# Invoqué dans le contexte empaqueté de Windows Terminal par Restore.ps1.
$root=$PSScriptRoot
$backup=Get-Content -LiteralPath (Join-Path $root 'backups\packaged-autorun-before.json') -Raw | ConvertFrom-Json
$key='HKCU:\Software\Microsoft\Command Processor'
# Annuler uniquement notre propre valeur : un AutoRun modifié depuis l'installation est laissé tel quel.
$written=$null;$file=Join-Path $root 'validation\packaged-autorun.json'
if(Test-Path -LiteralPath $file){$written=(Get-Content -LiteralPath $file -Raw | ConvertFrom-Json).AutoRun}
$current=[string](Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue).AutoRun
$inj='"'+$root+'\clink\clink_x64.exe" inject --quiet --profile "'+$root+'\clink-profile"'
if(-not $written -and $current.Contains($inj)){
    # Aucune trace de ce que nous avons écrit : on retire uniquement notre propre segment.
    $rest=$current.Replace(' & '+$inj,'').Replace($inj+' & ','').Replace($inj,'').Trim()
    if($rest){Set-ItemProperty -LiteralPath $key -Name AutoRun -Value $rest}
    else{Remove-ItemProperty -LiteralPath $key -Name AutoRun -ErrorAction SilentlyContinue}
}
elseif($written -and $current -eq $written){
    if($backup.Exists){Set-ItemProperty -LiteralPath $key -Name AutoRun -Value $backup.Value}
    else{Remove-ItemProperty -LiteralPath $key -Name AutoRun -ErrorAction SilentlyContinue}
}
[IO.File]::WriteAllText((Join-Path $root 'validation\package-restored.flag'),'restored')
