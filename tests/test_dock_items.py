"""DockItems.js : catalogue et calculs purs sur les listes barre/tiroir, exécutés dans un QJSEngine (sans fenêtre)."""
from pathlib import Path

import pytest

pytest.importorskip('PySide6.QtQml')
from PySide6.QtQml import QJSEngine  # noqa: E402

SOURCE = Path(__file__).resolve().parent.parent / 'src' / 'frontend' / 'qml' / 'DockItems.js'


@pytest.fixture(scope='module')
def js():
    import os

    from PySide6.QtWidgets import QApplication

    # Sans application Qt, créer un QJSEngine plante le processus quand ce fichier tourne seul. Même classe que
    # test_qml_runtime.py, sinon l'application créée ici empêcherait ses tests de créer la leur.
    os.environ.setdefault('QT_QPA_PLATFORM', 'offscreen')
    app = QApplication.instance() or QApplication([])
    engine = QJSEngine()
    engine._app = app  # garde l'application vivante tant que le moteur l'est
    engine.evaluate(SOURCE.read_text(encoding='utf-8').replace('.pragma library', ''))
    return engine


def call(js, expr):
    value = js.evaluate(expr)
    assert not value.isError(), value.toString()
    return value.toVariant()


BAR = '["start","recycle","files","search","volume","network","settings","power"]'
DRAWER = '["autoTile","running","wifi","bluetooth","micMute","keepAwake"]'


def test_defaults_match_contract(js):
    assert call(js, 'defaults()') == {
        'bar': ['start', 'recycle', 'files', 'search', 'volume', 'network', 'settings', 'power'],
        'drawer': ['autoTile', 'running', 'wifi', 'bluetooth', 'micMute', 'keepAwake'],
    }


def test_missing_lists_fall_back_to_defaults(js):
    assert call(js, 'normalize(undefined, undefined)') == call(js, 'defaults()')


def test_normalize_cleans_lists(js):
    got = call(js, 'normalize(["wifi","wifi","nope","search"], ["search","power","wifi"])')
    # doublon retiré, id inconnu retiré, lanceur refusé dans le tiroir, `settings` toujours présent
    assert got == {'bar': ['wifi', 'search', 'settings'], 'drawer': ['power']}


def test_placement(js):
    assert call(js, f'placement({BAR}, {DRAWER}, "volume")') == 'bar'
    assert call(js, f'placement({BAR}, {DRAWER}, "wifi")') == 'drawer'
    assert call(js, f'placement({BAR}, {DRAWER}, "nightLight")') == 'hidden'


def test_set_placement_moves_between_lists(js):
    got = call(js, f'setPlacement({BAR}, {DRAWER}, "volume", "drawer")')
    assert 'volume' not in got['bar'] and got['drawer'][-1] == 'volume'
    got = call(js, f'setPlacement({BAR}, {DRAWER}, "wifi", "hidden")')
    assert 'wifi' not in got['drawer'] and 'wifi' not in got['bar']
    got = call(js, f'setPlacement({BAR}, {DRAWER}, "nightLight", "bar")')
    assert got['bar'][-1] == 'nightLight'


def test_forbidden_placements_change_nothing(js):
    same = call(js, f'normalize({BAR}, {DRAWER})')
    assert call(js, f'setPlacement({BAR}, {DRAWER}, "settings", "hidden")') == same
    assert call(js, f'setPlacement({BAR}, {DRAWER}, "search", "drawer")') == same
    assert call(js, f'setPlacement({BAR}, {DRAWER}, "wifi", "nowhere")') == same


def test_move_swaps_with_neighbour(js):
    got = call(js, f'move({BAR}, {DRAWER}, "running", -1)')
    assert got['drawer'][:2] == ['running', 'autoTile']
    got = call(js, f'move({BAR}, {DRAWER}, "network", 1)')
    assert got['bar'][-3:] == ['settings', 'network', 'power']


def test_move_skips_launchers_and_stops_at_edges(js):
    assert call(js, f'canMove({BAR}, {DRAWER}, "volume", -1)') is False
    assert call(js, f'canMove({BAR}, {DRAWER}, "power", 1)') is False
    assert call(js, f'canMove({BAR}, {DRAWER}, "search", 1)') is False
    assert call(js, f'canMove({BAR}, {DRAWER}, "wifi", 1)') is True
    got = call(js, f'move(["start","volume","files","power","settings"], [], "power", -1)')
    assert got['bar'] == ['start', 'power', 'files', 'volume', 'settings']


def test_rows_and_bar_tools(js):
    rows = call(js, f'rows({BAR}, {DRAWER})')
    assert len(rows) == 17 and rows[-2:] == ['nightLight', 'focus']
    assert call(js, f'barTools({BAR}, {DRAWER})') == ['volume', 'network', 'settings', 'power']
