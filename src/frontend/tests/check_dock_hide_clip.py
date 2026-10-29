"""Offscreen check of qml/Dock.qml (stub Bridge, never touches the running shell or the real session).
Usage: python -I check_dock_hide_clip.py [OUT_DIR]     (QT_QPA_PLATFORM=offscreen is forced)
Checks: (1) hide animation: fixed clip box, nothing drawn above the contour line (row < offset), opacity bounded by the fade rule,
fully transparent once offset >= 0.6*height, nothing outside the window; (2) Effets alleges: offset stays 0, fade only;
(3) network icon for wifi / ethernet / none (+ the 'lan' ligature really is a glyph); (4) system zone order grid, trash, Fichiers, loupe."""
import os, sys, ast, time
os.environ['QT_QPA_PLATFORM'] = 'offscreen'
from pathlib import Path
from PySide6.QtCore import QPointF, QObject, Property, Signal, Slot, QUrl, qInstallMessageHandler
from PySide6.QtGui import QGuiApplication, QFontDatabase, QImage, QPainter, QColor, QFont
from PySide6.QtQuick import QQuickItem
from PySide6.QtQml import QQmlEngine, QQmlComponent

ROOT = Path(__file__).resolve().parents[1]
OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(os.environ.get('TEMP', '.')) / 'dock-test'
OUT.mkdir(parents=True, exist_ok=True)
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
        super().__init__(); self.reduced = False; self.kind = 'wifi'; self.log = []

    dark = Property(bool, lambda s: PAL['dark'], notify=changed)
    adaptivePalette = Property('QVariantMap', lambda s: PAL['p'], notify=changed)
    reducedEffects = Property(bool, lambda s: s.reduced, notify=changed)
    networkKind = Property(str, lambda s: s.kind, notify=changed)
    preview = Property(bool, lambda s: False, constant=True)
    taskbarEnabled = Property(bool, lambda s: True, constant=True)
    taskbarApps = Property('QVariantList', lambda s: [], notify=dockChanged)
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
    def action(self, n, p=None): self.log.append(('action', n))
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
dock.setProperty('shown', True); dock.show(); pump(600)
H = int(dock.property('height'))


def grab(name=None):
    img = dock.grabWindow().convertToFormat(QImage.Format_ARGB32)
    if name: img.save(str(OUT / name))
    return img


def alpha_rows(img):
    """(max alpha overall, first row holding alpha>8, last such row)"""
    mx, first, last = 0, -1, -1
    for y in range(img.height()):
        row = max(img.pixelColor(x, y).alpha() for x in range(0, img.width(), 2))
        if row > 8:
            if first < 0: first = y
            last = y
        mx = max(mx, row)
    return mx, first, last


def child(name): return dock.findChild(QQuickItem, name)


def sceneY(o): return o.mapToScene(QPointF(0, 0)).y()


def sceneX(o): return o.mapToScene(QPointF(0, 0)).x()


# --- (4) system zone order + (3) network icon
names = ['dockStart', 'dockRecycle', 'dockFiles', 'dockSearch']
items = [child(n) for n in names]
xs = [sceneX(o) for o in items if o is not None]
check('system zone order grid < trash < Fichiers < loupe', all(o is not None for o in items) and xs == sorted(xs) and len(set(xs)) == 4, xs)
check('Fichiers icon is "folder"', child('dockFiles').property('icon') == 'folder')
grab('order.png')
for kind, icon in (('wifi', 'wifi'), ('ethernet', 'lan'), ('none', 'wifi_off')):
    stub.kind = kind; stub.changed.emit(); pump(150)
    check(f'networkKind={kind} -> icon {icon}', child('dockNetwork').property('icon') == icon, child('dockNetwork').property('icon'))
    grab(f'network-{kind}.png')


def glyph_width(txt):
    img = QImage(600, 80, QImage.Format_ARGB32); img.fill(0)
    f = QFont('Material Symbols Rounded'); f.setPixelSize(40)
    p = QPainter(img); p.setFont(f); r = p.boundingRect(0, 0, 600, 80, 0, txt); p.end(); return r.width()


check('"lan" is a ligature in the bundled font (one glyph wide, not 3 letters)', glyph_width('lan') < 60, glyph_width('lan'))
stub.kind = 'wifi'; stub.changed.emit()

