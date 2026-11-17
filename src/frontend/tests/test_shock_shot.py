import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

pytest.importorskip('PySide6')
from PySide6.QtCore import QSize
from PySide6.QtGui import QImage

from shock_shot import ShockShot, ShockShotProvider, shrink


class FakePixmap:
    def toImage(self):
        return QImage(400, 200, QImage.Format_RGB32)


class FakeScreen:
    def grabWindow(self, _):
        return FakePixmap()


class BrokenScreen:
    def grabWindow(self, _):
        raise RuntimeError('capture impossible')


def test_grab_serves_half_resolution_then_release_clears():
    provider = ShockShotProvider()
    assert provider.grab(FakeScreen())
    size = QSize()
    image = provider.requestImage('1', size, QSize())
    assert (image.width(), image.height()) == (200, 100)
    provider.clear()
    assert provider.requestImage('1', size, QSize()).isNull()


def test_grab_failure_gives_no_image():
    provider = ShockShotProvider()
    assert not provider.grab(BrokenScreen())
    assert provider.requestImage('1', QSize(), QSize()).isNull()


def test_shrink_null_stays_null():
    assert shrink(QImage()).isNull()


def test_capture_slot_failure_returns_false_and_keeps_seq(monkeypatch):
    shock = ShockShot()
    monkeypatch.setattr(shock.provider, 'grab', lambda screen: False)
    seq = shock.seq
    assert shock.capture('inconnu') is False
    assert shock.seq == seq
    assert shock.provider.image.isNull()


def test_capture_unknown_screen_returns_false_without_fallback(monkeypatch):
    shock = ShockShot()
    grabbed = []
    monkeypatch.setattr(shock.provider, 'grab', lambda screen: grabbed.append(screen) or True)
    seq = shock.seq
    assert shock.capture('ecran-inexistant') is False
    assert grabbed == []
    assert shock.seq == seq


def test_no_file_written(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    provider = ShockShotProvider()
    provider.grab(FakeScreen())
    provider.clear()
    assert list(tmp_path.iterdir()) == []
