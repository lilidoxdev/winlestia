"""LyricsMath.js : calculs purs des paroles synchronisées, exécutés dans un QJSEngine (sans fenêtre)."""
from pathlib import Path

import pytest

pytest.importorskip('PySide6.QtQml')
from PySide6.QtGui import QGuiApplication  # noqa: E402
from PySide6.QtQml import QJSEngine  # noqa: E402

SOURCE = Path(__file__).resolve().parent.parent / 'src' / 'frontend' / 'qml' / 'lyrics' / 'LyricsMath.js'
LINES = '[{time:1,text:"a"},{time:5,text:""},{time:9,text:"c"}]'


@pytest.fixture(scope='module')
def js():
    app = QGuiApplication.instance() or QGuiApplication([])  # QJSEngine plante sans application Qt
    engine = QJSEngine()
    engine.app = app
    engine.evaluate(SOURCE.read_text(encoding='utf-8').replace('.pragma library', ''))
    return engine


def call(js, expr):
    value = js.evaluate(expr)
    assert not value.isError(), value.toString()
    return value.toVariant()


def test_active_index(js):
    assert call(js, f'activeIndex({LINES}, 0)') == -1
    assert call(js, f'activeIndex({LINES}, 1)') == 0
    assert call(js, f'activeIndex({LINES}, 4.9)') == 0
    assert call(js, f'activeIndex({LINES}, 5)') == 1
    assert call(js, f'activeIndex({LINES}, 500)') == 2
    assert call(js, 'activeIndex([], 3)') == -1
    assert call(js, 'activeIndex(undefined, 3)') == -1
    assert call(js, f'activeIndex({LINES}, NaN)') == -1


def test_next_time(js):
    assert call(js, f'nextTime({LINES}, -1)') == 1
    assert call(js, f'nextTime({LINES}, 0)') == 5
    assert call(js, f'nextTime({LINES}, 2)') == -1


def test_progress_in_line(js):
    assert call(js, f'progressInLine({LINES}, 0, 3)') == 0.5
    assert call(js, f'progressInLine({LINES}, 0, 100)') == 1
    assert call(js, f'progressInLine({LINES}, 2, 100)') == 1
    assert call(js, f'progressInLine({LINES}, -1, 3)') == 0
