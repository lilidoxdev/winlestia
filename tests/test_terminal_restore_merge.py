"""Terminal installer: restore() must merge, never overwrite user edits (temp dirs and mocks only)."""
from pathlib import Path
import tempfile, shutil, os, sys, importlib.util, json, subprocess
from unittest.mock import patch

r = Path(__file__).resolve().parents[1]
PKG = 'Microsoft.WindowsTerminal_fixture'


def load(base, settings_data):
    terminal = base / 'Programs/Winlestia/Winlestia-Terminal'
    shutil.copytree(r / 'src/terminal', terminal)
    local = base / 'Local'
    settings = local / 'Packages' / PKG / 'LocalState/settings.json'
    settings.parent.mkdir(parents=True)
    settings.write_text(json.dumps(settings_data))
    spec = importlib.util.spec_from_file_location('install_terminal', terminal / 'install.py')
    module = importlib.util.module_from_spec(spec)
    with patch.dict(os.environ, {'LOCALAPPDATA': str(local)}):
        spec.loader.exec_module(module)
    module.STATE = local / 'Winlestia/terminal-backup'
    registry = {}

    def reg(path, name, value=None, remove=False):
        key = (path, name)
        if remove:
            registry.pop(key, None)
        elif value is not None:
            registry[key] = value
        else:
            return registry.get(key)

    module.registry = reg
    module.ensure_clink = lambda: False
    module.policy = lambda restore=False: None
    return module, terminal, local, settings, registry


def env(local, base):
    return (
        patch.dict(os.environ, {'LOCALAPPDATA': str(local)}),
        patch.object(Path, 'home', lambda: base),
        patch('winreg.OpenKey', side_effect=OSError),
        patch('subprocess.Popen'),
        patch('subprocess.run'),
        patch('shutil.which', return_value=None),
    )


def run(base, local, fn):
    from contextlib import ExitStack

    with ExitStack() as stack:
        for p in env(local, base):
            stack.enter_context(p)
        return fn()


def load_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


USER = {
    'profiles': {'defaults': {'font': {'face': 'Original'}}, 'list': [{'name': 'Mine', 'guid': '{1}'}]},
    'schemes': [{'name': 'UserScheme'}],
    'userKey': 'keep',
    'actions': [{'command': 'copy'}],
}

