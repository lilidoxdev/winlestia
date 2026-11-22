"""Visite guidée : état persistant (tutorial.json) et tampon d'installation, dans un dossier temporaire."""
import json
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src' / 'frontend'))
import tutorial  # noqa: E402


@pytest.fixture
def dirs(tmp_path, monkeypatch):
    monkeypatch.delenv('WINLESTIA_INSTALL_STAMP', raising=False)
    config = tmp_path / 'data' / 'Caelestia'
    config.mkdir(parents=True)
    root = tmp_path / 'install' / 'Caelestia-Windows'  # le tampon est au parent : <racine>\install-stamp.txt
    root.mkdir(parents=True)
    return config, root


def stamp(root, text):
    (root.parent / 'install-stamp.txt').write_text(text, encoding='utf-8')


def test_missing_file_is_pending(dirs):
    config, root = dirs
    assert tutorial.is_pending(config, root)


def test_upgrader_without_file_is_pending(dirs):
    config, root = dirs
    stamp(root, 'AAAA 2026-01-01T00:00:00Z\n')
    assert tutorial.is_pending(config, root)


def test_seen_with_new_stamp_not_pending(dirs):
    config, root = dirs
    stamp(root, 'AAAA 2026-01-01T00:00:00Z\n')
    tutorial.save_state(config, root, False)
    stamp(root, 'BBBB 2026-02-01T00:00:00Z\n')
    assert not tutorial.is_pending(config, root)


def test_no_stamp_relies_on_file(dirs):
    config, root = dirs
    tutorial.save_state(config, root, True)
    assert tutorial.read_stamp(root) is None
    assert not tutorial.is_pending(config, root)


def test_finish_writes_file(dirs):
    config, root = dirs
    stamp(root, 'AAAA 2026-01-01T00:00:00Z\n')
    tutorial.save_state(config, root, False)
    data = json.loads((config / 'tutorial.json').read_text(encoding='utf-8'))
    assert data == {'seenStamp': 'AAAA 2026-01-01T00:00:00Z', 'done': False}
    tutorial.save_state(config, root, True)
    assert json.loads((config / 'tutorial.json').read_text(encoding='utf-8'))['done'] is True


@pytest.mark.parametrize('content', ['{not json', '[]', '"x"', '', '\x00\x01'])
def test_corrupt_json_is_pending(dirs, content):
    config, root = dirs
    (config / 'tutorial.json').write_text(content, encoding='utf-8')
    assert tutorial.load_state(config) is None
    assert tutorial.is_pending(config, root)


def test_wrong_types_do_not_crash(dirs):
    config, root = dirs
    (config / 'tutorial.json').write_text(json.dumps({'seenStamp': 5, 'done': 'x'}), encoding='utf-8')
    assert tutorial.load_state(config) == {'seenStamp': None, 'done': True}


def test_stamp_unchanged_after_seen_not_pending(dirs):
    config, root = dirs
    stamp(root, 'abc 2026-01-01T00:00:00Z')
    tutorial.save_state(config, root, False)
    assert not tutorial.is_pending(config, root)
    stamp(root, 'abc 2026-01-01T00:00:00Z')  # même contenu réécrit : toujours vu
    assert not tutorial.is_pending(config, root)


class _Signal:
    def emit(self, *args):
        pass


class _Bridge:
    """Bridge minimal : seul le mixin de visite est exercé."""
    tutorialPendingChanged = tutorialRequested = tutorialAborted = shellEvent = _Signal()
    screenFullscreen = False
    top = True
    args = type('A', (), {'preview': False})()

    def __init__(self):
        self._state = {'settings': {'x': 1}}
        self.init_tutorial()


@pytest.fixture
def bridge(dirs, monkeypatch):
    import bridge_tutorial
    config, root = dirs
    monkeypatch.setattr(bridge_tutorial, 'CONFIG', config)
    monkeypatch.setattr(bridge_tutorial, 'ROOT', root)
    cls = type('B', (bridge_tutorial.TutorialMixin, _Bridge), {})
    b = cls()
    b.events = []
    b.shellEvent = type('S', (), {'emit': lambda self, e: b.events.append(e)})()
    return b, config


def test_abort_keeps_pending_and_releases_holds(bridge):
    b, config = bridge
    b.tutorial_check()
    assert b.tutorial_on
    b.holdEdge('dock', True)
    b.holdEdge('social', True)
    b.abortTutorial()
    assert not b.tutorial_on and b.tutorial_wait and b.tutorial_pending()
    assert not b.tutorial_hold
    assert {'dockHide', 'socialHide'} <= set(b.events)
    assert not (config / 'tutorial.json').exists()


def test_abort_then_restarts_at_next_check(bridge):
    b, _ = bridge
    b.tutorial_check()
    b.abortTutorial()
    b.tutorial_check()
    assert b.tutorial_on


def test_fullscreen_mid_tour_aborts(bridge):
    b, config = bridge
    b.tutorial_check()
    b.screenFullscreen = True
    b.tutorial_check()
    assert not b.tutorial_on and b.tutorial_wait
    assert not (config / 'tutorial.json').exists()


def test_finish_and_abort_idempotent(bridge):
    b, config = bridge
    b.tutorial_check()
    b.finishTutorial(True)
    b.finishTutorial(False)  # sans effet : ne doit pas écraser 'done'
    b.abortTutorial()  # sans effet : ne ré-arme pas
    assert not b.tutorial_on and not b.tutorial_wait
    assert json.loads((config / 'tutorial.json').read_text(encoding='utf-8'))['done'] is True
    b.tutorial_check()
    assert not b.tutorial_on


@pytest.mark.parametrize('end', ['finishTutorial', 'abortTutorial'])
def test_panel_hold_released_on_exit(bridge, end):
    b, _ = bridge
    b.tutorial_check()
    b.holdEdge('panel', True)
    assert 'panel' in b.tutorial_hold and b.events == []
    getattr(b, end)(True) if end == 'finishTutorial' else getattr(b, end)()
    assert not b.tutorial_hold
