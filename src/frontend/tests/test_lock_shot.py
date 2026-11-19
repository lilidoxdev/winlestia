import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

pytest.importorskip('PySide6')
from PySide6.QtCore import QCoreApplication, QSize
from PySide6.QtGui import QImage

from lock_shot import LockShotProvider, shrink


def test_provider_returns_null_image_when_empty():
    provider = LockShotProvider()
    size = QSize()
    assert provider.requestImage('0?3', size, QSize()).isNull()


def test_provider_serves_and_clears():
    provider = LockShotProvider()
    provider.shots = {'0': shrink(QImage(400, 200, QImage.Format_RGB32))}
    size = QSize()
    image = provider.requestImage('0?1', size, QSize())
    assert (image.width(), image.height()) == (100, 50)
    provider.clear()
    assert provider.requestImage('0?1', size, QSize()).isNull()


def test_shrink_null_stays_null():
    assert shrink(QImage()).isNull()


def test_idle_normalisation_js():
    from PySide6.QtQml import QJSEngine

    app = QCoreApplication.instance() or QCoreApplication([])
    engine = QJSEngine()
    source = (ROOT / 'qml/settings/IdleTime.js').read_text(encoding='utf-8').replace('.pragma library', '')
    engine.evaluate(source)
    normalize = engine.globalObject().property('normalize')

    def run(minutes, seconds):
        value = normalize.call([minutes, seconds]).toVariant()
        return value['total'], value['minutes'], value['seconds']

    assert run(0, 5) == (30, 0, 30)
    assert run(2, 30) == (150, 2, 30)
    assert run(0, 0) == (30, 0, 30)
    assert run(1440, 59) == (86400, 1440, 0)
