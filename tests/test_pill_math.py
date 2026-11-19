"""PillMath.js : calculs purs de la Pilule, exécutés dans un QJSEngine (sans fenêtre)."""
from pathlib import Path

import pytest

pytest.importorskip('PySide6.QtQml')
from PySide6.QtQml import QJSEngine  # noqa: E402

SOURCE = Path(__file__).resolve().parent.parent / 'src' / 'frontend' / 'qml' / 'PillMath.js'


@pytest.fixture(scope='module')
def js():
    engine = QJSEngine()
    engine.evaluate(SOURCE.read_text(encoding='utf-8').replace('.pragma library', ''))
    return engine


def call(js, expr):
    value = js.evaluate(expr)
    assert not value.isError(), value.toString()
    return value.toVariant()


def test_clock(js):
    assert call(js, 'clock(83)') == '1:23'
    assert call(js, 'clock(3725)') == '1:02:05'
    assert call(js, 'clock(-1)') == '--:--'
    assert call(js, 'clock(NaN)') == '--:--'


def test_fraction(js):
    assert call(js, 'fraction(30, 120)') == 0.25
    assert call(js, 'fraction(5, 0)') == 0
    assert call(js, 'fraction(500, 120)') == 1


def test_follow_y(js):
    assert call(js, 'followY(12, 0, 12)') == 12
    assert call(js, 'followY(12, 5, 12)') == 17
    assert call(js, 'followY(12, 200, 12)') == 212


def test_step_volume(js):
    assert call(js, 'stepVolume(50, 2, 5)') == 60
    assert call(js, 'stepVolume(3, -1, 5)') == 0
    assert call(js, 'stepVolume(98, 1, 5)') == 100


def test_level(js):
    assert call(js, 'level([], 0, 4)') == 0
    assert call(js, 'level([1,1,1,1], 0, 2)') == 1
    assert call(js, 'level([0.25,0.25], 0, 1)') == 0.5
