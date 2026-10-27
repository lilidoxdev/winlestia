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
background=Background();background.setWindowFlags(Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.WindowDoesNotAcceptFocus|Qt.WindowTransparentForInput);background.setGeometry(bounds);background.show()
bridge=Bridge(SimpleNamespace(pipe='WinLestiaSurface92Test',backend_pid=0,preview=True));bridge.pointer.stop();bridge.city='';bridge.update_state({'state':{'settings':{'themeMode':'auto','screenFrame':True,'frameThickness':20,'glassOpacity':.12,'motionScale':1},'taskbarEnabled':True,'dark':True,'audioMixer':{'applications':[],'devices':[]}}})
bridge.walltheme.themes={screen.name():generate_palette('', '#75001b')};bridge.theme_ready()
engine=QQmlApplicationEngine();engine.addImportPath(str(ROOT/'qml'));engine.addImageProvider('capture',bridge.capture.provider);engine.rootContext().setContextProperty('Bridge',bridge);engine.load(QUrl.fromLocalFile(str(ROOT/'qml/Main.qml')))
if not engine.rootObjects():bridge.stop();print(issues);raise SystemExit(1)
root=engine.rootObjects()[0];bridge.top=root;bridge._window=root;bridge.dock=root.findChild(QObject,'bottomDock');bridge.windowChanged.emit();bridge.surface_scene=SurfaceScene(bridge);bridge.glass=NativeGlass(bridge,ROOT.parent/'research/qt92-glass/QtGlassBackdrop.dll')
bridge.navigate('access');bridge.dock.setProperty('shown',True)
import time
benchmark_start=time.perf_counter();samples=[];navigation_times=[];geometry_times=[];scene_times=[];original=bridge.glass.dll.BackdropSurface
original_scene=bridge.surface_scene.refresh

def native(*args):
    start=time.perf_counter();result=original(*args);geometry_times.append((time.perf_counter()-start)*1000);return result
bridge.glass.dll.BackdropSurface=native
original_dirty=bridge.glass.dll.BackdropSurfaceDirty
def dirty(*args):
    start=time.perf_counter();result=original_dirty(*args);geometry_times.append((time.perf_counter()-start)*1000);return result
bridge.glass.dll.BackdropSurfaceDirty=dirty
root.frameSwapped.connect(lambda:samples.append(time.perf_counter()))
sequence=['close','access','close','dashboard','settings','access','close','access','settings','dashboard','close','access']
def navigate(mode):navigation_times.append(time.perf_counter());bridge.navigate(mode)
for i,mode in enumerate(sequence):QTimer.singleShot(1400+i*650,lambda m=mode:navigate(m))
def finish():
    intervals=[(b-a)*1000 for a,b in zip(samples,samples[1:]) if any(start<=a and b<=start+.27 for start in navigation_times)]
    def stats(values):
        values=sorted(values);return {'count':len(values),'median':statistics.median(values) if values else None,'p95':values[min(len(values)-1,int(len(values)*.95))] if values else None,'max':max(values,default=0)}
    spikes=[{'at':round(a-benchmark_start,3),'ms':round((b-a)*1000,2)} for a,b in zip(samples,samples[1:]) if b-a>.0334 and any(start<=a and b<=start+.27 for start in navigation_times)]
    report={'spikes':spikes,'activeFrameMs':stats(intervals),'nativeGeometryMs':stats(geometry_times),'over33ms':sum(v>33.4 for v in intervals),'nativeErrors':bridge.glass.errors,'qmlIssues':issues,'refreshRate':screen.refreshRate()}
    (ROOT/'validation-9.2'/('performance-after.json')).write_text(json.dumps(report,indent=2));print(json.dumps(report));app.quit()
QTimer.singleShot(9700,finish)
def stop():bridge.stop()
app.aboutToQuit.connect(stop);app.exec()
