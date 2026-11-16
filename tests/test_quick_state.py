"""Bridge.quick : objet des bascules rapides (Wi-Fi, Bluetooth...), toujours un dictionnaire, vide par défaut."""
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

pytest.importorskip('PySide6.QtCore')
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src' / 'frontend'))

from bridge import Bridge  # noqa: E402


def quick(state):
    return Bridge.quick.fget(SimpleNamespace(_state=state))


def test_quick_defaults_to_empty_map():
    assert quick({}) == {}
    assert quick({'quick': None}) == {}


def test_quick_passes_the_backend_object_through():
    sent = {'wifi': {'available': True, 'on': True}, 'airplane': {'available': False, 'on': False}}
    assert quick({'quick': sent}) == sent


def test_quick_missing_keys_stay_missing():
    # QML lit avec ?. et ?? : une bascule absente ne doit pas être inventée côté Python.
    assert 'bluetooth' not in quick({'quick': {'wifi': {'available': True, 'on': False}}})


def test_dock_lists_pass_through_settings():
    state = {'settings': {'dockBar': ['settings'], 'dockDrawer': ['wifi']}}
    settings = Bridge.settings.fget(SimpleNamespace(_state=state))
    assert settings['dockBar'] == ['settings'] and settings['dockDrawer'] == ['wifi']
