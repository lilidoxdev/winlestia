<#
  Capped launcher for Winlestia Fichiers tests. EVERY launch of the app in tests goes through this script.
  Safety: the process lives in a Job Object (ProcessMemoryLimit 800 MB, ActiveProcessLimit 4, KILL_ON_JOB_CLOSE), hard timeout <= 120 s,
  free RAM >= 4 GB required, <= 60 pipe requests spaced >= 20 ms, fixtures only under %TEMP%\WinlestiaFilesOps (removed with Remove-Item, never the Recycle Bin).
  Modes: any WINLESTIA_FILES_SELFTEST mode (ops, dnd, refresh, states, palette, ...), 'handler' (--register/--restore CLI on a sandbox registry root), 'tabs' (pipe opens), 'idle' (idle CPU over 10 s with a watched folder).
#>
param(
  [Parameter(Mandatory)][string]$Mode,
  [int]$Seconds = 40,
  [int]$Files = 30,
  [string]$Exe = '',
  [string]$ShotDir = '',
  [string]$PaletteFile = '',
  [string]$ShotName = '',
  [string]$PaletteShell = '',
  [switch]$NoWatch
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Exe) { $Exe = Join-Path $here '..\..\src\files\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Winlestia.Files.exe' }
if ($Seconds -gt 120) { $Seconds = 120 }
$free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
if ($free -lt 4) { Write-Output ("SKIP: free RAM {0:F1} GB < 4 GB" -f $free); return }

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class Job {
  [StructLayout(LayoutKind.Sequential)] struct BASIC { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinWs, MaxWs; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint Priority, Scheduling; }
  [StructLayout(LayoutKind.Sequential)] struct IO { public ulong a, b, c, d, e, f; }
  [StructLayout(LayoutKind.Sequential)] struct EXT { public BASIC Basic; public IO Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcess, PeakJob; }
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string name);
  [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, ref EXT info, int size);
  [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
  public static IntPtr Create() {
    var job = CreateJobObject(IntPtr.Zero, null);
    var info = new EXT();
    info.Basic.LimitFlags = 0x2000 | 0x100 | 0x8;                 // KILL_ON_JOB_CLOSE | PROCESS_MEMORY | ACTIVE_PROCESS
    info.Basic.ActiveProcessLimit = 4;
    info.ProcessMemoryLimit = (UIntPtr)(800UL * 1024 * 1024);
    if (!SetInformationJobObject(job, 9, ref info, Marshal.SizeOf(typeof(EXT)))) throw new Exception("SetInformationJobObject " + Marshal.GetLastWin32Error());
    return job;
  }
  public static void Assign(IntPtr job, IntPtr process) { if (!AssignProcessToJobObject(job, process)) throw new Exception("AssignProcessToJobObject " + Marshal.GetLastWin32Error()); }
}
'@

