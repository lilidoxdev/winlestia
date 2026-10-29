"""Offscreen harness for the 'Tout ton espace' panel (WorkspaceBrowser) with a stub Bridge of ~372 entries.
Usage: python render_espace.py OUT_DIR PREFIX [bench]   (env ESPACE_REDUCED=1 for reduced effects)
Writes OUT_DIR/PREFIX-{red,teal,1366,search}.png, or prints open->first-frame timings with `bench`.
Hard timeout 120 s. Never touches the running app."""
import os, sys, time, statistics, random, ast
os.environ.setdefault('QT_QPA_PLATFORM', 'offscreen')
from pathlib import Path
from PySide6.QtCore import QObject, Property, Signal, Slot, QUrl, QTimer, qInstallMessageHandler
from PySide6.QtGui import QGuiApplication, QImage, QPainter, QColor, QFontDatabase
from PySide6.QtQuick import QQuickView
from PySide6.QtQml import QQmlEngine

ROOT = Path(__file__).resolve().parents[1]
_src = (Path(__file__).parent / 'render_workspaces.py').read_text(encoding='utf8')
PAL = next(eval(compile(ast.Expression(n.value), 'pal', 'eval')) for n in ast.parse(_src).body if isinstance(n, ast.Assign) and getattr(n.targets[0], 'id', '') == 'PAL')

ICON_DIR = Path(os.environ.get('ESPACE_ICONS', str(Path(__file__).parent / '_icons')))
CATS = ['3D et conception', 'Accès directs', 'Applications', 'Archives et outils', 'Audio, vidéo et médias', 'Bureautique', 'Communication', 'Développement', 'Documents', 'Jeux', 'Navigateurs', 'Réseau et sécurité', 'Système', 'Utilitaires', 'Graphisme', 'Études', 'Finance']
BOARDS = ['apps'] * 275 + ['games'] * 20 + ['projects'] * 41 + ['documents'] * 36
NAMES = ['Discord', 'Steam', 'VS Code', 'Chrome', 'Notes', 'Spotify', 'Blender', 'Photoshop', 'Firefox', 'OBS Studio', 'Git', 'Terminal', '7-Zip', 'Notepad++', 'Figma', 'Slack', 'Zoom', 'VLC', 'Paint', 'Calculatrice', 'Explorateur', 'Docker', 'Postman', 'Unity', 'Godot']
ICONS = []
MSGS = []


def make_icons():
    ICON_DIR.mkdir(parents=True, exist_ok=True)
    cols = ['#e5484d', '#f76b15', '#ffb224', '#30a46c', '#12a594', '#0090ff', '#6e56cf', '#d6409f', '#8d8d8d', '#00a2c7']
    for i, c in enumerate(cols):
        p = ICON_DIR / f'i{i}.png'
        if p.exists():
            continue
        im = QImage(96, 96, QImage.Format_ARGB32)
        im.fill(0)
        q = QPainter(im)
        q.setRenderHint(QPainter.Antialiasing)
        q.setBrush(QColor(c)); q.setPen(QColor(0, 0, 0, 0)); q.drawRoundedRect(6, 6, 84, 84, 20, 20)
        q.setBrush(QColor(255, 255, 255, 70)); q.drawEllipse(26, 26, 44, 44); q.end()
        im.save(str(p))
    return [QUrl.fromLocalFile(str(ICON_DIR / f'i{i}.png')).toString() for i in range(len(cols))]


def make_apps(n=372):
    rnd = random.Random(7)
    apps = []
    for i in range(n):
        base = NAMES[i % len(NAMES)]
        name = base if i < len(NAMES) else f'{base} {i // len(NAMES) + 1}'
        board = BOARDS[i] if i < len(BOARDS) else 'apps'
        apps.append({'id': f'e{i}', 'name': name, 'category': CATS[rnd.randrange(len(CATS))], 'board': board,
                     'favorite': i == 3, 'recent': (i if i < 12 else -1), 'kind': 'folder' if board in ('projects', 'documents') and i % 3 == 0 else 'app'})
    for a in apps:
        a['target'] = 'C:/Apps/' + a['id'] + '.exe'
    for a in apps[:6]:
        a['category'] = 'Windows et périphériques'
    # Duplicates (same target, empty category): the indexer must drop them (372 unique entries).
    for k in range(5):
        d = dict(apps[10 + k]); d['id'] = f'dup{k}'; d['category'] = ''; d['favorite'] = False; d['recent'] = -1; d['target'] = d['target'].upper(); apps.append(d)
    rnd.shuffle(apps)
    return apps


