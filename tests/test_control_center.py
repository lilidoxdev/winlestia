"""Centre de contrôle : rendu hors écran de WindowsControls avec un faux état (tuiles, sous-pages, Bluetooth, palette)."""
import os
import re
import sys
import tempfile
import time
from pathlib import Path
from types import SimpleNamespace

import pytest

os.environ.setdefault('QT_QPA_PLATFORM', 'offscreen')
os.environ.setdefault('WINLESTIA_DATA_DIR', tempfile.mkdtemp(prefix='wl-cc-'))
pytest.importorskip('PySide6.QtQml')

FRONT = Path(__file__).resolve().parent.parent / 'src' / 'frontend'
QML = FRONT / 'qml'
sys.path.insert(0, str(FRONT))

from PySide6.QtCore import QCoreApplication, QUrl, QMetaObject, Q_ARG  # noqa: E402
from PySide6.QtGui import QFontDatabase  # noqa: E402
from PySide6.QtQml import QQmlApplicationEngine  # noqa: E402
from PySide6.QtQuick import QQuickView  # noqa: E402
from PySide6.QtWidgets import QApplication  # noqa: E402

PAIRED = [
    {'id': 'h1', 'name': 'Casque', 'kind': 'headset', 'connected': True, 'percent': 85, 'canConnect': True},
    {'id': 'm1', 'name': 'Souris', 'kind': 'mouse', 'connected': True, 'percent': 40, 'canConnect': False},
    {'id': 'g1', 'name': 'Manette', 'kind': 'gamepad', 'connected': False, 'percent': None, 'canConnect': True},
    {'id': 'system:battery', 'name': '', 'kind': 'laptop', 'connected': True, 'percent': 42},
]


def state():
    return {
        'state': {
            'dark': True,
            'settings': {'reducedEffects': True},
            'controls': {
                'volume': 40,
                'muted': False,
                'network': 'Freebox',
                'wifiOn': True,
                'bluetoothOn': True,
                'displays': [{'id': 1, 'name': 'DELL', 'value': 63, 'available': True}],
                'wifi': {
                    'state': 'ok',
                    'networks': [
                        {'ssid': 'Freebox', 'bars': 4, 'security': 'wpa2', 'secure': True, 'connected': True, 'saved': True,
                            'supported': True},
                        {'ssid': 'Voisin', 'bars': 2, 'security': 'wpa2', 'secure': True, 'connected': False, 'saved': False,
                            'supported': True},
                    ],
                },
            },
            'peripherals': {'enabled': True, 'devices': [], 'paired': [dict(p) for p in PAIRED], 'busy': ''},
            'audioMixer': {
                'devices': [{'id': 'o1', 'name': 'Haut-parleurs', 'selected': True, 'volume': 50, 'muted': False, 'flow': 0}],
                'applications': [{'id': 'a', 'name': 'Spotify', 'pid': 1, 'volume': 30, 'muted': False}],
                'ready': True,
            },
        }
    }


HARNESS = """
import QtQuick
import "%s"
Item {
    width: 450
    height: 640
    WindowsControls { objectName: "panel"; width: 410; warmed: true }
}
"""


def pump(app, until, timeout=3.0):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        app.processEvents()
        QCoreApplication.sendPostedEvents(None, 0)
        if until():
            return True
        time.sleep(0.01)
    return until()


@pytest.fixture(scope='module')
def qapp():
    app = QApplication.instance() or QApplication([])
    app.setQuitOnLastWindowClosed(False)
    for font in (FRONT / 'assets' / 'fonts').glob('*.ttf'):
        QFontDatabase.addApplicationFont(str(font))
    return app


@pytest.fixture(scope='module')
def bridge(qapp):
    import app_state
    from localization import Translator
    from bridge_client import PipeClient

    app_state.TRANSLATOR = Translator(tempfile.mkdtemp(prefix='wl-lang-'))
    original = PipeClient.start
    PipeClient.start = lambda self, *a, **k: None
    from bridge import Bridge

    b = Bridge(SimpleNamespace(pipe='WinLestiaControlCenterOffline', backend_pid=0, preview=True))
    PipeClient.start = original
    b.pointer.stop()
    b.city = ''
    b.update_state(state())
    yield b
    b.walltheme.stop()


@pytest.fixture(scope='module')
def panel(qapp, bridge):
    engine = QQmlApplicationEngine()
    engine.addImportPath(str(QML))
    engine.rootContext().setContextProperty('Bridge', bridge)
    tmp = Path(tempfile.mkdtemp()) / 'Harness.qml'
    tmp.write_text(HARNESS % QML.as_uri(), encoding='utf-8')
    view = QQuickView(engine, None)
    view.setSource(QUrl.fromLocalFile(str(tmp)))
    assert view.status() == QQuickView.Ready, view.errors()
    view.show()
    pump(qapp, lambda: False, 0.3)
    yield next(i for i in items(view.rootObject()) if i.objectName() == 'panel')
    view.close()
    engine.deleteLater()


def items(item):
    yield item
    for child in item.childItems():
        yield from items(child)


