# Python3.12 / lifecycle065.py / isolated dummy processes; no running Winlestia instance is modified.
from pathlib import Path
import tempfile,subprocess,os,shutil,json
root=Path(__file__).resolve().parents[1];shell=Path(os.environ['SystemRoot'])/'System32/WindowsPowerShell/v1.0/powershell.exe'
with tempfile.TemporaryDirectory(prefix='winlestia-cleanup-') as folder:
 base=Path(folder);owned=base/'Winlestia';other=base/'Winlestia-other'
 for path in (owned,other):path.mkdir();shutil.copy2(Path(os.environ['SystemRoot'])/'System32/cmd.exe',path/'python.exe')
 processes=[subprocess.Popen([str(path/'python.exe'),'/d','/c','ping -n 20 127.0.0.1 >nul & rem frontend.py'],creationflags=0x08000000) for path in (owned,other)]
 try:
  result=subprocess.run([str(shell),'-NoProfile','-ExecutionPolicy','Bypass','-File',str(root/'installer/Close-Winlestia.ps1'),'-AppRoot',str(owned)],capture_output=True,timeout=15)
  assert result.returncode==0,result.stderr
  assert processes[0].poll() is not None,'Owned orphan not stopped'
  assert processes[1].poll() is None,'Sibling process was affected'
  print('Scoped installer cleanup: owned orphan stopped; sibling untouched')
 finally:
  for process in processes:
   if process.poll() is None:process.terminate()
   process.wait(timeout=5)
