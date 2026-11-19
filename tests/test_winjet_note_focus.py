"""Bascule d'activation d'une carte pendant la saisie (bloc note) : appels Win32 simulés."""
import sys
from pathlib import Path
from unittest.mock import MagicMock

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src' / 'frontend'))

import desktop_layer as dl  # noqa: E402


def make(style=dl.WS_EX_TOOLWINDOW | dl.WS_EX_NOACTIVATE):
    layer = dl.DesktopLayer.__new__(dl.DesktopLayer)
    layer.native = True
    layer.mode = 'bottom'
    layer.user = MagicMock()
    layer.user.GetWindowLongPtrW.return_value = style
    window = MagicMock()
    window.property.return_value = 77
    return layer, window


def test_exstyle_for_focus():
    base = dl.WS_EX_TOOLWINDOW
    assert not dl.exstyle_for_focus(base | dl.WS_EX_NOACTIVATE, True) & dl.WS_EX_NOACTIVATE
    assert dl.exstyle_for_focus(base, False) & dl.WS_EX_NOACTIVATE
    assert dl.exstyle_for_focus(base | dl.WS_EX_NOACTIVATE, True) & base


def test_accept_clears_noactivate_and_activates():
    layer, window = make()
    layer.acceptFocus(window, True)
    new = layer.user.SetWindowLongPtrW.call_args[0][2]
    assert not new & dl.WS_EX_NOACTIVATE and new & dl.WS_EX_TOOLWINDOW
    layer.user.SetForegroundWindow.assert_called_once()
    window.setFlag.assert_called_once_with(dl.Qt.WindowDoesNotAcceptFocus, False)
    window.requestActivate.assert_called_once()


def test_release_restores_and_pushes_to_bottom():
    layer, window = make(style=dl.WS_EX_TOOLWINDOW)
    layer.acceptFocus(window, False)
    assert layer.user.SetWindowLongPtrW.call_args[0][2] & dl.WS_EX_NOACTIVATE
    layer.user.SetForegroundWindow.assert_not_called()
    window.setFlag.assert_called_once_with(dl.Qt.WindowDoesNotAcceptFocus, True)
    last = layer.user.SetWindowPos.call_args[0]
    assert last[1].value == dl.HWND_BOTTOM


def test_unchanged_style_skips_write():
    layer, window = make()
    layer.acceptFocus(window, False)
    layer.user.SetWindowLongPtrW.assert_not_called()


def test_none_window_ignored():
    layer, _ = make()
    layer.acceptFocus(None, True)
    layer.user.SetForegroundWindow.assert_not_called()


def test_foreground_failure_restores_noactivate():
    layer, window = make()
    layer.user.SetForegroundWindow.return_value = 0
    assert layer.acceptFocus(window, True) is False
    styles = [c[0][2] for c in layer.user.SetWindowLongPtrW.call_args_list]
    assert not styles[0] & dl.WS_EX_NOACTIVATE
    window.requestActivate.assert_not_called()
    assert window.setFlag.call_args[0] == (dl.Qt.WindowDoesNotAcceptFocus, True)
    assert (layer.user.SetWindowLongPtrW.call_args[0][2] & dl.WS_EX_NOACTIVATE
            or layer.user.GetWindowLongPtrW.return_value & dl.WS_EX_NOACTIVATE)
