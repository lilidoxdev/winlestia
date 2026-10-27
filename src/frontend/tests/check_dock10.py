# Python 3.12 / Windows. Actual compositor tests for one continuous shell material.
import sys,ctypes,json,statistics
from pathlib import Path
from types import SimpleNamespace
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PySide6.QtCore import Qt,QTimer,QUrl,QObject,QPointF,qInstallMessageHandler
from PySide6.QtGui import QPainter,QColor,QFontDatabase
from PySide6.QtWidgets import QApplication,QWidget
from PySide6.QtQml import QQmlApplicationEngine
from frontend import Bridge,ROOT
from native_glass import NativeGlass
from surface_geometry import SurfaceScene
from chromatic_palette import generate_palette
app=QApplication([]);app.setQuitOnLastWindowClosed(False);screen=app.primaryScreen();bounds=screen.geometry();dest=ROOT/'validation-dock10';dest.mkdir(exist_ok=True);issues=[];checks=[];report={'checks':checks}
qInstallMessageHandler(lambda kind,context,message:issues.append(message) if int(kind)>0 else None)
for font in (ROOT/'assets/fonts').glob('*.ttf'):QFontDatabase.addApplicationFont(str(font))
class Background(QWidget):
    patterned=False
    def paintEvent(self,event):
        painter=QPainter(self)
        if not self.patterned:painter.fillRect(self.rect(),QColor('#a0c0e0'))
        else:
            for x in range(0,self.width(),16):painter.fillRect(x,0,16,self.height(),QColor('#e8e8e8' if x//16%2 else '#282828'))
background=Background();background.setWindowFlags(Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.WindowDoesNotAcceptFocus|Qt.WindowTransparentForInput);background.setGeometry(bounds);background.show()
bridge=Bridge(SimpleNamespace(pipe='WinLestiaSurface92Test',backend_pid=0,preview=True));bridge.pointer.stop();bridge.city='';bridge.update_state({'state':{'settings':{'themeMode':'auto','screenFrame':True,'frameThickness':20,'glassOpacity':.12,'motionScale':0},'taskbarEnabled':True,'dark':True,'audioMixer':{'applications':[],'devices':[]}}})
bridge.walltheme.themes={screen.name():generate_palette('', '#75001b')};bridge.theme_ready()
engine=QQmlApplicationEngine();engine.addImportPath(str(ROOT/'qml'));engine.addImageProvider('capture',bridge.capture.provider);engine.rootContext().setContextProperty('Bridge',bridge);engine.load(QUrl.fromLocalFile(str(ROOT/'qml/Main.qml')))
if not engine.rootObjects():bridge.stop();print(issues);raise SystemExit(1)
root=engine.rootObjects()[0];bridge.top=root;bridge._window=root;bridge.dock=root.findChild(QObject,'bottomDock');bridge.windowChanged.emit();bridge.surface_scene=SurfaceScene(bridge);bridge.glass=NativeGlass(bridge,ROOT.parent/'research/qt93-glass/QtGlassBackdrop.dll')
bridge.navigate('access');bridge.dock.setProperty('shown',True)

entry={'id':'fixture','name':'Application de test','path':'C:/fixture.exe','hwnd':123,'count':2,'pinned':False,'favorite':False,'canStop':True,'windows':[{'hwnd':123,'title':'Document A'},{'hwnd':124,'title':'Document B'}]}
bridge._state['runningApps']=[entry];bridge._state['taskbarApps']=[entry];bridge.dockChanged.emit()
menu=next(w for w in app.allWindows() if w.title()=='Panneaux · Menu du dock')
from PySide6.QtCore import QMetaObject,Q_ARG
records=[]
from PySide6.QtTest import QTest
from PySide6.QtCore import QPoint
clicks=[]
def items(item):
    yield item
    for child in item.childItems():yield from items(child)
def click(name,button,expected):
    menu.setVisible(False)
    item=next(i for i in items(bridge.dock.contentItem()) if i.objectName()==name)
    point=item.mapToScene(QPointF(item.width()/2,item.height()/2))
    QTest.mouseClick(bridge.dock,button,Qt.NoModifier,QPoint(round(point.x()),round(point.y())))
    QTimer.singleShot(220,lambda:clicks.append({'target':name,'kind':menu.property('kind'),'visible':menu.isVisible(),'point':[point.x(),point.y()],'itemSize':[item.width(),item.height()],'pass':menu.isVisible() and menu.property('kind')==expected}))
QTimer.singleShot(1900,lambda:click('dockStart',Qt.RightButton,'system'))
QTimer.singleShot(2300,lambda:click('dock-app-fixture',Qt.RightButton,'app'))
QTimer.singleShot(2700,lambda:click('dockRunning',Qt.LeftButton,'running'))
def show(kind):
    menu.setProperty('kind',kind);menu.setProperty('entry',entry);menu.setProperty('anchorX',bounds.width()/2);menu.setProperty('anchorY',bounds.height()-90);menu.setVisible(True)
    QTimer.singleShot(180,lambda:records.append({'kind':kind,'height':menu.height(),'visible':menu.isVisible()}))
QTimer.singleShot(500,lambda:show('app'));QTimer.singleShot(900,lambda:show('system'));QTimer.singleShot(1300,lambda:show('running'))
def finish():
    global report
    report={'states':records,'clicks':clicks,'qmlIssues':issues,'nativeErrors':bridge.glass.errors,'allPassed':not issues and not bridge.glass.errors and len(records)==3 and len(clicks)==3 and all(c['pass'] for c in clicks)}
    (dest/'qml-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report));app.quit()
QTimer.singleShot(3200,finish);app.aboutToQuit.connect(bridge.stop);app.exec();raise SystemExit(0 if report['allPassed'] else 1)
