"""Bridge.lock : état de l'écran de verrouillage, toujours un dictionnaire, vide par défaut."""
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

pytest.importorskip('PySide6.QtCore')
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src' / 'frontend'))

from bridge import Bridge  # noqa: E402


def lock(state):
    return Bridge.lock.fget(SimpleNamespace(_state=state))


def test_lock_defaults_to_empty_map():
    assert lock({}) == {}
    assert lock({'lock': None}) == {}


def test_lock_passes_the_backend_object_through():
    sent = {'locked': True, 'failures': 2, 'retryAfter': 30, 'passwordResult': 'wrong', 'passwordSeq': 4}
    assert lock({'lock': sent}) == sent


def test_focus_helper_never_raises_on_invalid_handle():
    import native_glass

    assert native_glass.focus_native_window(0) is False
    assert native_glass.focus_native_window('x') is False
    native_glass.focus_native_window(1)  # handle invalide : ne doit pas lever


def test_lock_window_registration():
    fake = SimpleNamespace(lock_windows=[])
    window = SimpleNamespace(isVisible=lambda: True)
    hidden = SimpleNamespace(isVisible=lambda: False)
    Bridge.registerLockWindow(fake, window)
    Bridge.registerLockWindow(fake, window)
    Bridge.registerLockWindow(fake, hidden)
    assert fake.lock_windows == [window, hidden]
    assert Bridge.live_lock_windows(fake) == [window]
    Bridge.unregisterLockWindow(fake, window)
    assert Bridge.live_lock_windows(fake) == []
