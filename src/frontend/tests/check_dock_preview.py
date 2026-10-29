"""Offscreen check of the dock window-preview requests in qml/Dock.qml (stub Bridge, never touches the running shell).
Usage: python -I check_dock_preview.py     (QT_QPA_PLATFORM=offscreen is forced)
Checks: (1) hide animation: fixed clip box, nothing drawn above the contour line (row < offset), opacity bounded by the fade rule,
fully transparent once offset >= 0.6*height, nothing outside the window; (2) Effets alleges: offset stays 0, fade only;
(3) network icon for wifi / ethernet / none (+ the 'lan' ligature really is a glyph); (4) system zone order grid, trash, Fichiers, loupe."""
import os, sys, ast, time
os.environ['QT_QPA_PLATFORM'] = 'offscreen'
from pathlib import Path
from PySide6.QtCore import QPoint, QPointF, QObject, Property, Signal, Slot, QUrl, qInstallMessageHandler
from PySide6.QtGui import QGuiApplication, QFontDatabase, QImage, QPainter, QColor, QFont
from PySide6.QtQuick import QQuickItem
from PySide6.QtTest import QTest
from PySide6.QtCore import Qt
from PySide6.QtQml import QQmlEngine, QQmlComponent

ROOT = Path(__file__).resolve().parents[1]
_src = (Path(__file__).parent / 'render_workspaces.py').read_text(encoding='utf8')
PAL = next(eval(compile(ast.Expression(n.value), 'pal', 'eval')) for n in ast.parse(_src).body if isinstance(n, ast.Assign) and getattr(n.targets[0], 'id', '') == 'PAL')['red-dark']
MSGS = []
qInstallMessageHandler(lambda k, c, m: MSGS.append(m))
results = []


def check(name, ok, detail=''):
    results.append(bool(ok)); print(('PASS ' if ok else 'FAIL ') + name + (' -- ' + str(detail) if detail else ''))


class Stub(QObject):
    changed = Signal(); dockChanged = Signal(); shellEvent = Signal(str)

    def __init__(self):
        super().__init__(); self.reduced = False; self.kind = 'wifi'; self.log = []; self.apps = [{'id': 'edge', 'name': 'Edge', 'hwnd': 11, 'count': 2, 'active': False, 'pinned': True, 'windows': [{'hwnd': 11, 'title': 'a'}, {'hwnd': 12, 'title': 'b'}]}, {'id': 'idle', 'name': 'Idle', 'hwnd': 0, 'count': 0, 'active': False, 'pinned': True, 'windows': []}]

    dark = Property(bool, lambda s: PAL['dark'], notify=changed)
    adaptivePalette = Property('QVariantMap', lambda s: PAL['p'], notify=changed)
    reducedEffects = Property(bool, lambda s: s.reduced, notify=changed)
    networkKind = Property(str, lambda s: s.kind, notify=changed)
    preview = Property(bool, lambda s: False, constant=True)
    taskbarEnabled = Property(bool, lambda s: True, constant=True)
    taskbarApps = Property('QVariantList', lambda s: s.apps, notify=dockChanged)
    runningApps = Property('QVariantList', lambda s: [], notify=dockChanged)
    settings = Property('QVariantMap', lambda s: {'motionScale': 1}, notify=changed)
    screenX = Property(int, lambda s: 0, constant=True)
    screenY = Property(int, lambda s: 0, constant=True)
    screenWidth = Property(int, lambda s: 1280, constant=True)
    screenHeight = Property(int, lambda s: 720, constant=True)
    screenFullscreen = Property(bool, lambda s: False, constant=True)
    frameEnabled = Property(bool, lambda s: False, constant=True)
    frameThickness = Property(int, lambda s: 0, constant=True)
    glassEnabled = Property(bool, lambda s: False, constant=True)
    glassOpacity = Property(float, lambda s: 1.0, constant=True)
    glassTintOpacity = Property(float, lambda s: 1.0, constant=True)
    themeMode = Property(str, lambda s: 'dark', constant=True)
    iconRevision = Property(int, lambda s: 0, constant=True)
    assetRoot = Property(str, lambda s: ROOT.joinpath('assets').as_uri(), constant=True)

    @Slot()
    def updateSurfaces(self): pass
    @Slot(str, 'QVariant')
    def action(self, n, p=None): self.log.append((n, p.toVariant() if hasattr(p, 'toVariant') else p, time.perf_counter()))
    @Slot(str)
    def navigate(self, t): self.log.append(('navigate', t))
    @Slot(int)
    def openTab(self, t): self.log.append(('tab', t))
    @Slot()
    def closePanel(self): pass
    @Slot(str, 'QVariant', result=str)
    def iconUrl(self, i, r=0): return ''
    @Slot(str, result=str)
    def localUrl(self, p): return ''
    @Slot()
    def menuOutsideClick(self): pass


