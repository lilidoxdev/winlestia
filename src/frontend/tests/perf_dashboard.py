"""Offscreen perf harness for the top dashboard (Content.qml) with a stub Bridge.
Usage: python perf_dashboard.py [OUT.json]
Measures first frame, per-tab switch time, frameSwapped intervals (median/p95/max) during open and tab
animations, live objects per tab, and running Timers/Animations in inactive tabs. Offscreen = software
scenegraph unless QSG_RHI_BACKEND is set, so compare before/after, not absolute GPU numbers.
Does not touch the running app. Hard timeout 90 s."""
import os, sys, json, time, statistics
os.environ.setdefault('QT_QPA_PLATFORM', 'offscreen')
from pathlib import Path
from PySide6.QtCore import QEventLoop, QThread, QObject, Property, Signal, Slot, QUrl, QTimer, QElapsedTimer
from PySide6.QtGui import QGuiApplication
from PySide6.QtQuick import QQuickView
from PySide6.QtQml import QQmlEngine
sys.path.insert(0, str(Path(__file__).parent))
import render_workspaces as rw  # reuses PAL / Stub / icons (main() is guarded below)

ROOT = rw.ROOT

class Win(QObject):
    changed = Signal()
    visible = Property(bool, lambda s: True, notify=changed)
    pageMode = Property(str, lambda s: 'dashboard', notify=changed)
    dashboardTab = Property(int, lambda s: s.tab, notify=changed)
    def __init__(s): super().__init__(); s.tab = 0

class Player(QObject):
    changed = Signal()
    for _n, _t, _v in (('available',bool,True),('trackTitle',str,'Title'),('trackArtist',str,'Artist'),('trackAlbum',str,'Album'),('trackArtUrl',str,''),('identity',str,'Player'),('isPlaying',bool,False),('length',float,200.0),('volume',float,70.0),('canGoNext',bool,True),('canGoPrevious',bool,True),('canTogglePlaying',bool,True),('canSeek',bool,True),('positionSupported',bool,True),('canRaise',bool,True),('position',float,10.0)):
        locals()[_n] = Property(_t, (lambda v: (lambda s: v))(_v), notify=changed)
    del _n, _t, _v

class DStub(rw.Stub):
    perfChanged = Signal()
    def __init__(s, *a):
        super().__init__(*a); s.win = Win(); s.pl = Player()
    window = Property('QVariant', lambda s: s.win, constant=True)
    player = Property('QVariant', lambda s: s.pl, constant=True)
    performance = Property('QVariantMap', lambda s: {'cpu':12,'gpu':5,'cpuTemp':50,'gpuTemp':45,'memPercent':40,'memUsedKiB':8000000,'diskPercent':50,'diskUsedKiB':200000000}, notify=perfChanged)
    sysInfo = Property('QVariantMap', lambda s: {'osName':'Windows 11','uptime':'1h'}, notify=perfChanged)
    spectrum = Property('QVariantList', lambda s: [0.0]*64, constant=True)
    weather = Property('QVariantMap', lambda s: {}, constant=True)
    @Slot()
    def refreshWeather(s): pass
    @Slot(int)
    def openTab(s, i): pass

def pct(v, p):
    v = sorted(v); return v[min(len(v)-1, int(len(v)*p))] if v else 0
def summ(v):
    return {'n': len(v), 'median': round(statistics.median(v),2) if v else 0, 'p95': round(pct(v,.95),2), 'max': round(max(v),2) if v else 0}