$root = Join-Path $env:TEMP 'WinlestiaFilesOps'
$fx = Join-Path $root ("run-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $fx -Force | Out-Null
1..$Files | ForEach-Object { Set-Content -Path (Join-Path $fx ("file{0:000}.txt" -f $_)) -Value "x" }
if ($Mode -eq 'ops') {
  Get-ChildItem $fx -File | Remove-Item; 1..60 | ForEach-Object { Set-Content -Path (Join-Path $fx ("file{0:000}.txt" -f $_)) -Value "x" }
  New-Item -ItemType Directory (Join-Path $fx 'big'), (Join-Path $fx 'ext') | Out-Null
  1..500 | ForEach-Object { Set-Content -Path (Join-Path (Join-Path $fx 'big') "b$_.txt") -Value "x" }
  'x1.txt', 'x2.txt', 'm1.txt' | ForEach-Object { Set-Content -Path (Join-Path (Join-Path $fx 'ext') $_) -Value 'x' }
}
if ($Mode -eq 'dnd') {
  Get-ChildItem $fx -File | Remove-Item; 1..12 | ForEach-Object { Set-Content -Path (Join-Path $fx ("file{0:000}.txt" -f $_)) -Value "x" }
  Set-Content (Join-Path $fx 'ctxtest.txt') 'x'; 'Cible', 'Autre', 'Third' | ForEach-Object { New-Item -ItemType Directory (Join-Path $fx $_) | Out-Null }
}
if ($Mode -eq 'tabs') { 1..60 | ForEach-Object { New-Item -ItemType Directory -Path (Join-Path $fx ("d{0:00}" -f $_)) | Out-Null } }

if ($Mode -eq 'handler') {
  # --register / --restore CLI of the exe against a SANDBOX root (WINLESTIA_FILES_HANDLER_ROOT under Software\WinlestiaTest\, no shell effect, own backup file in the fixture). Never the real HKCU\Software\Classes.
  $rel = 'Software\WinlestiaTest\Sandbox\files-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
  $bk = Join-Path $fx 'backup.json'
  $fake = Join-Path $fx 'file001.txt'
  $hk = [Microsoft.Win32.Registry]::CurrentUser
  function RunCli([string[]]$cliArgs) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
    $psi.UseShellExecute = $false
    $psi.Environment['WINLESTIA_FILES_HANDLER_ROOT'] = $rel
    $psi.Environment['WINLESTIA_FILES_HANDLER_BACKUP'] = $bk
    $psi.Arguments = ($cliArgs | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $j = [Job]::Create(); $q = [System.Diagnostics.Process]::Start($psi)
    try { [Job]::Assign($j, $q.Handle) } catch { }
    if (-not $q.WaitForExit(20000)) { try { $q.Kill() } catch { } ; [void][Job]::CloseHandle($j); return 'TIMEOUT' }
    $code = $q.ExitCode; [void][Job]::CloseHandle($j); return $code
  }
  function Val([string]$sub, [string]$name = '') { $k = $hk.OpenSubKey("$rel\Classes\$sub"); if ($k -eq $null) { return '<no key>' }; try { $v = $k.GetValue($name, '<no value>'); return $v } finally { $k.Dispose() } }
  function Chk([string]$name, [bool]$ok) { "{0} {1}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name }
  try {
    $k = $hk.CreateSubKey("$rel\Classes\Directory\shell"); $k.SetValue('', 'none'); $k.Dispose()
    $k = $hk.CreateSubKey("$rel\Classes\Directory\shell\foo\command"); $k.SetValue('', 'foo.exe'); $k.Dispose()
    $c1 = RunCli @('--register', $fake)
    Chk 'register exit 0' ($c1 -eq 0)
    Chk 'Directory default = winlestia' ((Val 'Directory\shell') -eq 'winlestia')
    Chk 'Drive default = winlestia' ((Val 'Drive\shell') -eq 'winlestia')
    Chk 'command written' ((Val 'Directory\shell\winlestia\command') -eq ('"' + $fake + '" "%1"'))
    Chk 'Folder untouched' ((Val 'Folder') -eq '<no key>')
    Chk 'backup file written' (Test-Path $bk)
    $c2 = RunCli @('--restore')
    Chk 'restore exit 0' ($c2 -eq 0)
    Chk 'Directory default back to none' ((Val 'Directory\shell') -eq 'none')
    Chk 'our subkey gone' ((Val 'Directory\shell\winlestia') -eq '<no key>')
    Chk 'foreign verb kept' ((Val 'Directory\shell\foo\command') -eq 'foo.exe')
    Chk 'Drive keys gone' ((Val 'Drive') -eq '<no key>')
    Chk 'backup deleted' (-not (Test-Path $bk))
    Chk 'restore again exit 0 (idempotent)' ((RunCli @('--restore')) -eq 0)
  } finally {
    try { $hk.DeleteSubKeyTree($rel, $false) } catch { }
    foreach ($parent in 'Software\WinlestiaTest\Sandbox', 'Software\WinlestiaTest') { try { $pk = $hk.OpenSubKey($parent); if ($pk -ne $null) { $empty = $pk.SubKeyCount -eq 0 -and $pk.ValueCount -eq 0; $pk.Dispose(); if ($empty) { $hk.DeleteSubKey($parent, $false) } } } catch { } }
    Chk 'sandbox deleted' ($hk.OpenSubKey($rel) -eq $null)
    Remove-Item -LiteralPath $fx -Recurse -Force -ErrorAction SilentlyContinue
  }
  return
}
$logDir = Join-Path $env:LOCALAPPDATA 'Winlestia\Files'
$selfLog = Join-Path $logDir 'selftest.log'
$before = if (Test-Path $selfLog) { @(Get-Content $selfLog).Count } else { 0 }
$tabLog = Join-Path $root ('tablog-' + (Split-Path $fx -Leaf) + '.txt')
$pipe = 'harness' + (Get-Random -Maximum 99999)

$psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
$psi.UseShellExecute = $false
$psi.Environment['WINLESTIA_FILES_PIPE'] = $pipe
$psi.Environment['WINLESTIA_FILES_OPS_NOBIN'] = '1'
$psi.Environment['WINLESTIA_FILES_TABLOG'] = $tabLog
if ($NoWatch) { $psi.Environment['WINLESTIA_FILES_NOWATCH'] = '1' }
if ($PaletteFile) { $psi.Environment['WINLESTIA_FILES_PALETTE_FILE'] = $PaletteFile }
if ($PaletteShell) { $psi.Environment['WINLESTIA_FILES_PALETTE_SHELL'] = $PaletteShell }
if ($ShotName) { $psi.Environment['WINLESTIA_FILES_SHOTNAME'] = $ShotName }
if ($ShotDir) { $psi.Environment['WINLESTIA_FILES_SHOTDIR'] = $ShotDir }
if ($Mode -notin 'tabs', 'idle') { $psi.Environment['WINLESTIA_FILES_SELFTEST'] = $Mode }
if ($Mode -in 'refresh', 'states', 'ops', 'dnd') { $psi.Environment['WINLESTIA_FILES_PERF'] = '1' }
$psi.Arguments = $(if ($Mode -eq 'tabs') { '--resident ' } else { '' }) + '"' + $fx + '"'

$job = [Job]::Create()
$p = [System.Diagnostics.Process]::Start($psi)
[Job]::Assign($job, $p.Handle)
$sw = [Diagnostics.Stopwatch]::StartNew()
$peakMb = 0.0; $peakThreads = 0
function Sample { try { $p.Refresh(); $script:peakMb = [Math]::Max($script:peakMb, $p.PrivateMemorySize64 / 1MB); $script:peakThreads = [Math]::Max($script:peakThreads, $p.Threads.Count) } catch { } }

function Send([string]$path, [string]$cmd = $null) {
  $c = New-Object System.IO.Pipes.NamedPipeClientStream '.', "Winlestia.Files.test.$pipe", ([System.IO.Pipes.PipeDirection]::InOut), ([System.IO.Pipes.PipeOptions]::None)
  try {
    $c.Connect(300)
    $body = if ($cmd) { @{ cmd = $cmd; args = @() } } else { @{ args = @($path); cwd = $fx } }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Compress) + "`n")
    $head = New-Object byte[] 4; [void]$c.Read($head, 0, 4)
    $c.Write($bytes, 0, $bytes.Length); $c.Flush()
    $ack = New-Object byte[] 1; [void]$c.Read($ack, 0, 1)
  } catch { Write-Output "send failed: $($_.Exception.Message)" } finally { $c.Dispose() }
}
function Tabs { if (Test-Path $tabLog) { try { (Get-Content $tabLog -Raw).Trim() } catch { '?' } } else { 'no log' } }

