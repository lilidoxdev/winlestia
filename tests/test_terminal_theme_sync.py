"""Regression checks for the 0.6.5 installer failure: execution-policy override/restore, red/blue palette, fresh Terminal settings."""
from pathlib import Path
import os, sys, subprocess, tempfile, shutil, importlib.util, json

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / 'src/terminal'))
sys.path.insert(0, str(root / 'src/frontend'))
import sync_theme


def test_policy_override_restore_and_palette(monkeypatch):
    with tempfile.TemporaryDirectory(prefix='winlestia-0651-') as folder:
        base = Path(folder)
        terminal = base / 'Winlestia-Terminal'
        terminal.mkdir()
        shutil.copy2(root / 'src/terminal/theme-template.json', terminal / 'theme-template.json')
        (terminal / 'clink-profile').mkdir()
        local = base / 'Local'
        settings = local / 'Packages/Microsoft.WindowsTerminal_fixture/LocalState/settings.json'
        settings.parent.mkdir(parents=True)
        settings.write_text('{// fresh Terminal\n"profiles":{"list":[]},}')
        monkeypatch.setattr(sync_theme, 'ROOT', terminal)
        monkeypatch.setenv('LOCALAPPDATA', str(local))
        # STATE est calculé à l'import sur le vrai LOCALAPPDATA : on l'isole, sinon le test lit et réécrit l'état réel.
        state = base / 'terminal-backup'
        state.mkdir()
        (state / 'state.json').write_text('{}')
        monkeypatch.setattr(sync_theme, 'STATE', state)
        monkeypatch.setattr(sys.modules['install'], 'STATE', state)
        for color in ('#ff315b', '#317bff'):
            values = {
                'accent': color,
                'secondary': '#eeeeee',
                'muted': '#888888',
                'foreground': '#eeeeee',
                'background': '#101518',
                'source': 'fixture',
            }
            sync_theme.apply(values)
            assert json.loads((terminal / 'palette.json').read_text())['accent'] == color, f'palette.json accent must be {color}'
            data = json.loads(settings.read_text(encoding='utf-8-sig'))
            glass = next(s for s in data['schemes'] if s['name'] == 'Winlestia Glass')
            assert glass['blue'] == color, f'Winlestia Glass scheme blue must follow accent {color}'
        assert data['profiles']['defaults']['useAcrylic'], 'acrylic must be enabled in fresh settings'
        helper = root / 'src/terminal/Configure-Policy.ps1'
        state = base / 'policy.json'
        script = base / 'policy-test.ps1'
        script.write_text(
            "$script:policy='Undefined'\n"
            "function Get-ExecutionPolicy {param($Scope) if($Scope -eq 'CurrentUser'){$script:policy}else{'Undefined'}}\n"
            "function Set-ExecutionPolicy {param($Scope,$ExecutionPolicy,[switch]$Force) "
            "$script:policy=[string]$ExecutionPolicy;throw 'ExecutionPolicyOverride'}\n. '"
            + str(helper).replace("'", "''")
            + "' -StatePath '"
            + str(state)
            + "'\nif($script:policy -ne 'RemoteSigned'){throw 'Not applied'}\n. '"
            + str(helper).replace("'", "''")
            + "' -StatePath '"
            + str(state)
            + "' -Restore\nif($script:policy -ne 'Undefined'){throw 'Not restored'}\n",
            encoding='utf-8-sig',
        )
        shell = Path(os.environ['SystemRoot']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'
        result = subprocess.run(
            [str(shell), '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(script)], capture_output=True, timeout=15
        )
        assert result.returncode == 0, f'execution policy not applied/restored: {result.stderr!r}'
