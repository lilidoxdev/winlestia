"""Pastille de batterie des périphériques : rendu QML hors écran avec un faux état, plus garde-fou sur les couleurs en dur."""
import json
import os
import re
import subprocess
import sys
from pathlib import Path

import pytest

root = Path(__file__).resolve().parents[1]
qml = root / 'src' / 'frontend' / 'qml'

SCRIPT = r"""
import os, sys, json, tempfile
from pathlib import Path
from types import SimpleNamespace
root = Path(sys.argv[1])
sys.path.insert(0, str(root / 'src/frontend'))
os.environ['QT_QPA_PLATFORM'] = 'offscreen'
os.environ['QT_QUICK_BACKEND'] = 'software'
from PySide6.QtCore import QObject, QUrl
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtQuick import QQuickView
from frontend import Bridge, ROOT
app = QApplication([])
bridge = Bridge(SimpleNamespace(pipe='PanneauxPeripheralTest', backend_pid=0, preview=False))
bridge.pointer.stop()
def dev(i, pct, charging=False, hidden=False, approx=False):
    return {'id': i, 'name': i, 'kind': 'headset', 'percent': pct, 'charging': charging, 'approximate': approx, 'hidden': hidden,
        'source': 'bluetooth'}
def state(on, devices, **extra):
    s = {'settings': {'themeMode': 'custom', 'customPalette': {'background': '#0e1a1a', 'text': '#dff2f0', 'primary': '#2fbfb0',
         'secondary': '#5a8f8a', 'tertiary': '#7aa0c0'}, 'peripheralBattery': on}, 'dark': True}
    if devices is not None:
        s['peripherals'] = {'enabled': on, 'devices': devices, 'paired': []}
    bridge.update_state({'state': s})
engine = QQmlApplicationEngine()
engine.addImportPath(str(ROOT / 'qml'))
engine.rootContext().setContextProperty('Bridge', bridge)
issues = []
from PySide6.QtCore import qInstallMessageHandler
qInstallMessageHandler(lambda k, c, m: issues.append(m))
tmp = Path(tempfile.mkdtemp()) / 'Probe.qml'
tmp.write_text('import QtQuick\nimport qs.services\nimport "%s"\nItem {\n'
           ' Rectangle { objectName: "errSwatch"; color: Colours.palette.m3error }\n'
           ' Rectangle { objectName: "priSwatch"; color: Colours.palette.m3primary }\n'
           ' PeripheralBatteryPill {}\n}\n' % (ROOT / 'qml').as_uri(), encoding='utf-8')
view = QQuickView(engine, None)
view.setSource(QUrl.fromLocalFile(str(tmp)))
view.show()
def settle():
    for _ in range(5):
        app.processEvents()
assert view.rootObject(), [e.toString() for e in view.errors()]
top = view.rootObject()
def items(item):
    yield item
    for child in item.childItems():
        yield from items(child)
pill = next(c for c in items(top) if c.objectName() == 'dockPeripherals')
def groups():
    return len([c for c in items(pill) if c.objectName() == 'dockPeripheralGroup'])
out = {}
state(False, [dev('a', 70)]); settle(); out['disabled'] = [pill.isVisible(), groups()]
state(True, None); settle(); out['missing'] = [pill.isVisible(), groups()]
state(True, []); settle(); out['empty'] = [pill.isVisible(), groups()]
state(True, [dev('a', 70, hidden=True), dev('b', 50, hidden=True)]); settle(); out['allHidden'] = [pill.isVisible(), groups()]
state(True, [dev('a', 70), dev('b', 15), dev('c', 10, charging=True), dev('d', 40, hidden=True)]); settle()
out['shown'] = [pill.isVisible(), groups()]
fills = {}
for c in items(pill):
    if c.objectName() == 'dockPeripheralFill':
        fills[len(fills)] = c.property('color').name()
out['fills'] = fills
state(True, [dev('a', 75, approx=True), dev('b', -75), dev('c', 255), dev('d', None)]); settle()
out['bounded'] = [pill.isVisible(), groups()]
out['texts'] = [c.property('text') for c in items(pill) if c.property('text') and '%' in str(c.property('text'))]
"""


def test_pill_offscreen():
    pytest.importorskip('PySide6', reason='PySide6 is required (pip install -r src/frontend/requirements.txt)')
    script = SCRIPT.replace('from qml_colours import palette_colours  # noqa\n', '')
    script += """
out['err'] = next(c for c in items(top) if c.objectName() == 'errSwatch').property('color').name()
out['pri'] = next(c for c in items(top) if c.objectName() == 'priSwatch').property('color').name()
out['issues'] = [m for m in issues if 'QFontDatabase' not in m and 'plugin does not support' not in m]
print('RESULT' + json.dumps(out), flush=True)
bridge.stop()
os._exit(0)
"""
    result = subprocess.run([sys.executable, '-c', script, str(root)], capture_output=True, timeout=60, cwd=root, text=True)
    line = next((x for x in result.stdout.splitlines() if x.startswith('RESULT')), None)
    assert line, f'probe produced no result (exit {result.returncode}): {result.stderr[-500:]!r}'
    out = json.loads(line[6:])
    assert out['disabled'][0] is False and out['disabled'][1] == 0, 'pill must be hidden when the setting is off'
    assert out['missing'][0] is False, 'pill must be hidden without Bridge.peripherals'
    assert out['empty'][0] is False, 'pill must be hidden with no device'
    assert out['allHidden'][0] is False, 'pill must be hidden when every device is hidden'
    assert out['shown'] == [True, 3], 'one group per non-hidden device'
    fills = [out['fills'][str(i)] for i in range(3)]
    primary, low, charging = fills
    assert (primary, low, charging) == (out['pri'], out['err'], out['pri']), 'low battery must use m3error, others m3primary'
    assert out['bounded'] == [True, 1], 'percent outside 0..100 or missing must be treated as unknown and not shown'
    assert out['texts'] == ['75 %'], f'approximate level must read "75 %", never "~75"/"-75": {out["texts"]}'
    assert not out['issues'], f'QML issues: {out["issues"]}'


def test_no_hardcoded_colours_in_new_qml():
    for path in (qml / 'PeripheralBatteryPill.qml',):
        text = path.read_text(encoding='utf-8')
        assert not re.search(r'#[0-9a-fA-F]{3,8}\b', text), f'hard-coded colour in {path.name}'
        assert 'Qt.rgba' not in text, f'Qt.rgba in {path.name}'
    page = (qml / 'settings' / 'PageDocks.qml').read_text(encoding='utf-8')
    section = page[page.index('rowId: "peripheralBattery"'):]
    assert not re.search(r'#[0-9a-fA-F]{3,8}\b', section) and 'Qt.rgba' not in section
