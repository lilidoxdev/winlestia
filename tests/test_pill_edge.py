"""Exclusion du bord haut à l'aplomb de la Pilule : seulement quand on la vise, jamais pour une pilule compacte au repos."""
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

pytest.importorskip('PySide6.QtWidgets')
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src' / 'frontend'))

from bridge_pointer import PointerMixin, pill_blocks_edge  # noqa: E402

PILL = SimpleNamespace(left=lambda: 400, right=lambda: 600)


def test_compact_not_hovered_excludes_nothing():
    assert not pill_blocks_edge(PILL, 500, False, False)


def test_expanded_excludes_its_width_with_margin():
    assert pill_blocks_edge(PILL, 500, True, False)
    assert pill_blocks_edge(PILL, 392, True, False)
    assert not pill_blocks_edge(PILL, 380, True, False)


def test_reaching_for_compact_pill_excludes():
    assert pill_blocks_edge(PILL, 450, False, True)


def test_no_pill_on_this_screen():
    assert not pill_blocks_edge(None, 500, True, True)


def test_pill_window_ignores_hidden_windows(monkeypatch):
    import bridge_pointer

    class Win:
        def __init__(self, name, shown):
            self._n, self._s = name, shown

        def objectName(self):
            return self._n

        def isVisible(self):
            return self._s

    wins = [Win('other', True), Win('livePill', False), Win('livePill', True)]
    monkeypatch.setattr(bridge_pointer.QApplication, 'topLevelWindows', staticmethod(lambda: wins))
    assert PointerMixin.pill_window(None) is wins[2]
