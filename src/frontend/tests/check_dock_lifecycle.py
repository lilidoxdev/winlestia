# Python 3.12 / Qt 6.10 / Windows. Real native window visibility, with the dashboard initially hidden.
import ctypes, json, os, sys
from pathlib import Path
from types import SimpleNamespace
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
os.environ.setdefault('QSG_RHI_BACKEND','d3d11')
from PySide6.QtCore import QObject,QTimer,QUrl,qInstallMessageHandler
from PySide6.QtGui import QFontDatabase
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtQuick import QQuickWindow
from frontend import Bridge,ROOT

app=QApplication([]);app.setQuitOnLastWindowClosed(False)
for font in (ROOT/'assets/fonts').glob('*.ttf'):QFontDatabase.addApplicationFont(str(font))
issues=[];qInstallMessageHandler(lambda kind,context,message:issues.append(message))
bridge=Bridge(SimpleNamespace(pipe='PanneauxDockLifecycleTest',backend_pid=0,preview=False));bridge.pointer.stop();bridge.city=''
bridge._state={'taskbarEnabled':True};engine=QQmlApplicationEngine();engine.addImportPath(str(ROOT/'qml'));engine.rootContext().setContextProperty('Bridge',bridge);engine.load(QUrl.fromLocalFile(str(ROOT/'qml/Main.qml')))
if not engine.rootObjects():bridge.stop();print(json.dumps({'loadErrors':issues}));raise SystemExit(1)
top=engine.rootObjects()[0];bridge.top=top;bridge._window=top;bridge.dock=top.findChild(QObject,'bottomDock');bridge.windowChanged.emit()
user32=ctypes.windll.user32;user32.IsWindowVisible.argtypes=[ctypes.c_void_p];user32.GetWindow.argtypes=[ctypes.c_void_p,ctypes.c_uint];user32.GetWindow.restype=ctypes.c_void_p
checks=[]
def observe(name,expected):
    dock=bridge.dock;native=bool(user32.IsWindowVisible(int(dock.winId())));owner=user32.GetWindow(int(dock.winId()),4)
    checks.append({'name':name,'mainVisible':top.isVisible(),'dockRequested':dock.property('shown'),'dockVisible':native,'ownedByMain':owner==int(top.winId()),'pass':native==expected and not top.isVisible()})
def cold():
    observe('Initially both hidden',False);bridge.shellEvent.emit('dock')
QTimer.singleShot(250,cold)
QTimer.singleShot(800,lambda:observe('Bottom edge before any top hover',True))
QTimer.singleShot(850,lambda:bridge.openTab(0))
QTimer.singleShot(1600,bridge.closePanel)
QTimer.singleShot(2200,lambda:observe('Dock remains visible after dashboard closes',True))
QTimer.singleShot(2250,lambda:bridge.shellEvent.emit('dockHide'))
QTimer.singleShot(2800,lambda:observe('Dock hides while dashboard stays closed',False))
QTimer.singleShot(2850,lambda:bridge.shellEvent.emit('dock'))
QTimer.singleShot(3400,lambda:observe('Dock reopens without the dashboard',True))
def finish():
    report={'checks':checks,'allPassed':all(c['pass'] for c in checks),'runtimeIssues':issues[:]};destination=ROOT/'validation-8.1/dock-lifecycle.json';destination.parent.mkdir(exist_ok=True);destination.write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2));app.quit()
QTimer.singleShot(3500,finish);app.aboutToQuit.connect(bridge.stop);app.exec();raise SystemExit(0 if all(c['pass'] for c in checks) else 1)
