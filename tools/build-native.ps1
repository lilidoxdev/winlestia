# Compile les composants natifs (MSVC) dans src\backend\native. Source unique pour build.ps1 et la CI.
# Reutilise src\backend\build-*.cmd. Sans cl.exe dans le PATH, charge l'environnement via vswhere.
$ErrorActionPreference = 'Stop'
$backend = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\backend'
$native  = Join-Path $backend 'native'
New-Item -ItemType Directory -Force -Path $native | Out-Null

if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = if (Test-Path $vswhere) { & $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
    if ($vs) {
        $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
        # A temporary .cmd avoids quoting differences between Windows PowerShell and pwsh 7.
        $probe = Join-Path ([IO.Path]::GetTempPath()) "vcvars-$PID.cmd"
        Set-Content -Path $probe -Encoding ascii -Value "@call `"$vcvars`" >nul`r`n@set"
        try {
            & $probe | ForEach-Object {
                if ($_ -match '^([^=]+)=(.*)$') { Set-Item -Path "env:$($Matches[1])" -Value $Matches[2] }
            }
        } finally { Remove-Item $probe -ErrorAction SilentlyContinue }
    }
}

foreach ($s in 'build-backdrop','build-process-audio','build-rescue','build-taskbar-rescue') {
    Write-Host "-> $s" -ForegroundColor Cyan
    & (Join-Path $backend "$s.cmd")
    if ($LASTEXITCODE -ne 0) { throw "Native build failed: $s" }
}
foreach ($f in 'GlassBackdrop.dll','SECOURS_BUREAU.exe','RETABLIR_BARRE_WINDOWS.exe') {
    $src = Join-Path $backend $f
    if (Test-Path $src) { Move-Item $src $native -Force }
}
foreach ($f in 'GlassBackdrop.dll','ProcessAudio.dll','SECOURS_BUREAU.exe','RETABLIR_BARRE_WINDOWS.exe') {
    if (-not (Test-Path (Join-Path $native $f))) { throw "Composant natif manquant apres compilation : $f" }
}
