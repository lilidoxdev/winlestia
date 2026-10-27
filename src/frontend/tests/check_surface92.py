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
bridge=Bridge(SimpleNamespace(pipe='WinLestiaSurface92Test',backend_pid=0,preview=True));bridge.pointer.stop();bridge.city='';bridge.update_state({'state':{'settings':{'themeMode':'auto','screenFrame':True,'frameThickness':20,'glassOpacity':.12,'motionScale':0},'taskbarEnabled':True,'dark':True,'audioMixer':{'applications':[],'devices':[]}}})
bridge.walltheme.themes={screen.name():generate_palette('', '#75001b')};bridge.theme_ready()
engine=QQmlApplicationEngine();engine.addImportPath(str(ROOT/'qml'));engine.addImageProvider('capture',bridge.capture.provider);engine.rootContext().setContextProperty('Bridge',bridge);engine.load(QUrl.fromLocalFile(str(ROOT/'qml/Main.qml')))
if not engine.rootObjects():bridge.stop();print(issues);raise SystemExit(1)
root=engine.rootObjects()[0];bridge.top=root;bridge._window=root;bridge.dock=root.findChild(QObject,'bottomDock');bridge.windowChanged.emit();bridge.surface_scene=SurfaceScene(bridge);bridge.glass=NativeGlass(bridge,ROOT.parent/'research/qt92-glass/QtGlassBackdrop.dll')
bridge.navigate('access');bridge.dock.setProperty('shown',True)
def check(name,value):checks.append({'name':name,'pass':bool(value)})
def capture():return screen.grabWindow(0).toImage()
def color(image,x,y):return image.pixelColor(round(x*screen.devicePixelRatio()),round(y*screen.devicePixelRatio()))
def flat():
    image=capture();w=bounds.width();h=bounds.height();t=bridge.frameThickness;x=w/2
    points={'frameTop':(x,t-3),'panelTop':(x,t+3),'dockBottom':(x,h-t-3),'frameBottom':(x,h-t+3)}
    samples={name:color(image,*point).getRgb()[:3] for name,point in points.items()};report['flatSamples']=samples
    check('Frame dashboard dock have identical composited material',max(max(channel)-min(channel) for channel in zip(*samples.values()))<=2)
    path=bridge.surface_scene.paths[screen.name()];inside=next(iter(samples.values()));outside=color(image,200,h/2).getRgb()[:3];bad=0
    for origin_x,origin_y in ((0,0),(w-90,0),(0,h-90),(w-90,h-90)):
        for px in range(int(origin_x)+2,int(origin_x)+88,2):
            for py in range(int(origin_y)+2,int(origin_y)+88,2):
                states=[path.contains(QPointF(px+dx,py+dy)) for dx,dy in ((-1.5,-1.5),(1.5,-1.5),(-1.5,1.5),(1.5,1.5))]
                if len(set(states))!=1:continue
                expected=inside if states[0] else outside;actual=color(image,px+.5,py+.5).getRgb()[:3]
                if max(abs(a-b) for a,b in zip(actual,expected))>3:bad+=1
    report['cornerMismatches']=bad;check('Smooth corners have no internal segment lines',bad==0)
    image.save(str(dest/'flat-continuous.png'));check('No native geometry errors',not bridge.glass.errors)
    background.patterned=True;background.update()
def alpha(value):bridge.action('preference',{'key':'glassOpacity','value':value})
def detail(value):
    image=capture();w=bounds.width();h=bounds.height();t=bridge.frameThickness;xs=range(round(w/2)-96,round(w/2)+96)
    def contrast(y):return statistics.pstdev(color(image,x,y).red() for x in xs)
    raw=contrast(h/2);values={'frame':contrast(t-3)/raw,'panel':contrast(t+3)/raw,'dock':contrast(h-t-3)/raw}
    report[str(value)+'Detail']=values
    seams=[]
    for physical in range(512,image.width(),512):
        logical=physical/screen.devicePixelRatio()
        for dx in range(-3,4):
            for yy in (t/2,h-t/2):
                a=color(image,logical+dx,yy).getRgb()[:3];b=color(image,logical+dx-32,yy).getRgb()[:3]
                seams.append(max(abs(v-z) for v,z in zip(a,b)))
    report[str(value)+'TileSeamDelta']=max(seams,default=0)
    check(str(value)+' no native tile seam',max(seams,default=0)<=2)
    check(str(value)+' same transparency for every surface',max(values.values())-min(values.values())<.035)
    check(str(value)+' transparency matches requested detail transmission',all(abs(v-(1-value))<.10 for v in values.values()))
    if value==.12:image.save(str(dest/'transparent-continuous.png'))
def closed():
    image=capture();w=bounds.width();h=bounds.height();t=bridge.frameThickness
    point=(w/2,t+180);raw=color(image,w/2,h/2).getRgb()[:3];actual=color(image,*point).getRgb()[:3]
    check('Closing panel leaves no stale glass tile',max(abs(a-b) for a,b in zip(raw,actual))<=2)

def finish():
    check('No QML warnings',not issues);report.update(allPassed=all(c['pass'] for c in checks),qmlIssues=issues,nativeErrors=bridge.glass.errors);(dest/'surface-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report));app.quit()
QTimer.singleShot(1700,flat);QTimer.singleShot(2100,lambda:detail(.12));QTimer.singleShot(2300,lambda:alpha(.02));QTimer.singleShot(2700,lambda:detail(.02));QTimer.singleShot(2900,lambda:alpha(.5));QTimer.singleShot(3300,lambda:detail(.5));QTimer.singleShot(3500,lambda:alpha(.12));QTimer.singleShot(3900,lambda:bridge.navigate("close"));QTimer.singleShot(4300,closed);QTimer.singleShot(4500,finish)
def stop():bridge.glass.stop();bridge.surface_scene.stop();bridge.stop()
app.aboutToQuit.connect(stop);app.exec();raise SystemExit(0 if report.get('allPassed') else 1)
