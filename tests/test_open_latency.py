"""Latence d'ouverture : ré-affirmation au premier plan seulement hors animation, trace opt-in muette par défaut."""
import importlib
import sys
from pathlib import Path

FRONTEND = str(Path(__file__).resolve().parent.parent / 'src' / 'frontend')


def load(name):
    if FRONTEND not in sys.path:
        sys.path.insert(0, FRONTEND)
    return importlib.import_module(name)


def test_topmost_skipped_while_animating():
    due = load('native_glass').topmost_due
    assert due((), (1,), True)  # fenêtre qui apparaît
    assert not due((1,), (1,), True)  # image d'animation : géométrie envoyée, même jeu de fenêtres
    assert due((1,), (1,), False)  # fin d'animation : synchronisation sans mouvement
    assert due((1,), (1, 2), True)  # nouvelle fenêtre pendant une animation
    assert not due((1,), (), False)  # rien de visible : rien à faire


def test_trace_off_by_default(tmp_path, monkeypatch):
    monkeypatch.delenv('WINLESTIA_TRACE_OPEN', raising=False)
    monkeypatch.delenv('LOCALAPPDATA', raising=False)
    monkeypatch.setattr('tempfile.tempdir', str(tmp_path))
    sys.modules.pop('open_trace', None)
    trace = load('open_trace')
    monkeypatch.setattr(trace, '_flag_file', lambda: False)
    trace.begin('event palette')
    trace.mark('openMode palette')
    assert not trace.ENABLED
    assert not list(tmp_path.iterdir())


def test_trace_writes_when_enabled(tmp_path, monkeypatch):
    monkeypatch.setenv('WINLESTIA_TRACE_OPEN', '1')
    monkeypatch.delenv('LOCALAPPDATA', raising=False)
    monkeypatch.setattr('tempfile.tempdir', str(tmp_path))
    sys.modules.pop('open_trace', None)
    trace = load('open_trace')
    trace.begin('event palette')
    trace.mark('first-frame')
    lines = (tmp_path / 'winlestia-open-trace.log').read_text(encoding='utf-8').splitlines()
    assert len(lines) == 2 and lines[1].endswith('first-frame')
    sys.modules.pop('open_trace', None)