# --- (1) hide animation, sampled every frame
clip = child('dockClip')
samples = []
dock.setProperty('shown', False)
t0 = time.perf_counter()
while dock.property('visible') and time.perf_counter() - t0 < 1.5 and len(samples) < 400:
    app.processEvents()
    off = float(dock.property('offset')); op = float(dock.property('contentOpacity'))
    mx, first, last = alpha_rows(grab())
    samples.append((off, op, mx, first, last, sceneY(clip), clip.height()))
check('hide animation ran and the window ended hidden', len(samples) > 3 and not dock.property('visible'), len(samples))
check('opacity never exceeds the fade rule clamp(1 - offset/(0.6*height))', not [s for s in samples if s[2] > s[1] * 255 + 4], [s for s in samples if s[2] > s[1] * 255 + 4][:2])
check('nothing drawn above the contour line (row < offset)', not [s for s in samples if 0 <= s[3] < s[0] - 1], [s for s in samples if 0 <= s[3] < s[0] - 1][:2])
check('fully transparent once offset >= 0.6*height', not [s for s in samples if s[0] >= 0.6 * H and s[2] > 8], [s for s in samples if s[0] >= 0.6 * H and s[2] > 8][:2])
check('clip box is fixed (never translated) and equals the window bounds', all(s[5] == 0 and s[6] == H for s in samples))
check('no drawn row at or past the window bottom', all(s[4] < H for s in samples))

# --- PNGs at about 0 / 25 / 50 / 75 % of the slide
for pct in (0, 25, 50, 75):
    dock.setProperty('shown', True); pump(900)
    dock.setProperty('shown', False)
    target = H * pct / 100.0
    best = None; img = None; t0 = time.perf_counter()
    while time.perf_counter() - t0 < 1.5:
        app.processEvents()
        o = float(dock.property('offset'))
        if best is None or abs(o - target) < abs(best - target):
            best = o; img = grab()
        if o >= target: break
    img.save(str(OUT / f'hide-{pct:02d}.png'))
    print(f'  hide-{pct:02d}.png captured at offset={best:.1f}/{H} opacity={float(dock.property("contentOpacity")):.2f}')
dock.setProperty('shown', True); pump(900)

# --- (2) Effets alleges: no slide, short fade
stub.reduced = True; stub.changed.emit(); pump(200)
offs, ops = [], []
dock.setProperty('shown', False); t0 = time.perf_counter()
while dock.property('visible') and time.perf_counter() - t0 < 1.5:
    app.processEvents(); offs.append(float(dock.property('offset'))); ops.append(float(dock.property('contentOpacity')))
check('reduced: no slide (offset stays 0)', len(offs) > 2 and max(offs) == 0, (len(offs), max(offs) if offs else None))
check('reduced: opacity fades down monotonically', ops and ops[-1] < 0.1 and all(a >= b - 1e-6 for a, b in zip(ops, ops[1:])), (ops[:1], ops[-1:]))
check('reduced: window hidden at the end', not dock.property('visible'))
dock.setProperty('shown', True); pump(400)
check('reduced: shown again -> opaque and visible', dock.property('visible') and float(dock.property('contentOpacity')) > 0.99)
grab('reduced-shown.png')
# --- (3) toggling reducedEffects while hidden must never show the dock
dock.setProperty('shown', False); pump(600)
check('flip: hidden before test', not dock.property('visible'))
seen = False
for to in (False, True, False, True):
    stub.reduced = to; stub.changed.emit(); t0 = time.perf_counter()
    while time.perf_counter() - t0 < 0.4:
        app.processEvents(); seen = seen or bool(dock.property('visible'))
check('flip while hidden (reduced true<->false): visible stays false', not seen)
stub.reduced = False; stub.changed.emit(); pump(300)
dock.setProperty('shown', True); pump(900)
check('flip: shown afterwards -> visible, offset 0', dock.property('visible') and float(dock.property('offset')) == 0)
warn = [m for m in MSGS if 'Dock.qml' in m or 'ReferenceError' in m or 'TypeError' in m]
check('no QML warnings from Dock.qml', not warn, warn[:3])
print(f'TOTAL {sum(results)}/{len(results)}  PNGs in {OUT}')
sys.exit(0 if all(results) else 1)
