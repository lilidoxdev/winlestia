"""Fumée QML hors écran : Main.qml est réellement instancié avec un vrai Bridge (sans tube) et chaque panneau est activé.

Attrape les fautes d'exécution que la simple compilation laisse passer (ReferenceError, TypeError, type inconnu,
affectation impossible, signal déclaré sans parenthèses, composant inline déplacé...).
"""
import os
import sys
import tempfile
from pathlib import Path
from types import SimpleNamespace

import pytest

os.environ.setdefault('QT_QPA_PLATFORM', 'offscreen')
os.environ.setdefault('WINLESTIA_DATA_DIR', tempfile.mkdtemp(prefix='wl-qmlsmoke-'))
pytest.importorskip('PySide6.QtQml')

FRONT = Path(__file__).resolve().parent.parent / 'src' / 'frontend'
sys.path.insert(0, str(FRONT))

from PySide6.QtCore import QCoreApplication, QUrl, QMetaObject, Q_ARG, qInstallMessageHandler  # noqa: E402
from PySide6.QtGui import QFontDatabase  # noqa: E402
from PySide6.QtQml import QQmlApplicationEngine, QQmlComponent  # noqa: E402
from PySide6.QtWidgets import QApplication  # noqa: E402

# Messages ignorés : bruit de la plateforme offscreen, sans lien avec le QML.
BRUIT = ('QFontDatabase', 'window masks', 'propagateSizeHints', 'QOpenGLContext', 'This plugin does not support')
FAUTES = ('ReferenceError', 'TypeError', 'is not a type', 'Cannot assign', 'is not installed', 'unavailable',
          'Unable to assign', 'Cannot read property', 'Invalid property', 'Invalid grouped')

PANNEAUX = ('tools', 'audio', 'microphone', 'mixer', 'capture', 'workspace', 'palette', 'settings', 'clipboard', 'controls')


@pytest.fixture(scope='session')
def qapp():
    app = QApplication.instance() or QApplication([])
    app.setQuitOnLastWindowClosed(False)
    for font in (FRONT / 'assets' / 'fonts').glob('*.ttf'):
        QFontDatabase.addApplicationFont(str(font))
    return app


@pytest.fixture(scope='session')
def messages():
    journal = []

    def handler(kind, context, message):
        if not any(b in message for b in BRUIT):
            journal.append((int(kind), message))

    qInstallMessageHandler(handler)
    yield journal
    qInstallMessageHandler(None)


@pytest.fixture(scope='session')
def bridge(qapp, messages):
    import app_state
    from localization import Translator
    from bridge_client import PipeClient

    app_state.TRANSLATOR = Translator(tempfile.mkdtemp(prefix='wl-lang-'))
    # Jamais de connexion au tube : le thread client est neutralisé.
    original = PipeClient.start
    PipeClient.start = lambda self, *a, **k: None
    from bridge import Bridge

    b = Bridge(SimpleNamespace(pipe='WinLestiaQmlSmokeOffline', backend_pid=0, preview=True))
    PipeClient.start = original
    b.pointer.stop()
    b.city = ''
    yield b
    b.walltheme.stop()


def pump(app, rounds=12):
    for _ in range(rounds):
        app.processEvents()
        QCoreApplication.sendPostedEvents(None, 0)


def open_mode(root, mode):
    QMetaObject.invokeMethod(root, 'openMode', Q_ARG('QVariant', mode))


def fautes(messages, depuis=0):
    return [m for k, m in messages[depuis:] if k >= 1 and any(f in m for f in FAUTES)]


@pytest.fixture(scope='session')
def shell(qapp, bridge, messages):
    engine = QQmlApplicationEngine()
    bridge.engine = engine
    engine.addImportPath(str(FRONT / 'qml'))
    engine.addImageProvider('capture', bridge.capture.provider)
    engine.rootContext().setContextProperty('Bridge', bridge)
    debut = len(messages)
    engine.load(QUrl.fromLocalFile(str(FRONT / 'qml' / 'Main.qml')))
    pump(qapp)
    assert not fautes(messages, debut), fautes(messages, debut)
    assert engine.rootObjects(), [m for _, m in messages[debut:]][:10]
    yield engine.rootObjects()[0], messages
    engine.deleteLater()
    pump(qapp)


def test_main_qml_instantiates_without_runtime_faults(shell):
    root, _ = shell
    assert root.property('pageMode') == 'dashboard'


@pytest.mark.parametrize('mode', PANNEAUX)
def test_each_pane_loads(qapp, shell, mode):
    root, messages = shell
    debut = len(messages)
    open_mode(root, mode)
    pump(qapp, 30)
    assert root.property('pageMode') == mode
    assert not fautes(messages, debut), fautes(messages, debut)
    open_mode(root, 'close')
    pump(qapp)


@pytest.mark.parametrize('prop', ('mixerOpen', 'wifiOpen'))
def test_controls_side_panes(qapp, shell, prop):
    root, messages = shell
    debut = len(messages)
    open_mode(root, 'controls')
    root.setProperty(prop, True)
    pump(qapp, 30)
    assert root.property(prop) is True
    assert not fautes(messages, debut), fautes(messages, debut)
    root.setProperty(prop, False)
    open_mode(root, 'close')
    pump(qapp)


def test_wallpaper_switcher_and_dashboard(qapp, shell, bridge):
    root, messages = shell
    debut = len(messages)
    bridge.shellEvent.emit('wallpaper-switcher')
    pump(qapp, 30)
    bridge.shellEvent.emit('wallpaper-switcher')
    for mode in ('dashboard', 'media', 'performance', 'access'):
        open_mode(root, mode)
        pump(qapp, 20)
    open_mode(root, 'close')
    pump(qapp)
    assert not fautes(messages, debut), fautes(messages, debut)


@pytest.mark.parametrize(
    'fichier', ('qs/modules/dashboard/Workspaces.qml', 'WorkspaceBrowser.qml', 'ThemePreviewMock.qml', 'CustomThemeEditor.qml')
)
def test_module_roots_instantiate(qapp, shell, bridge, fichier):
    _, messages = shell
    engine = bridge.engine
    debut = len(messages)
    comp = QQmlComponent(engine, QUrl.fromLocalFile(str(FRONT / 'qml' / fichier)))
    obj = comp.create()
    pump(qapp, 20)
    erreurs = [e.toString() for e in comp.errors()]
    assert obj is not None and not erreurs, erreurs
    assert not fautes(messages, debut), fautes(messages, debut)
    obj.deleteLater()
    pump(qapp)


def test_message_handler_catches_runtime_faults(qapp, shell, bridge):
    # Garde-fou : prouve que le filtre voit bien une vraie ReferenceError (sinon tous les tests ci-dessus seraient aveugles).
    _, messages = shell
    debut = len(messages)
    comp = QQmlComponent(bridge.engine)
    comp.setData(b'import QtQuick\nItem { Component.onCompleted: console.warn(inexistant.x) }', QUrl('file:///smoke.qml'))
    obj = comp.create()
    pump(qapp)
    assert fautes(messages, debut)
    obj.deleteLater()