def named(root, name):
    return [i for i in items(root) if i.objectName() == name]


def drain(bridge):
    sent = []
    while not bridge.client.commands.empty():
        sent.append(bridge.client.commands.get_nowait())
    return sent


def go(qapp, panel, page):
    panel.setProperty('page', page)
    slot = {'main': 'ccMain', 'wifi': 'ccWifi', 'bluetooth': 'ccBluetooth', 'sound': 'ccSound'}[page]
    assert pump(qapp, lambda: named(panel, slot) and named(panel, slot)[0].property('opacity') == 1)
    return named(panel, slot)[0]


def test_tiles_subtitles(qapp, bridge, panel):
    tiles = {n: named(panel, n)[0] for n in ('tileWifi', 'tileBluetooth', 'tileDark', 'tileLight')}
    assert tiles['tileWifi'].property('subtitle') == 'Freebox'
    assert tiles['tileWifi'].property('checked') is True
    assert tiles['tileBluetooth'].property('subtitle') == '2 connectés'
    assert tiles['tileDark'].property('subtitle') == 'Activé'
    assert tiles['tileLight'].property('subtitle') == 'Moins d’effets'
    one = state()
    one['state']['peripherals']['paired'] = [dict(PAIRED[0])]
    one['state']['controls']['wifiOn'] = False
    bridge.update_state(one)
    pump(qapp, lambda: tiles['tileBluetooth'].property('subtitle') == 'Casque')
    assert tiles['tileBluetooth'].property('subtitle') == 'Casque'
    assert tiles['tileWifi'].property('subtitle') == 'Désactivé'
    bridge.update_state(state())
    assert pump(qapp, lambda: tiles['tileWifi'].property('subtitle') == 'Freebox')


def test_title_renamed(qapp, panel):
    assert named(panel, 'ccTitle')[0].property('text') == 'Centre de contrôle'


def test_brightness_hidden_without_screen(qapp, bridge, panel):
    box = named(panel, 'ccBrightness')[0]
    assert pump(qapp, lambda: box.property('visible') is True)
    none = state()
    none['state']['controls']['displays'] = []
    bridge.update_state(none)
    assert pump(qapp, lambda: box.property('visible') is False)
    bridge.update_state(state())
    assert pump(qapp, lambda: box.property('visible') is True)


def test_push_and_pop(qapp, panel):
    main = named(panel, 'ccMain')[0]
    for page in ('wifi', 'bluetooth', 'sound'):
        sub = go(qapp, panel, page)
        assert pump(qapp, lambda: main.property('opacity') == 0)
        assert sub.property('x') == 0
        assert main.property('visible') is False
        go(qapp, panel, 'main')
    assert main.property('opacity') == 1


def test_bluetooth_rows(qapp, bridge, panel):
    go(qapp, panel, 'bluetooth')

    def subs():
        return [i.property('text') for i in named(panel, 'btSubtitle')]

    assert pump(qapp, lambda: len(subs()) == 3)
    assert sorted(subs()) == ['Appairé', 'Connecté • 40 %', 'Connecté • 85 %']
    busy = state()
    busy['state']['peripherals']['busy'] = 'g1'
    bridge.update_state(busy)
    assert pump(qapp, lambda: 'Connexion…' in subs())
    bridge.update_state(state())
    assert pump(qapp, lambda: 'Connexion…' not in subs())
    page = [i for i in items(panel) if i.metaObject().className().startswith('CcBluetoothPage')][0]
    drain(bridge)
    for ident, connected, can in (('g1', False, True), ('m1', True, False)):
        QMetaObject.invokeMethod(page, 'toggle', Q_ARG('QVariant', ident), Q_ARG('QVariant', connected), Q_ARG('QVariant', can))
    sent = drain(bridge)
    assert sent[0] == {'command': 'bluetoothDevice', 'args': {'id': 'g1', 'connect': True}}
    assert sent[1]['command'] == 'openBluetooth'
    go(qapp, panel, 'main')


def test_bluetooth_empty_state(qapp, bridge, panel):
    empty = state()
    empty['state']['peripherals']['paired'] = []
    bridge.update_state(empty)
    go(qapp, panel, 'bluetooth')
    assert pump(qapp, lambda: named(panel, 'btSubtitle') == [])
    assert named(panel, 'btEmpty')[0].property('visible') is True
    bridge.update_state(state())
    go(qapp, panel, 'main')


def test_wifi_page_loads(qapp, panel):
    go(qapp, panel, 'wifi')
    assert pump(qapp, lambda: len(named(panel, 'wifiState')) == 1)
    go(qapp, panel, 'main')


def test_no_hardcoded_colours():
    files = ['WindowsControls.qml'] + sorted(p.name for p in QML.glob('Cc*.qml'))
    assert len(files) >= 9
    for name in files:
        text = (QML / name).read_text(encoding='utf-8')
        assert not re.search(r'"#[0-9a-fA-F]{3,8}"', text), name
        assert 'Qt.rgba(' not in text, name
        assert 'Qt.color(' not in text, name