def main():
    app = QGuiApplication(sys.argv)
    from PySide6.QtGui import QFontDatabase
    for f in (ROOT/'assets/fonts').glob('*.ttf'): QFontDatabase.addApplicationFont(str(f))
    rw.ICONS.update(rw.make_icons(Path(os.environ.get('TEMP', '.')) / 'ws2-icons'))
    QTimer.singleShot(90000, lambda: os._exit(2))
    msgs = []
    from PySide6.QtCore import qInstallMessageHandler
    qInstallMessageHandler(lambda t, c, m: msgs.append(m))
    stub = DStub(rw.PAL['red-dark'], 5, 4)
    eng = QQmlEngine(); eng.addImportPath(str(ROOT/'qml')); eng.rootContext().setContextProperty('Bridge', stub)
    view = QQuickView(eng, None)
    clock = QElapsedTimer(); frames = []
    view.frameSwapped.connect(lambda: frames.append(clock.nsecsElapsed()/1e6))
    view.setSource(QUrl.fromLocalFile(str(Path(__file__).parent/os.environ.get('H','DashHarness.qml'))))
    if view.status() != QQuickView.Ready:
        print('QML errors:', [e.toString() for e in view.errors()]); sys.exit(1)
    view.setResizeMode(QQuickView.SizeRootObjectToView); view.resize(980, 620)
    res = {}
    def wait(ms):
        end = clock.nsecsElapsed()/1e6 + ms
        while clock.nsecsElapsed()/1e6 < end: app.processEvents(QEventLoop.AllEvents, 5); QThread.msleep(1)
    def intervals(a, b):
        f = [x for x in frames if a <= x <= b]
        return [f[i+1]-f[i] for i in range(len(f)-1)]
    clock.start(); t0 = 0.0
    view.show()
    while not frames: app.processEvents()
    res['first_frame_ms'] = round(frames[0], 1)
    wait(1200)  # settle
    root = view.rootObject()
    from PySide6.QtCore import QMetaObject, Q_RETURN_ARG, Q_ARG
    def stats(i):
        return root.property('dummy') if False else QMetaObject.invokeMethod(root, 'stats', Q_RETURN_ARG('QVariant'), Q_ARG('QVariant', i))
    res['open'] = summ(intervals(0, clock.nsecsElapsed()/1e6)[:60])
    res['tabs'] = {}
    pass
    for tab in (1, 2, 3, 0, 3, 2, 1, 0):
        a = clock.nsecsElapsed()/1e6
        stub.win.tab = tab; stub.win.changed.emit()
        ts = time.perf_counter(); QMetaObject.invokeMethod(root, 'setTab', Q_ARG('QVariant', tab)); sync = (time.perf_counter()-ts)*1000
        n0 = len(frames)
        while len(frames) == n0 and clock.nsecsElapsed()/1e6 - a < 2000: app.processEvents(QEventLoop.AllEvents, 5); QThread.msleep(1)
        first = clock.nsecsElapsed()/1e6 - a  # switch -> next swapped frame
        wait(600)
        b = clock.nsecsElapsed()/1e6
        ivs = intervals(a, b)
        key = f'tab{tab}' + ('_cold' if f'tab{tab}_cold' not in res['tabs'] else '_warm')
        res['tabs'][key] = {'sync_setTab_ms': round(sync,1), 'switch_to_frame_ms': round(first,1), 'frames': summ(ivs)}
    res['live'] = {}
    # Inactive tabs must be idle: check each loaded pane while tab 0 is current.
    for i in range(4):
        s = stats(i); s = s.toVariant() if hasattr(s,'toVariant') else s; res['live'][f'tab{i}'] = s
    res['dbg']=[round(x) for x in frames[-6:]]+[len(frames), round(clock.nsecsElapsed()/1e6)]
    # Cost of one Bridge.changed emit (every Bridge binding re-evaluates), all tabs loaded.
    res['changed_emit_ms'] = {}
    for tab in (0, 3):
        QMetaObject.invokeMethod(root, 'setTab', Q_ARG('QVariant', tab)); wait(900)
        ts = time.perf_counter()
        for _ in range(20): stub.changed.emit()
        res['changed_emit_ms'][f'active_tab{tab}'] = round((time.perf_counter()-ts)*1000/20, 2)
        ts = time.perf_counter()
        for _ in range(20): stub.perfChanged.emit()
        res.setdefault('perfChanged_emit_ms', {})[f'active_tab{tab}'] = round((time.perf_counter()-ts)*1000/20, 2)
    print(json.dumps(res, indent=1, default=str))
    if len(sys.argv) > 1: Path(sys.argv[1]).write_text(json.dumps(res, indent=1, default=str))
    bad = [m for m in msgs if 'TypeError' in m or 'ReferenceError' in m or 'Cannot' in m or 'is not' in m]
    print('MSGS', len(msgs), 'ERR', len(bad)); [print('  ', m[:200]) for m in bad[:8]]
    os._exit(0)

main()
