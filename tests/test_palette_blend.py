"""Mélange de palettes (Colours.qml) : fonctions JS pures évaluées par QJSEngine."""
from pathlib import Path

import pytest

pytest.importorskip("PySide6")
from PySide6.QtWidgets import QApplication  # noqa: E402
from PySide6.QtQml import QJSEngine  # noqa: E402

SOURCE = Path(__file__).resolve().parents[1] / "src" / "frontend" / "qml" / "qs" / "services" / "PaletteBlend.js"


@pytest.fixture(scope="module")
def engine():
    app = QApplication.instance() or QApplication([])
    e = QJSEngine()
    e.app = app
    e.evaluate(SOURCE.read_text(encoding="utf-8"))
    return e


def call(engine, expr):
    return engine.evaluate(expr).toVariant()


def test_endpoints_and_midpoint(engine):
    assert call(engine, 'mix("#000000", "#ffffff", 0)') == "#000000"
    assert call(engine, 'mix("#000000", "#ffffff", 1)') == "#ffffff"
    assert call(engine, 'mix("#000000", "#ffffff", 0.5)') == "#ff808080"


def test_alpha_and_short_forms(engine):
    assert call(engine, 'mix("#00ffffff", "#ffffffff", 0.5)') == "#80ffffff"
    assert call(engine, 'mix("#000", "#fff", 0.5)') == "#ff808080"


def test_unreadable_colour_snaps_to_target(engine):
    assert call(engine, 'mix("transparent", "#102030", 0.3)') == "#102030"


def test_snapshot_restarts_from_displayed_colours(engine):
    out = call(engine, 'snapshot({a: "#000000", b: "#111111"}, {a: "#ffffff"}, 0.5)')
    assert out == {"a": "#ff808080", "b": "#111111"}


def test_same_ignores_unchanged_palettes(engine):
    assert call(engine, 'same({a: "#000000", b: "#111111"}, {b: "#111111", a: "#000000"})') is True
    assert call(engine, 'same({a: "#000000"}, {a: "#000001"})') is False
    assert call(engine, 'same({a: "#000000"}, {a: "#000000", b: "#111111"})') is False
    assert call(engine, 'same({}, {})') is True
