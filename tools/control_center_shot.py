# Python 3.12 / control_center_shot.py / Rendu hors écran du Centre de contrôle (page principale, Wi-Fi, Bluetooth, Son).
# usage: python control_center_shot.py [OUTDIR]   (défaut : build/probes/control-center)
# Le Bridge est le vrai (état synthétique, pipe isolé) ; la palette passe par le mode personnalisé du Bridge.
import os, sys, json, tempfile
from pathlib import Path
from types import SimpleNamespace

root = Path(__file__).resolve().parents[1]
front = Path(os.environ.get('WINLESTIA_FRONTEND', root / 'src/frontend'))
sys.path.insert(0, str(front))
os.environ['QT_QPA_PLATFORM'] = 'offscreen'
os.environ.setdefault('QT_QUICK_BACKEND', 'software')
import threading

threading.Timer(90, lambda: os._exit(3)).start()
from PySide6.QtCore import QUrl, QTimer, qInstallMessageHandler
from PySide6.QtGui import QFontDatabase
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtQuick import QQuickView
from frontend import Bridge, ROOT

PALETTE = {
    'background': '#0e1a1a',
    'text': '#dff2f0',
    'primary': '#2fbfb0',
    'secondary': '#5a8f8a',
    'tertiary': '#7aa0c0',
}
NETWORKS = [
    {'ssid': 'Freebox-4F2A', 'signal': 82, 'bars': 4, 'security': 'wpa2', 'secure': True, 'connected': True, 'saved': True,
        'supported': True},
    {'ssid': 'SFR_WiFi_B3C1', 'signal': 70, 'bars': 3, 'security': 'wpa2', 'secure': True, 'connected': False, 'saved': True,
        'supported': True},
    {'ssid': 'Orange-7721', 'signal': 22, 'bars': 1, 'security': 'wpa2', 'secure': True, 'connected': False, 'saved': False,
        'supported': True},
    {'ssid': 'Cafe Public', 'signal': 90, 'bars': 4, 'security': 'open', 'secure': False, 'connected': False, 'saved': False,
        'supported': True},
]
PAIRED = [
    {'id': 'h1', 'name': 'Casque WH-1000XM4', 'kind': 'headset', 'connected': True, 'percent': 85, 'canConnect': True},
    {'id': 'm1', 'name': 'Souris MX Master', 'kind': 'mouse', 'connected': True, 'percent': 40, 'canConnect': False},
    {'id': 'g1', 'name': 'Manette Xbox', 'kind': 'gamepad', 'connected': False, 'percent': None, 'canConnect': True},
    {'id': 'system:battery', 'name': '', 'kind': 'laptop', 'connected': True, 'percent': 42},
]
DEVICES = [
    {'id': 'o1', 'name': 'Haut-parleurs · Realtek Audio', 'selected': True, 'volume': 72, 'muted': False, 'flow': 0},
    {'id': 'o2', 'name': 'Casque Bluetooth WH-1000XM4', 'selected': False, 'volume': 50, 'muted': False, 'flow': 0},
    {'id': 'o3', 'name': 'Écran HDMI NVIDIA', 'selected': False, 'volume': 100, 'muted': False, 'flow': 0},
    {'id': 'i1', 'name': 'Microphone · Realtek Audio', 'selected': True, 'volume': 80, 'muted': False, 'flow': 1},
    {'id': 'i2', 'name': 'Micro casque', 'selected': False, 'volume': 60, 'muted': False, 'flow': 1},
]
SESSIONS = [
    {'id': 'spotify', 'name': 'Spotify', 'pid': 10, 'volume': 42, 'muted': False, 'playing': True},
    {'id': 'edge', 'name': 'Microsoft Edge', 'pid': 20, 'volume': 66, 'muted': False, 'playing': True},
    {'id': 'system', 'name': 'Sons système', 'pid': 0, 'volume': 30, 'muted': True, 'playing': False},
]
STATE = {
    'settings': {'themeMode': 'custom', 'customPalette': PALETTE, 'reducedEffects': True, 'peripheralBattery': True},
    'dark': True,
    'controls': {
        'volume': 72,
        'muted': False,
        'network': 'Freebox-4F2A',
        'wifiOn': True,
        'bluetoothOn': True,
        'displays': [{'id': 1, 'name': 'DELL U2723QE', 'value': 63, 'available': True}],
        'wifi': {'state': 'ok', 'connecting': False, 'limited': False, 'networks': NETWORKS},
    },
    'peripherals': {'enabled': True, 'devices': [], 'paired': PAIRED, 'busy': ''},
    'audioMixer': {'devices': DEVICES, 'applications': SESSIONS, 'running': True, 'ready': True},
}
HARNESS = """
import QtQuick
import qs.services
import "%(qml)s"
Rectangle {
    width: 450
    height: panel.implicitHeight + 36
    color: Colours.palette.m3surface
    WindowsControls {
        id: panel
        x: 20
        y: 18
        width: 410
        page: "%(page)s"
        warmed: true
    }
}
"""


def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else root / 'build/probes/control-center'
    out.mkdir(parents=True, exist_ok=True)
    app = QApplication([])
    for font in (ROOT / 'assets/fonts').glob('*.ttf'):
        QFontDatabase.addApplicationFont(str(font))
    issues = []
    qInstallMessageHandler(lambda kind, context, message: issues.append(message))
    bridge = Bridge(SimpleNamespace(pipe='PanneauxControlCenterShot', backend_pid=0, preview=False))
    bridge.pointer.stop()
    bridge.update_state({'state': STATE})
    engine = QQmlApplicationEngine()
    engine.addImportPath(str(ROOT / 'qml'))
    engine.rootContext().setContextProperty('Bridge', bridge)
    shots = []
    for page, file in (('main', 'main'), ('wifi', 'wifi'), ('bluetooth', 'bluetooth'), ('sound', 'sound')):
        view = QQuickView(engine, None)
        view.setResizeMode(QQuickView.SizeViewToRootObject)
        tmp = Path(tempfile.mkdtemp()) / ('Harness_' + page + '.qml')
        tmp.write_text(HARNESS % {'qml': ROOT.joinpath('qml').as_uri(), 'page': page}, encoding='utf-8')
        view.setSource(QUrl.fromLocalFile(str(tmp)))
        view.show()
        shots.append((view, tmp, out / (file + '.png')))
    done = []

    def finish():
        for view, tmp, target in shots:
            view.grabWindow().save(str(target))
            tmp.unlink(missing_ok=True)
            tmp.parent.rmdir()
            done.append(str(target))
        errors = [x for x in issues if 'plugin does not support' not in x and 'QFontDatabase' not in x and 'OpenThemeData' not in x]
        print(json.dumps({'saved': done, 'qmlIssues': errors}, ensure_ascii=False))
        bridge.stop()
        app.quit()

    QTimer.singleShot(2500, finish)
    app.exec()


main()