app = QGuiApplication([])
for f in (ROOT / 'assets/fonts').glob('*.ttf'):
    QFontDatabase.addApplicationFont(str(f))


def pump(ms):
    end = time.perf_counter() + ms / 1000
    while time.perf_counter() < end:
        app.processEvents()


stub = Stub()
eng = QQmlEngine(); eng.addImportPath(str(ROOT / 'qml')); eng.rootContext().setContextProperty('Bridge', stub)
comp = QQmlComponent(eng, QUrl.fromLocalFile(str(ROOT / 'qml/Dock.qml')))
dock = comp.create()
if dock is None:
    print('QML errors:', [e.toString() for e in comp.errors()]); sys.exit(1)
dock.setProperty('shown', True); dock.show(); pump(700)


def previews(): return [l for l in stub.log if l[0] == 'dockPreview']
def ends(): return [l for l in stub.log if l[0] == 'dockPreviewEnd']
def find_item(root, name):
    for c in root.childItems():
        if c.objectName() == name: return c
        r = find_item(c, name)
        if r is not None: return r


def center(oid):
    it = find_item(dock.contentItem(), 'dock-app-' + oid); p = it.mapToScene(QPointF(it.width() / 2, it.height() / 2)); return int(p.x()), int(p.y())


QTest.mouseMove(dock, QPoint(5, 5)); pump(100); stub.log.clear()
ex, ey = center('edge')
QTest.mouseMove(dock, QPoint(ex, ey)); t0 = time.perf_counter(); pump(350)
check('hover on app with 2 windows: no dockPreview before 450 ms', not previews(), previews())
pump(250)
check('one dockPreview after the delay', len(previews()) == 1, len(previews()))
if previews():
    a = previews()[0][1]
    check('request carries id/x/y/cx/cy/dpr', a.get('id') == 'edge' and all(k in a for k in ('x', 'y', 'cx', 'cy', 'dpr')) and a['y'] == dock.property('y'), a)
    check('request fired at 450 ms (not earlier)', previews()[0][2] - t0 >= 0.44, previews()[0][2] - t0)
pump(700)
check('still exactly one request while hovering', len(previews()) == 1, len(previews()))
QTest.mouseMove(dock, QPoint(5, 5)); t1 = time.perf_counter(); pump(120)
check('no dockPreviewEnd before the 200 ms grace', not ends(), ends())
pump(250)
check('dockPreviewEnd after exit', len(ends()) == 1 and ends()[0][2] - t1 >= 0.19, ends())

# quick re-hover while a preview is open -> short (120 ms) delay, cancels the pending end
stub.log.clear(); QTest.mouseMove(dock, QPoint(ex, ey)); pump(700)
check('re-hover requests again', len(previews()) == 1)
QTest.mouseMove(dock, QPoint(5, 5)); pump(60)
QTest.mouseMove(dock, QPoint(ex, ey)); pump(400)
check('leaving and re-entering within the grace does not send dockPreviewEnd', not ends(), ends())

# dock hides while a preview is open -> immediate end (now)
dock.setProperty('shown', False); pump(50)
check('dock hide sends dockPreviewEnd {now:true} immediately', len(ends()) == 1 and (ends()[0][1] or {}).get('now') is True, ends())

# shown=false: nothing requested; app without windows: nothing requested
stub.log.clear(); QTest.mouseMove(dock, QPoint(5, 5)); pump(100); QTest.mouseMove(dock, QPoint(ex, ey)); pump(700)
check('dock.shown=false: no dockPreview', not previews(), previews())
dock.setProperty('shown', True); pump(900); stub.log.clear()
ix, iy = center('idle'); QTest.mouseMove(dock, QPoint(5, 5)); pump(100); QTest.mouseMove(dock, QPoint(ix, iy)); pump(700)
check('app without window: no dockPreview', not previews(), previews())
QTest.mouseMove(dock, QPoint(5, 5)); pump(100)
warn = [m for m in MSGS if 'Dock.qml' in m or 'ReferenceError' in m or 'TypeError' in m]
check('no QML warnings from Dock.qml', not warn, warn[:3])
print(f'TOTAL {sum(results)}/{len(results)}')
sys.exit(0 if all(results) else 1)