class Stub(QObject):
    changed = Signal(); catalogChanged = Signal(); navigationRequested = Signal(str)

    def __init__(self, pal):
        super().__init__()
        self._pal = pal; self._apps = make_apps(); self.log = []; self.icon_calls = 0; self.action_calls = 0
        self.reduced = os.environ.get('ESPACE_REDUCED') == '1'; self.h = 900

    dark = Property(bool, lambda s: s._pal['dark'], notify=changed)
    adaptivePalette = Property('QVariantMap', lambda s: s._pal['p'], notify=changed)
    apps = Property('QVariantList', lambda s: s._apps, notify=catalogChanged)
    iconRevision = Property(int, lambda s: 0, notify=changed)
    reducedEffects = Property(bool, lambda s: s.reduced, notify=changed)
    glassEnabled = Property(bool, lambda s: False, notify=changed)
    glassOpacity = Property(float, lambda s: 1.0, notify=changed)
    glassTintOpacity = Property(float, lambda s: 1.0, notify=changed)
    frameEnabled = Property(bool, lambda s: False, notify=changed)
    frameThickness = Property(int, lambda s: 0, notify=changed)
    themeMode = Property(str, lambda s: 'dark', notify=changed)
    assetRoot = Property(str, lambda s: ROOT.joinpath('assets').as_uri(), notify=changed)
    screenHeight = Property(int, lambda s: s.h, notify=changed)
    settings = Property('QVariantMap', lambda s: {'iconSize': 56, 'motionScale': 1}, notify=changed)

    @Slot(str, 'QVariant', result=str)
    def iconUrl(self, i, r=0):
        self.icon_calls += 1
        return ICONS[sum(map(ord, i)) % len(ICONS)]

    @Slot(str, 'QVariant')
    def action(self, n, p=None):
        self.action_calls += 1; self.log.append(('action', n))

    @Slot(str)
    def navigate(self, t): self.log.append(('navigate', t))

    @Slot()
    def closePanel(self): self.log.append(('close',))

    @Slot(str)
    def chooseEntryIcon(self, i): self.log.append(('chooseIcon', i))

    @Slot(str, result=str)
    def localUrl(self, p): return ''


def make_view(app, key, size=(1280, 820)):
    stub = Stub(PAL[key])
    eng = QQmlEngine(); eng.addImportPath(str(ROOT / 'qml')); eng.rootContext().setContextProperty('Bridge', stub)
    view = QQuickView(eng, None)
    view.setSource(QUrl.fromLocalFile(str(Path(__file__).parent / 'EspaceHarness.qml')))
    if view.status() != QQuickView.Ready:
        print('QML errors:', [e.toString() for e in view.errors()]); print(MSGS[-20:]); sys.exit(1)
    view.setResizeMode(QQuickView.SizeRootObjectToView); view.resize(*size); view.show()
    for _ in range(6): app.processEvents()
    pump(app, 1200)  # shell idle: lets one-shot preload timers fire, like a real session
    return view, stub, eng


def pump(app, ms):
    end = time.perf_counter() + ms / 1000
    while time.perf_counter() < end:
        app.processEvents()


def bench(app, view, runs=7):
    root = view.rootObject(); res = []
    for _ in range(runs):
        t0 = time.perf_counter(); root.setProperty('open', True); t1 = time.perf_counter()
        view.grabWindow(); t2 = time.perf_counter()
        res.append(((t1 - t0) * 1000, (t2 - t0) * 1000))
        pump(app, 1200)
        root.setProperty('open', False); pump(app, 700)
    return res


def shot(app, view, path, settle=1400):
    view.rootObject().setProperty('open', True); pump(app, settle)
    view.grabWindow().save(str(path))


def main():
    global ICONS
    out = Path(sys.argv[1]); prefix = sys.argv[2]; do_bench = len(sys.argv) > 3 and sys.argv[3] == 'bench'
    app = QGuiApplication(sys.argv)
    for f in (ROOT / 'assets/fonts').glob('*.ttf'): QFontDatabase.addApplicationFont(str(f))
    QTimer.singleShot(120000, lambda: os._exit(2))
    qInstallMessageHandler(lambda t, c, m: MSGS.append(m))
    ICONS = make_icons()
    if do_bench:
        view, stub, eng = make_view(app, 'red-dark')
        res = bench(app, view)
        for i, (a, b) in enumerate(res): print(f'run{i}: setOpen={a:.1f}ms  open->first-frame={b:.1f}ms')
        tot = [b for a, b in res]
        print(f'first={tot[0]:.1f}  median(all)={statistics.median(tot):.1f}  median(2..n)={statistics.median(tot[1:]):.1f}  iconUrl calls={stub.icon_calls} action calls={stub.action_calls}')
    else:
        for name, key, size in (('red', 'red-dark', (1280, 820)), ('teal', 'teal-dark', (1280, 820)), ('1366', 'red-dark', (1366, 768))):
            view, stub, eng = make_view(app, key, size)
            stub.h = size[1]; stub.changed.emit()
            shot(app, view, out / f'{prefix}-{name}.png')
            if name == '1366':
                view.rootObject().findChild(QObject, 'workspaceQuery').setProperty('text', 'dis')
                pump(app, 900); view.grabWindow().save(str(out / f'{prefix}-search.png'))
            view.hide()
    print('MESSAGES:', len(MSGS))
    for m in MSGS[:25]: print(' ', m[:240])
    sys.stdout.flush(); os._exit(0)


if __name__ == "__main__":
    main()