try {
  if ($Mode -eq 'tabs') {
    for ($i = 0; $i -lt 100 -and -not (Test-Path "\\.\pipe\Winlestia.Files.test.$pipe"); $i++) { Start-Sleep -Milliseconds 100 }
    Start-Sleep -Milliseconds 1500
    "start: $(Tabs)"
    1..50 | ForEach-Object { Send (Join-Path $fx ("d{0:00}" -f $_)); Start-Sleep -Milliseconds 25; Sample }
    Start-Sleep -Seconds 3; Sample; "after 50 opens: $(Tabs)"
    51..60 | ForEach-Object { Send (Join-Path $fx ("d{0:00}" -f $_)); Start-Sleep -Milliseconds 25; Sample }
    Start-Sleep -Seconds 2; Sample; "after 10 more: $(Tabs)"
    Send (Join-Path $fx 'd05'); Start-Sleep -Seconds 1; "after same-path: $(Tabs)"
    Send '' 'quit'; Start-Sleep -Milliseconds 500
  } elseif ($Mode -eq 'idle') {
    Start-Sleep -Seconds 5; $p.Refresh(); $c0 = $p.TotalProcessorTime.TotalMilliseconds; Sample
    Start-Sleep -Seconds 10; $p.Refresh(); $c1 = $p.TotalProcessorTime.TotalMilliseconds; Sample
    "idle CPU over 10 s: {0:F0} ms ({1:F2} %)" -f ($c1 - $c0), (($c1 - $c0) / 100)
  } else {
    while (-not $p.HasExited -and $sw.Elapsed.TotalSeconds -lt $Seconds) { Sample; Start-Sleep -Milliseconds 250 }
    if (-not $p.HasExited) { "TIMEOUT after $Seconds s (job closed)" }
  }
} finally {
  Sample
  [void][Job]::CloseHandle($job)                      # KILL_ON_JOB_CLOSE: nothing survives the harness
  Start-Sleep -Milliseconds 300
  Remove-Item -LiteralPath $fx -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $tabLog -Force -ErrorAction SilentlyContinue
}
"peak private {0:F0} MB, peak threads {1}, exit {2}, {3:F1}s" -f $peakMb, $peakThreads, $(if ($p.HasExited) { $p.ExitCode } else { 'killed' }), $sw.Elapsed.TotalSeconds
if (Test-Path $selfLog) { Get-Content $selfLog | Select-Object -Skip $before }
