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
app=QApplication([]);app.setQuitOnLastWindowClosed(False);screen=app.primaryScreen();bounds=screen.geometry();dest=ROOT/'validation-9.2';dest.mkdir(exist_ok=True);issues=[];checks=[];report={'checks':checks}
qInstallMessageHandler(lambda kind,context,message:issues.append(message) if int(kind)>0 else None)
for font in (ROOT/'assets/fonts').glob('*.ttf'):QFontDatabase.addApplicationFont(str(font))
class Background(QWidget):
    patterned=False
    def paintEvent(self,event):
        painter=QPainter(self)
        if not self.patterned:painter.fillRect(self.rect(),QColor('#a0c0e0'))
        else:
            for x in range(0,self.width(),16):painter.fillRect(x,0,16,self.height(),QColor('#e8e8e8' if x//16%2 else '#282828'))
background=Background();background.setWindowFlags(Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.WindowDoesNotAcceptFocus|Qt.WindowTransparentForInput);background.setGeometry(bounds)
bridge=Bridge(SimpleNamespace(pipe='WinLestiaPointer92Test',backend_pid=0,preview=True));bridge.pointer.stop();bridge.city='';bridge.update_state({'state':{'settings':{'themeMode':'auto','screenFrame':True,'frameThickness':20,'glassOpacity':.12,'motionScale':0},'taskbarEnabled':True,'dark':True,'audioMixer':{'applications':[],'devices':[]}}})
bridge.walltheme.themes={screen.name():generate_palette('', '#75001b')};bridge.theme_ready()
engine=QQmlApplicationEngine();engine.addImportPath(str(ROOT/'qml'));engine.addImageProvider('capture',bridge.capture.provider);engine.rootContext().setContextProperty('Bridge',bridge);engine.load(QUrl.fromLocalFile(str(ROOT/'qml/Main.qml')))
if not engine.rootObjects():bridge.stop();print(issues);raise SystemExit(1)
root=engine.rootObjects()[0];bridge.top=root;bridge._window=root;bridge.dock=root.findChild(QObject,'bottomDock');bridge.windowChanged.emit();bridge.surface_scene=SurfaceScene(bridge);bridge.glass=NativeGlass(bridge,ROOT.parent/'research/qt92-glass/QtGlassBackdrop.dll')
bridge.navigate('access');bridge.dock.setProperty('shown',True)
import subprocess,time
child=subprocess.Popen([sys.executable,str(ROOT/'tests/pointer_target92.py'),str(dest)],creationflags=0x08000000)
user=ctypes.windll.user32;user.GetWindowLongPtrW.argtypes=[ctypes.c_void_p,ctypes.c_int];user.GetWindowLongPtrW.restype=ctypes.c_ssize_t
class Point(ctypes.Structure):_fields_=[('x',ctypes.c_long),('y',ctypes.c_long)]
old=Point();user.GetCursorPos(ctypes.byref(old));report={}
def count():return int((dest/'click-count.txt').read_text())
def click(x,y):
    user.SetCursorPos(round(x*screen.devicePixelRatio()),round(y*screen.devicePixelRatio()));user.mouse_event(2,0,0,0,0);user.mouse_event(4,0,0,0,0)
def first():
    bridge.glass.sync()
    report['before']=count();click(150,270)
def second():
    report['afterDesktopClick']=count();bridge.glass.sync();click(root.x()+3,root.y()+120)
def corner():
    report['afterShoulderClick']=count();bridge.glass.sync();width=float(root.property('panelWidth') or root.width());height=float(root.property('panelHeight') or root.height());click(root.x()+(root.width()-width)/2+22,root.y()+height-2)
def visual_items(item):
    yield item
    for child in item.childItems():yield from visual_items(child)

def button():
    report['afterCornerClick']=count();bridge.glass.sync();tile=next((item for item in visual_items(root.contentItem()) if item.objectName()=='workspace-apps'),None);report['tileFound']=bool(tile)
    if tile:
        position=tile.mapToScene(QPointF(tile.width()/2,tile.height()/2));report['buttonPoint']=[position.x(),position.y()];click(root.x()+position.x(),root.y()+position.y())
def finish():
    report['afterButtonClick']=count();report['mode']=bridge.mode;report['qmlIssues']=issues;report['cornerPass']=report['afterCornerClick']>report['afterShoulderClick'];report['buttonPass']=bridge.mode=='workspace' and report['afterButtonClick']==report['afterCornerClick'];report['desktopPass']=report['afterDesktopClick']>report['before'];report['shoulderPass']=report['afterShoulderClick']>report['afterDesktopClick'];report['nativeErrors']=bridge.glass.errors
    frames=[w for w in app.allWindows() if w.title().startswith('Panneaux · Cadre')]
    report['surfaceStyles']=[user.GetWindowLongPtrW(int(w.winId()),-20) for w in frames]
    report['helperStyles']=[user.GetWindowLongPtrW(bridge.glass.dll.BackdropPart(h,0),-20) for h in bridge.glass.windows]
    user.SetCursorPos(old.x,old.y);(dest/'pointer-after.json').write_text(json.dumps(report,indent=2));print(json.dumps(report));app.quit()
QTimer.singleShot(1600,first);QTimer.singleShot(2100,second);QTimer.singleShot(2500,corner);QTimer.singleShot(2900,button);QTimer.singleShot(3300,finish)
def stop():bridge.stop()
app.aboutToQuit.connect(stop);app.exec();child.terminate();child.wait()
