# Python 3.12 / peripheral_pill_shot.py / Rendu hors écran de la pastille de batterie des périphériques et de sa section de réglages.
# usage: python peripheral_pill_shot.py [teal|red] [OUTDIR]
#   teal (sombre) écrit docs/media/peripheral-battery{,-settings}.png ; red (clair) écrit build/probes/*-red.png.
# Le Bridge est le vrai (état synthétique, pipe isolé) ; la palette passe par le mode personnalisé du Bridge, comme prefs_shots.py.
# %LOCALAPPDATA%\Panneaux\palette-shell.json n'est que lu : hors mode auto il ne contient pas de palette ({"mode":"custom"}).
import os, sys, json, tempfile
from pathlib import Path
from types import SimpleNamespace

root = Path(__file__).resolve().parents[1]
front = Path(os.environ.get('WINLESTIA_FRONTEND', root / 'src/frontend'))
sys.path.insert(0, str(front))
os.environ['QT_QPA_PLATFORM'] = 'offscreen'
os.environ.setdefault('QT_QUICK_BACKEND', 'software')
import threading

threading.Timer(60, lambda: os._exit(3)).start()
from PySide6.QtCore import QUrl, QTimer, qInstallMessageHandler
from PySide6.QtGui import QFontDatabase
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
from frontend import Bridge, ROOT

PAL = {
    'red': {
        'background': '#fff8f6',
        'text': '#261916',
        'primary': '#c0152f',
        'secondary': '#8a4a50',
        'tertiary': '#7a5a30',
    },
    'teal': {
        'background': '#0e1a1a',
        'text': '#dff2f0',
        'primary': '#2fbfb0',
        'secondary': '#5a8f8a',
        'tertiary': '#7aa0c0',
    },
}
DEVICES = [
    {'id': 'h1', 'name': 'Casque WH-1000XM4', 'kind': 'headset', 'percent': 70, 'charging': False, 'approximate': False, 'hidden': False,
        'source': 'bluetooth'},
    {'id': 'h2', 'name': 'Casque Arctis', 'kind': 'headset', 'percent': 50, 'charging': False, 'approximate': False, 'hidden': False,
        'source': 'ble'},
    {'id': 'g1', 'name': 'Manette Xbox', 'kind': 'gamepad', 'percent': 15, 'charging': False, 'approximate': False, 'hidden': False,
        'source': 'xinput'},
    {'id': 'm1', 'name': 'Souris MX Master', 'kind': 'mouse', 'percent': 60, 'charging': True, 'approximate': True, 'hidden': False,
        'source': 'ble'},
    {'id': 'system:battery', 'name': '', 'kind': 'laptop', 'percent': 42, 'charging': True, 'approximate': False, 'hidden': False,
        'source': 'system'},
]
PAIRED = [
    {'id': 'h1', 'name': 'Casque WH-1000XM4', 'kind': 'headset', 'detectedKind': 'headset', 'overridden': False, 'connected': True,
        'percent': 70},
    {'id': 'g1', 'name': 'Manette Xbox', 'kind': 'gamepad', 'detectedKind': 'other', 'overridden': True, 'connected': True, 'percent': 15},
    {'id': 'k1', 'name': 'Clavier K380', 'kind': 'keyboard', 'detectedKind': 'keyboard', 'overridden': False, 'connected': False,
        'percent': None},
    {'id': 'system:battery', 'name': '', 'kind': 'laptop', 'detectedKind': 'laptop', 'overridden': False, 'connected': True, 'percent': 42},
]
PILL = """
import QtQuick
import qs.services
import qs.config
import qs.components
import "%(qml)s"
Rectangle {
    width: row.implicitWidth + 48
    height: 64
    color: Colours.palette.m3surface
    Row {
        id: row
        anchors.centerIn: parent
        spacing: 12
        PeripheralBatteryPill {}
        Column {
            StyledText { text: "21:45"; font.pointSize: Appearance.font.size.small }
            StyledText { text: "17 nov"; font.pointSize: Appearance.font.size.small; color: Colours.palette.m3onSurfaceVariant }
        }
    }
}
"""
SETTINGS = """
import QtQuick
import qs.services
import "%(qml)s/settings"
Rectangle {
    width: 760
    height: page.implicitHeight + 24
    color: Colours.palette.m3surface
    PageDocks {
        id: page
        x: 12
        y: 12
        width: 736
    }
}
"""


def main():
    name = sys.argv[1] if len(sys.argv) > 1 and sys.argv[1] in PAL else 'teal'
    default = name == 'teal'
    out = Path(sys.argv[2]) if len(sys.argv) > 2 else root / ('docs/media' if default else 'build/probes')
    out.mkdir(parents=True, exist_ok=True)
    suffix = '' if default else '-' + name
    app = QApplication([])
    for font in (ROOT / 'assets/fonts').glob('*.ttf'):
        QFontDatabase.addApplicationFont(str(font))
    issues = []
    qInstallMessageHandler(lambda kind, context, message: issues.append(message))
    bridge = Bridge(SimpleNamespace(pipe='PanneauxPeripheralShot', backend_pid=0, preview=False))
    bridge.pointer.stop()
    bridge.update_state(
        {
            'state': {
                'settings': {
                    'themeMode': 'custom',
                    'customPalette': PAL[name],
                    'reducedEffects': True,
                    'peripheralBattery': True,
                },
                'dark': name == 'teal',
                'peripherals': {'enabled': True, 'devices': DEVICES, 'paired': PAIRED},
            }
        }
    )
    engine = QQmlApplicationEngine()
    engine.addImportPath(str(ROOT / 'qml'))
    engine.rootContext().setContextProperty('Bridge', bridge)
    shots = []
    for file, src in ((f'peripheral-battery{suffix}.png', PILL), (f'peripheral-battery-settings{suffix}.png', SETTINGS)):
        qml = ROOT.joinpath('qml').as_uri()
        from PySide6.QtQml import QQmlComponent
        from PySide6.QtQuick import QQuickView

        view = QQuickView(engine, None)
        view.setResizeMode(QQuickView.SizeViewToRootObject)
        data = src % {'qml': qml}
        tmp = Path(tempfile.mkdtemp()) / ('Harness' + str(len(shots)) + '.qml')
        tmp.write_text(data, encoding='utf-8')
        view.setSource(QUrl.fromLocalFile(str(tmp)))
        view.show()
        shots.append((view, tmp, out / file))
    done = []

    def finish():
        for view, tmp, target in shots:
            image = view.grabWindow()
            image = image.scaled(image.width() * 2, image.height() * 2) if 'settings' not in target.name else image
            image.save(str(target))
            tmp.unlink(missing_ok=True)
            tmp.parent.rmdir()
            done.append(str(target))
        errors = [x for x in issues if 'plugin does not support' not in x and 'QFontDatabase' not in x]
        print(json.dumps({'saved': done, 'qmlIssues': errors}, ensure_ascii=False))
        bridge.stop()
        app.quit()

    QTimer.singleShot(1500, finish)
    app.exec()


main()