def test_restore_merges_user_edits_and_guards_autorun():
    with tempfile.TemporaryDirectory(prefix='winlestia-l3-') as tmp:
        base = Path(tmp)
        # (a) les modifications de l'utilisateur faites après l'installation survivent à la restauration ; seul ce qui
        # est encore à nous est rétabli.
        a = base / 'a'
        a.mkdir()
        module, terminal, local, settings, registry = load(a, USER)
        run(a, local, module.configure)
        state = load_json(module.STATE / 'state.json')
        assert state['version'] == 2 and PKG in state['wt']
        assert (
            state['wt'][PKG]['original']['theme'] == module.ABSENT
            and state['wt'][PKG]['original']['defaults']['opacity'] == module.ABSENT
        )
        data = load_json(settings)
        assert data['theme'] == 'Winlestia' and data['profiles']['defaults']['opacity'] == 65
        assert {s['name'] for s in data['schemes']} >= {'UserScheme', 'Winlestia Glass'}
        data['profiles']['list'].append({'name': 'New', 'guid': '{2}'})
        data['profiles']['defaults']['fontSize'] = 14
        data['profiles']['defaults']['opacity'] = 90
        settings.write_text(json.dumps(data))
        backup_before = (module.STATE / (PKG + '.json')).read_bytes()
        run(a, local, module.restore)
        data = load_json(settings)
        d = data['profiles']['defaults']
        assert [p['name'] for p in data['profiles']['list']] == ['Mine', 'New'], data
        assert d['fontSize'] == 14 and d['opacity'] == 90, d
        assert d['font'] == {'face': 'Original'} and 'colorScheme' not in d and 'useAcrylic' not in d, d
        assert [s['name'] for s in data['schemes']] == ['UserScheme'] and 'themes' not in data and 'theme' not in data, data
        assert data['userKey'] == 'keep' and data['actions'] == [{'command': 'copy'}]
        # sauvegarde conservée, jamais recopiée par-dessus
        assert (module.STATE / (PKG + '.json')).read_bytes() == backup_before
        assert not (module.STATE / 'state.json').exists() and (module.STATE / 'state.restored.json').exists()
        assert not any(k[1] == 'WinlestiaTerminalTheme' for k in registry) and (module.CMD, 'AutoRun') not in registry
        # seconde restauration (désinstallation après une mise à jour non cochée) : sans effet
        run(a, local, module.restore)
        assert load_json(settings) == data
        # re-install after restore captures fresh originals
        run(a, local, module.configure)
        assert load_json(module.STATE / 'state.json')['wt'][PKG]['original']['defaults']['font'] == {'face': 'Original'}
        # les valeurs de sync_theme enregistrées via note_written comptent comme les nôtres
        module.note_written(PKG, opacity=40)
        data = load_json(settings)
        data['profiles']['defaults']['opacity'] = 40
        settings.write_text(json.dumps(data))
        run(a, local, module.restore)
        # retour à la valeur trouvée à la seconde installation
        assert load_json(settings)['profiles']['defaults']['opacity'] == 90
        # une valeur utilisateur égale à ce que nous avons écrit est indiscernable et rétablie ; un thème changé par
        # l'utilisateur est conservé
        a2 = base / 'a2'
        a2.mkdir()
        module, terminal, local, settings, registry = load(a2, {'theme': 'dark'})
        run(a2, local, module.configure)
        data = load_json(settings)
        data['theme'] = 'mine'
        settings.write_text(json.dumps(data))
        run(a2, local, module.restore)
        data = load_json(settings)
        assert data['theme'] == 'mine' and 'profiles' not in data and 'schemes' not in data, data

        # (b) AutoRun / Run key guards.
        b = base / 'b'
        b.mkdir()
        module, terminal, local, settings, registry = load(b, USER)
        registry[(module.CMD, 'AutoRun')] = 'echo hi'
        run(b, local, module.configure)
        inj = module.injection()
        assert registry[(module.CMD, 'AutoRun')] == 'echo hi & ' + inj
        run(b, local, module.configure)
        assert registry[(module.CMD, 'AutoRun')] == 'echo hi & ' + inj  # idempotent
        registry[(module.CMD, 'AutoRun')] += ' & mycmd'
        run(b, local, module.restore)
        assert registry[(module.CMD, 'AutoRun')] == 'echo hi & mycmd', registry
        b2 = base / 'b2'
        b2.mkdir()
        module, terminal, local, settings, registry = load(b2, USER)
        run(b2, local, module.configure)
        registry[(module.CMD, 'AutoRun')] = 'mycmd & ' + registry[(module.CMD, 'AutoRun')]
        registry[(module.RUN, 'WinlestiaTerminalTheme')] = 'user-changed'
        run(b2, local, module.restore)
        assert (
            registry[(module.CMD, 'AutoRun')] == 'mycmd'
            and registry[(module.RUN, 'WinlestiaTerminalTheme')] == 'user-changed'
        ), registry
        b3 = base / 'b3'
        b3.mkdir()
        module, terminal, local, settings, registry = load(b3, USER)
        run(b3, local, module.configure)
        registry[(module.CMD, 'AutoRun')] = 'something else'
        run(b3, local, module.restore)
        assert registry[(module.CMD, 'AutoRun')] == 'something else'

        # une mise à jour ne doit pas capturer des ajouts ultérieurs de l'utilisateur comme 'written'.
        b4 = base / 'b4'
        b4.mkdir()
        module, terminal, local, settings, registry = load(b4, USER)
        registry[(module.CMD, 'AutoRun')] = 'echo hi'
        run(b4, local, module.configure)
        registry[(module.CMD, 'AutoRun')] += ' & mycmd'
        run(b4, local, module.configure)
        assert registry[(module.CMD, 'AutoRun')] == 'echo hi & ' + module.injection() + ' & mycmd', registry
        run(b4, local, module.restore)
        assert registry[(module.CMD, 'AutoRun')] == 'echo hi & mycmd', registry
        # note_written ne crée jamais state.json et tolère son absence.
        module.note_written(PKG, opacity=1)
        assert not (module.STATE / 'state.json').exists()

        # (c) un state.json v1 (sans manifeste) se restaure encore.
        c = base / 'c'
        c.mkdir()
        module, terminal, local, settings, registry = load(c, USER)
        registry[(module.CMD, 'AutoRun')] = 'echo hi'
        run(c, local, module.configure)
        v = load_json(module.STATE / 'state.json')
        module.save(module.STATE / 'state.json', json.dumps({k: v[k] for k in ('profiles', 'autorun', 'sync')}))
        data = load_json(settings)
        data['profiles']['defaults']['fontSize'] = 14
        data['profiles']['defaults']['opacity'] = 90
        settings.write_text(json.dumps(data))
        run(c, local, module.restore)
        data = load_json(settings)
        d = data['profiles']['defaults']
        assert (
            d == {'font': {'face': 'Original'}, 'fontSize': 14, 'opacity': 90, 'useAcrylic': True}
            and 'theme' not in data
            and [s['name'] for s in data['schemes']] == ['UserScheme']
        ), data
        assert registry[(module.CMD, 'AutoRun')] == 'echo hi' and not any(
            k[1] == 'WinlestiaTerminalTheme' for k in registry
        ), registry

        # état v1 : l'opacité changée par la synchro est conservée à la restauration ; configure migre vers v2 en
        # gardant les originaux v1.
        c2 = base / 'c2'
        c2.mkdir()
        module, terminal, local, settings, registry = load(c2, USER)
        run(c2, local, module.configure)
        v = load_json(module.STATE / 'state.json')
        module.save(module.STATE / 'state.json', json.dumps({k: v[k] for k in ('profiles', 'autorun', 'sync')}))
        run(c2, local, module.configure)
        s = load_json(module.STATE / 'state.json')
        assert s['version'] == 2 and s['wt'][PKG]['original']['defaults']['font'] == {'face': 'Original'}, s
        module.note_written(PKG, opacity=40)  # v2 now: fine
        c3 = base / 'c3'
        c3.mkdir()
        module, terminal, local, settings, registry = load(c3, USER)
        run(c3, local, module.configure)
        v = load_json(module.STATE / 'state.json')
        module.save(module.STATE / 'state.json', json.dumps({k: v[k] for k in ('profiles', 'autorun', 'sync')}))
        module.note_written(PKG, opacity=40)  # v1 : pas de KeyError, aucun changement
        assert 'version' not in load_json(module.STATE / 'state.json')

        # (d) pas de state.json : la restauration ne change rien.
        d_ = base / 'd'
        d_.mkdir()
        module, terminal, local, settings, registry = load(d_, USER)
        registry[(module.CMD, 'AutoRun')] = 'echo hi'
        before = settings.read_bytes()
        run(d_, local, module.restore)
        assert (
            settings.read_bytes() == before
            and registry == {(module.CMD, 'AutoRun'): 'echo hi'}
            and not (terminal / 'theme-sync.stop').exists()
        )

        # sync_theme ne doit pas toucher à Windows Terminal sans intégration (pas de state.json).
        sys.path.insert(0, str(r / 'src/terminal'))
        sys.path.insert(0, str(r / 'src/frontend'))
        import sync_theme

        e = base / 'e'
        e.mkdir()
        module, terminal, local, settings, registry = load(e, USER)
        before = settings.read_bytes()
        with (
            patch.dict(os.environ, {'LOCALAPPDATA': str(local)}),
            patch.object(sync_theme, 'STATE', module.STATE),
            patch.object(sync_theme, 'ROOT', terminal),
            patch.object(sync_theme, 'apply', side_effect=AssertionError('sync wrote')),
        ):
            sync_theme.run(True)
        assert settings.read_bytes() == before
