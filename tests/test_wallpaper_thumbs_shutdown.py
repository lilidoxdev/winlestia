"""Fermeture : le thread de vignettes ne doit jamais lever quand le signal Qt est déjà détruit."""
import sys
import threading
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src' / 'frontend'))

import bridge_wallpaper  # noqa: E402


class _DeadSignal:
    def emit(self, *args):
        raise RuntimeError('Signal source has been deleted')


class _Fake(bridge_wallpaper.WallpaperMixin):
    wallpaperThumbReady = _DeadSignal()


def _wait_threads():
    for t in threading.enumerate():
        if t is not threading.current_thread() and t.daemon:
            t.join(2)


def test_dead_signal_is_swallowed(monkeypatch):
    monkeypatch.setattr(bridge_wallpaper, 'thumb_for', lambda p: str(Path(__file__)))
    errors = []
    monkeypatch.setattr(threading, 'excepthook', lambda a: errors.append(a))
    _Fake().requestWallpaperThumbs(['a', 'b'])
    _wait_threads()
    assert errors == []


def test_stop_prevents_emit(monkeypatch):
    emitted = []
    fake = _Fake()
    fake.stopWallpaperThumbs()
    monkeypatch.setattr(bridge_wallpaper, 'thumb_for', lambda p: emitted.append(p))
    fake.requestWallpaperThumbs([])
    assert emitted == []
