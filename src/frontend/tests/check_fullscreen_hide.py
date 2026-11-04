"""Offscreen check: Dock.qml and EdgeRail.qml are hidden while Bridge.screenFullscreen is true and come back afterwards (stub Bridge).
Usage: python -I check_fullscreen_hide.py     (QT_QPA_PLATFORM=offscreen is forced)"""

import os, sys, ast, time

os.environ["QT_QPA_PLATFORM"] = "offscreen"
from pathlib import Path
from PySide6.QtCore import QPointF, QObject, Property, Signal, Slot, QUrl, qInstallMessageHandler
from PySide6.QtGui import QGuiApplication, QFontDatabase, QImage, QPainter, QColor, QFont
from PySide6.QtQuick import QQuickItem
from PySide6.QtQml import QQmlEngine, QQmlComponent

ROOT = Path(__file__).resolve().parents[1]
OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(os.environ.get("TEMP", ".")) / "dock-test"
OUT.mkdir(parents=True, exist_ok=True)
_src = (Path(__file__).parent / "render_workspaces.py").read_text(encoding="utf8")
PAL = next(
    eval(compile(ast.Expression(n.value), "pal", "eval"))
    for n in ast.parse(_src).body
    if isinstance(n, ast.Assign) and getattr(n.targets[0], "id", "") == "PAL"
)["red-dark"]
MSGS = []
qInstallMessageHandler(lambda k, c, m: MSGS.append(m))
results = []


def check(name, ok, detail=""):
    results.append(bool(ok))
    print(("PASS " if ok else "FAIL ") + name + (" -- " + str(detail) if detail else ""))


class Stub(QObject):
    fsChanged = Signal()
    changed = Signal()
    dockChanged = Signal()
    shellEvent = Signal(str)

    def __init__(self):
        super().__init__()
        self.reduced = False
        self.kind = "wifi"
        self.log = []

    dark = Property(bool, lambda s: PAL["dark"], notify=changed)
    adaptivePalette = Property("QVariantMap", lambda s: PAL["p"], notify=changed)
    reducedEffects = Property(bool, lambda s: s.reduced, notify=changed)
    networkKind = Property(str, lambda s: s.kind, notify=changed)
    preview = Property(bool, lambda s: False, constant=True)
    taskbarEnabled = Property(bool, lambda s: True, constant=True)
    taskbarApps = Property("QVariantList", lambda s: [], notify=dockChanged)
    runningApps = Property("QVariantList", lambda s: [], notify=dockChanged)
    settings = Property("QVariantMap", lambda s: {"motionScale": 1}, notify=changed)
    screenX = Property(int, lambda s: 0, constant=True)
    screenY = Property(int, lambda s: 0, constant=True)
    screenWidth = Property(int, lambda s: 1280, constant=True)
    screenHeight = Property(int, lambda s: 720, constant=True)
    screenFullscreen = Property(bool, lambda s: s.fs, notify=fsChanged)
    frameEnabled = Property(bool, lambda s: False, constant=True)
    frameThickness = Property(int, lambda s: 0, constant=True)
    glassEnabled = Property(bool, lambda s: False, constant=True)
    glassOpacity = Property(float, lambda s: 1.0, constant=True)
    glassTintOpacity = Property(float, lambda s: 1.0, constant=True)
    themeMode = Property(str, lambda s: "dark", constant=True)
    iconRevision = Property(int, lambda s: 0, constant=True)
    assetRoot = Property(str, lambda s: ROOT.joinpath("assets").as_uri(), constant=True)

    @Slot()
    def updateSurfaces(self):
        pass

    @Slot(str, "QVariant")
    def action(self, n, p=None):
        self.log.append(("action", n))

    @Slot(str)
    def navigate(self, t):
        self.log.append(("navigate", t))

    @Slot(int)
    def openTab(self, t):
        self.log.append(("tab", t))

    @Slot()
    def closePanel(self):
        pass

    @Slot(str, "QVariant", result=str)
    def iconUrl(self, i, r=0):
        return ""

    @Slot(str, result=str)
    def localUrl(self, p):
        return ""

    @Slot()
    def menuOutsideClick(self):
        pass


def pump(ms):
    end = time.perf_counter() + ms / 1000
    while time.perf_counter() < end:
        app.processEvents()


app = QGuiApplication([])
for f in (ROOT / "assets/fonts").glob("*.ttf"):
    QFontDatabase.addApplicationFont(str(f))
stub = Stub()
stub.fs = False
eng = QQmlEngine()
eng.addImportPath(str(ROOT / "qml"))
eng.rootContext().setContextProperty("Bridge", stub)
for name in ("Dock", "EdgeRail"):
    comp = QQmlComponent(eng, QUrl.fromLocalFile(str(ROOT / f"qml/{name}.qml")))
    w = comp.create()
    if w is None:
        check(name + " loads", False, [e.toString() for e in comp.errors()][:2])
        continue
    w.setProperty("shown", True)
    pump(500)
    check(name + " visible when not fullscreen", w.property("visible") is True, w.property("visible"))
    stub.fs = True
    stub.fsChanged.emit()
    pump(200)
    check(name + " hidden on fullscreen monitor", w.property("visible") is False, w.property("visible"))
    stub.fs = False
    stub.fsChanged.emit()
    pump(200)
    check(name + " restored after fullscreen exit", w.property("visible") is True, w.property("visible"))
    w.setProperty("visible", False)
print("TOTAL %d/%d" % (sum(results), len(results)))
sys.exit(0 if all(results) else 1)
