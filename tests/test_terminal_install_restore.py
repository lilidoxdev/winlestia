"""Installateur du terminal : chemins portables, AutoRun CMD, profils PowerShell, acrylique,
reinstallation idempotente et restauration (ni registre ni profils reels touches)."""
from pathlib import Path
import tempfile, shutil, os, sys, importlib.util, json, subprocess

real_run = subprocess.run
real_popen = subprocess.Popen
from unittest.mock import patch


def test_install_idempotent_and_restore():
    r = Path(__file__).resolve().parents[1]
    with tempfile.TemporaryDirectory(prefix='winlestia-terminal-test-') as tmp:
        base = Path(tmp)
        terminal = base / 'Programs/Winlestia/Winlestia-Terminal'
        shutil.copytree(r / 'src/terminal', terminal)
        local = base / 'Local'
        settings = local / 'Packages/Microsoft.WindowsTerminal_fixture/LocalState/settings.json'
        settings.parent.mkdir(parents=True)
        settings.write_text(
            json.dumps(
                {
                    'profiles': {'defaults': {'font': {'face': 'Original'}}},
                    'schemes': [],
                    'themes': [],
                    'actions': [{'command': 'copy'}],
                }
            )
        )
        spec = importlib.util.spec_from_file_location('install_terminal', terminal / 'install.py')
        module = importlib.util.module_from_spec(spec)
        with patch.dict(os.environ, {'LOCALAPPDATA': str(local)}):
            spec.loader.exec_module(module)
        module.STATE = local / 'Winlestia/terminal-backup'
        registry = {}
        documents = base / 'Documents'
        module.Path.home = lambda: base

        def reg(path, name, value=None, remove=False):
            key = (path, name)
            if remove:
                registry.pop(key, None)
            elif value is not None:
                registry[key] = value
            else:
                return registry.get(key)

        module.registry = reg
        module.ensure_clink = lambda: False  # Clink est téléchargé à l'installation ; ce test reste hors ligne.
        with (
            patch.dict(os.environ, {'LOCALAPPDATA': str(local)}),
            patch.object(module.winreg, 'OpenKey', side_effect=OSError),
            patch.object(module.subprocess, 'Popen'),
            patch.object(module.subprocess, 'run'),
            patch.object(module.shutil, 'which', return_value=None),
        ):
            module.configure()
            module.configure()
            jsonc = base / 'settings-jsonc.json'
            jsonc.write_text('{// comment\n"url":"https://example.com/a//b", "items":[1,2,],}')
            assert module.read_settings(jsonc) == {'url': 'https://example.com/a//b', 'items': [1, 2]}, (
                'JSONC: comments and trailing commas stripped, // inside strings kept'
            )
            powershell = Path(os.environ['SystemRoot']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'
            with patch.object(subprocess, 'Popen', real_popen):
                output = real_run(
                    [
                        str(powershell),
                        '-NoLogo',
                        '-NoProfile',
                        '-ExecutionPolicy',
                        'Bypass',
                        '-File',
                        str(terminal / 'Banner.ps1'),
                        '-Shell',
                        'CMD',
                    ],
                    env={**os.environ, 'PSExecutionPolicyPreference': 'Restricted'},
                    capture_output=True,
                    timeout=20,
                    creationflags=0x08000000,
                )
            assert output.returncode == 0 and b'PSSecurityException' not in output.stderr, (
                f'Banner.ps1 must run under Restricted policy: {output.stderr!r}'
            )

            p = documents / 'WindowsPowerShell/profile.ps1'
            data = json.loads(settings.read_text(encoding='utf-8-sig'))
            assert p.read_text(encoding='utf-8-sig').count('# >>> Winlestia Terminal >>>') == 1, (
                'PowerShell profile block must be present exactly once after two configure() calls'
            )
            assert data['profiles']['defaults']['opacity'] == 65 and data['actions'] == [{'command': 'copy'}], (
                'opacity 65 set and user actions preserved'
            )
            assert '__WINLESTIA' not in (terminal / 'Profile.ps1').read_text(encoding='utf-8-sig'), (
                'Profile.ps1 placeholders must be substituted'
            )
            assert not (terminal / 'clink-profile/clink_start.cmd').read_bytes().startswith(b'\xef\xbb\xbf'), (
                'clink_start.cmd must be written without BOM'
            )
            assert 'inject --quiet' in registry[(module.CMD, 'AutoRun')], 'CMD AutoRun must inject clink'
            module.restore()
            assert 'Winlestia Terminal >>>' not in p.read_text(encoding='utf-8-sig'), 'restore() must remove the PowerShell profile block'
            assert (
                json.loads(settings.read_text(encoding='utf-8-sig'))['profiles']['defaults']['font']['face'] == 'Original'
        ), 'restore() must bring back the original font face'
