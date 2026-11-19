"""Ordre Z du fond natif : BackdropRaise côté Python, chaînage côté C++ (source), sonde de diagnostic."""
import importlib
import sys
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parent.parent
for sub in ('src/frontend', 'tools'):
    if str(ROOT / sub) not in sys.path:
        sys.path.insert(0, str(ROOT / sub))

native_glass = importlib.import_module('native_glass')
probe = importlib.import_module('zorder_probe')


def fake(dll, windows):
    calls = []
    user = SimpleNamespace(SetWindowPos=lambda *args: calls.append(('pos', args[0])))
    return SimpleNamespace(dll=dll, windows=windows, user=user), calls


def test_raise_uses_backdrop_raise_for_backdrop_handles_only():
    calls = []
    dll = SimpleNamespace(BackdropRaise=lambda h: calls.append(('raise', h)) or 0)
    self, pos = fake(dll, {1: {}, 3: {}})
    native_glass.NativeGlass.raise_content(self, (1, 2, 3))
    assert calls == [('raise', 1), ('raise', 3)]
    assert pos == [('pos', 2)]


def test_raise_falls_back_when_export_missing():
    self, pos = fake(SimpleNamespace(), {1: {}})
    native_glass.NativeGlass.raise_content(self, (1, 2))
    assert pos == [('pos', 1), ('pos', 2)]


def test_raise_falls_back_without_dll_or_on_failure():
    self, pos = fake(None, {1: {}})
    native_glass.NativeGlass.raise_content(self, (1,))
    assert pos == [('pos', 1)]
    self, pos = fake(SimpleNamespace(BackdropRaise=lambda h: -1), {1: {}})
    native_glass.NativeGlass.raise_content(self, (1,))
    assert pos == [('pos', 1)]


def compact_source(rel):
    # Contrat sur les jetons, pas sur la mise en page : tous les blancs sont retirés avant la recherche.
    return ''.join((ROOT / rel).read_text(encoding='utf-8').split())


def test_cpp_source_contract():
    source = compact_source('src/native/qtglass/QtGlassBackdrop.cpp')
    assert 'BackdropRaise(HWNDcontent)' in source and 'dllexport' in source.split('BackdropRaise')[0][-60:]
    assert 'DeferWindowPos' in source and 'BeginDeferWindowPos' in source and 'EndDeferWindowPos' in source
    update = source[source.index('BackdropUpdate(HWND'):source.index('BackdropRaise(HWND')]
    assert 'elseif(ChainBroken(item,content))' in update
    assert 'HWND_TOPMOST,0,0,0,0' in update


def entry(hwnd, rect, **kw):
    base = dict(hwnd=hwnd, pid=kw.pop('pid', 9), visible=True, rect=rect, winlestia=False, content=False, host=False)
    base.update(kw)
    return base


CONTENT = dict(winlestia=True, content=True)
HOST = dict(winlestia=True, host=True)


def test_probe_detects_sandwich():
    zorder = [entry(1, (0, 0, 100, 100), **CONTENT), entry(2, (50, 50, 200, 200), pid=5), entry(3, (0, 0, 100, 100), **HOST)]
    found = probe.find_sandwiches(zorder)
    assert [(c['hwnd'], o['hwnd'], h['hwnd']) for c, o, h in found] == [(1, 2, 3)]


def test_probe_ignores_clean_chain_foreign_above_and_disjoint():
    clean = [entry(1, (0, 0, 100, 100), **CONTENT), entry(3, (0, 0, 100, 100), **HOST), entry(2, (0, 0, 100, 100), pid=5)]
    above = [entry(2, (0, 0, 100, 100), pid=5), entry(1, (0, 0, 100, 100), **CONTENT), entry(3, (0, 0, 100, 100), **HOST)]
    apart = [entry(1, (0, 0, 100, 100), **CONTENT), entry(2, (500, 500, 600, 600), pid=5), entry(3, (0, 0, 100, 100), **HOST)]
    hidden = [entry(1, (0, 0, 100, 100), **CONTENT), entry(2, (0, 0, 100, 100), pid=5, visible=False), entry(3, (0, 0, 9, 9), **HOST)]
    for case in (clean, above, apart, hidden):
        assert probe.find_sandwiches(case) == []


def test_probe_helpers():
    assert probe.intersects((0, 0, 10, 10), (5, 5, 20, 20)) and not probe.intersects((0, 0, 10, 10), (10, 0, 20, 10))
    assert probe.is_winlestia({'exe': 'Panneaux.Glass.exe'}) and probe.is_winlestia({'cls': 'WinLestiaContinuousGlass91'})
    assert not probe.is_winlestia({'exe': 'notepad.exe', 'title': 'x', 'cls': 'Notepad'})


def test_cpp_review_fixes():
    source = compact_source('src/native/qtglass/QtGlassBackdrop.cpp')
    drift = source[source.index('staticboolChainBroken'):
                   source.index('extern"C"__declspec(dllexport)HRESULT__stdcallBackdropAttach')]
    assert 'DWMWA_CLOAKED' in drift and 'IntersectRect' in drift and 'r.right<=r.left' in drift
    update = source[source.index('BackdropUpdate(HWND'):source.index('BackdropRaise(HWND')]
    assert 'GetTickCount64' in update and '>=1000' in update
    assert 'HWND_NOTOPMOST' in update and 'HWND_TOP,' in update
    raised = source[source.index('BackdropRaise(HWND'):]
    assert raised.index('HWND_TOPMOST') < raised.index('IsWindowVisible')
    assert 'SWP_NOOWNERZORDER' in source
    assert 'item->owner=GetWindow(content,GW_OWNER)' in source  # hôtes détenus par le propriétaire du contenu, pas par le contenu
