import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

pytest.importorskip('PySide6')
from PySide6.QtCore import QSize
from PySide6.QtGui import QGuiApplication, QImage

from shock_shot import ShockShot, ShockShotProvider, clamp_region, shrink


class FakePixmap:
    def __init__(self, w, h):
        self.size = (w, h)

    def toImage(self):
        return QImage(*self.size, QImage.Format_RGB32)


class FakeScreen:
    def __init__(self):
        self.calls = []

    def grabWindow(self, _, x=0, y=0, w=400, h=200):
        self.calls.append((x, y, w, h))
        return FakePixmap(w, h)


class BrokenScreen:
    def grabWindow(self, *_):
        raise RuntimeError('capture impossible')


def test_clamp_region_keeps_inside_screen():
    assert clamp_region(1000, 500, 100, 50, 200, 100) == (100, 50, 200, 100)
    assert clamp_region(1000, 500, -60, -20, 200, 100) == (0, 0, 140, 80)
    assert clamp_region(1000, 500, 900, 450, 200, 100) == (900, 450, 100, 50)
    assert clamp_region(1000, 500, 2000, 0, 200, 100) is None
    assert clamp_region(1000, 500, 0, 0, 0, 100) is None


def test_capture_grabs_clamped_region_at_half_resolution(monkeypatch):
    shock = ShockShot()
    screen = FakeScreen()
    screen.size = lambda: QSize(1000, 500)
    screen.name = lambda: 'fake'
    monkeypatch.setattr(QGuiApplication, 'screens', staticmethod(lambda: [screen]))
    assert shock.capture('fake', 900, -20, 200, 100) is True
    assert screen.calls == [(900, 0, 100, 80)]
    size = QSize()
    image = shock.provider.requestImage('1', size, QSize())
    assert (image.width(), image.height()) == (50, 40)
    assert shock.capture('fake', 5000, 0, 200, 100) is False
    assert shock.provider.image.isNull()


def test_grab_serves_half_resolution_then_release_clears():
    provider = ShockShotProvider()
    assert provider.grab(FakeScreen(), 0, 0, 400, 200)
    size = QSize()
    image = provider.requestImage('1', size, QSize())
    assert (image.width(), image.height()) == (200, 100)
    provider.clear()
    assert provider.requestImage('1', size, QSize()).isNull()


def test_grab_failure_gives_no_image():
    provider = ShockShotProvider()
    assert not provider.grab(BrokenScreen(), 0, 0, 10, 10)
    assert provider.requestImage('1', QSize(), QSize()).isNull()


def test_shrink_null_stays_null():
    assert shrink(QImage()).isNull()


def test_capture_slot_failure_returns_false_and_keeps_seq(monkeypatch):
    shock = ShockShot()
    monkeypatch.setattr(shock.provider, 'grab', lambda *args: False)
    seq = shock.seq
    assert shock.capture('inconnu', 0, 0, 10, 10) is False
    assert shock.seq == seq
    assert shock.provider.image.isNull()


def test_capture_unknown_screen_returns_false_without_fallback(monkeypatch):
    shock = ShockShot()
    grabbed = []
    monkeypatch.setattr(shock.provider, 'grab', lambda *args: grabbed.append(args) or True)
    seq = shock.seq
    assert shock.capture('ecran-inexistant', 0, 0, 10, 10) is False
    assert grabbed == []
    assert shock.seq == seq


def test_no_file_written(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    provider = ShockShotProvider()
    provider.grab(FakeScreen(), 0, 0, 400, 200)
    provider.clear()
    assert list(tmp_path.iterdir()) == []


def test_trace_flag_file_enables_trace(tmp_path, monkeypatch):
    import importlib

    import open_trace

    monkeypatch.delenv('WINLESTIA_TRACE_OPEN', raising=False)
    monkeypatch.setenv('LOCALAPPDATA', str(tmp_path))
    flag = Path(open_trace.__file__).resolve().parent / open_trace.FLAG_NAME
    existed = flag.exists()
    try:
        flag.unlink(missing_ok=True)
        assert importlib.reload(open_trace).ENABLED is False
        flag.write_text('')
        assert importlib.reload(open_trace).ENABLED is True
        flag.unlink()
        open_trace.begin('event palette')  # relu à chaque begin()
        assert open_trace.ENABLED is False
    finally:
        monkeypatch.undo()
        if existed:
            flag.write_text('')
        else:
            flag.unlink(missing_ok=True)
        importlib.reload(open_trace)
