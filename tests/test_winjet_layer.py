"""Couche bureau des Winjets : décisions pures (aucun appel natif)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src' / 'frontend'))

import desktop_layer as dl  # noqa: E402
import winjet_expr as ex  # noqa: E402


def win(**kw):
    return {'visible': True, 'minimized': False, 'cloaked': False, 'tool': False, 'ours': False, 'shell': False, **kw}


def test_desktop_shown_only_without_visible_app_windows():
    assert dl.desktop_shown([])
    assert not dl.desktop_shown([win()])
    assert dl.desktop_shown([win(minimized=True)])
    assert dl.desktop_shown([win(cloaked=True)])  # fantôme UWP
    assert dl.desktop_shown([win(tool=True), win(ours=True), win(shell=True), win(visible=False)])
    assert not dl.desktop_shown([win(cloaked=True), win()])


def test_click_on_wallpaper_with_apps_stays_at_bottom():
    assert dl.next_mode('bottom', 'desktop', False, shown=False) == 'bottom'
    assert dl.next_mode('bottom', 'desktop', False, shown=True) == 'above'
    assert dl.next_mode('above', 'other', False) == 'bottom'
    assert dl.next_mode('above', 'desktop', False, shown=False) == 'bottom'
    assert dl.next_mode('edit', 'other', False) == 'edit'
    assert dl.next_mode('above', 'other', True) == 'above'


def test_unregister_after_destruction_forgets_handle_and_glass():
    class Glass:
        detached = []

        def detach(self, hwnd):
            self.detached.append(hwnd)

    layer = dl.DesktopLayer.__new__(dl.DesktopLayer)
    layer.windows, layer.hwnds, layer.handles, layer.glass = {7: object()}, {7: 4242}, {4242}, Glass()
    layer._forget(7)
    assert not layer.handles and not layer.hwnds and Glass.detached == [4242]
    layer._forget(7)  # idempotent


def test_template_length_is_capped():
    long = '{{ settings.a }}' * 500
    assert len(ex.render(long, {'settings': {'a': 'x'}})) <= ex.MAX_TEMPLATE
