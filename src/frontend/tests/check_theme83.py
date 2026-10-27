# Python 3.12 / Qt 6 / Windows. Synthetic wallpapers, real native click-through frame and contrast checks.
import sys,os,json,ctypes
from pathlib import Path
from types import SimpleNamespace
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
os.environ.setdefault('QSG_RHI_BACKEND','d3d11')
from PySide6.QtCore import QObject,QTimer,QUrl,qInstallMessageHandler
from PySide6.QtGui import QFontDatabase
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtQuick import QQuickWindow
from PIL import Image,ImageDraw
from material_color_utilities import get_contrast_ratio
from frontend import Bridge,ROOT
from wallpaper_theme import generate_palette

dest=ROOT/'validation-8.3';dest.mkdir(exist_ok=True);checks=[];issues=[]
def check(name,value):checks.append({'name':name,'pass':bool(value)})
palettes={}
for name,base,accent in [('blue','#264680','#6b4e9a'),('yellow','#f2cd55','#c68d31'),('gray','#eeeeee','#666666')]:
    path=dest/(name+'-wallpaper.png');picture=Image.new('RGB',(800,450),base);draw=ImageDraw.Draw(picture);draw.ellipse((400,200,1100,600),fill=accent);picture.save(path)
    theme=generate_palette(str(path));palettes[name]=theme
    p=theme['palette'];check(name+' text contrast',get_contrast_ratio(p['m3surface'],p['m3onSurface'])>=7 and get_contrast_ratio(p['m3primary'],p['m3onPrimary'])>=4.5)
check('Blue and yellow produce different palettes',palettes['blue']['palette']['m3primary']!=palettes['yellow']['palette']['m3primary'])
check('Grayscale wallpaper stays monochrome',palettes['gray']['monochrome'])
app=QApplication([]);app.setQuitOnLastWindowClosed(False)
for font in (ROOT/'assets/fonts').glob('*.ttf'):QFontDatabase.addApplicationFont(str(font))
qInstallMessageHandler(lambda kind,context,message:issues.append(message))
bridge=Bridge(SimpleNamespace(pipe='PanneauxTheme83Test',backend_pid=0,preview=False));bridge.pointer.stop();bridge.city=''
state={'settings':{'themeMode':'auto','screenFrame':True,'motionScale':1},'controls':{'volume':50,'brightnessAvailable':True,'brightness':64},'audioMixer':{'applications':[],'devices':[]},'dark':True,'taskbarEnabled':True}
bridge.update_state({'state':state})
engine=QQmlApplicationEngine();engine.addImportPath(str(ROOT/'qml'));engine.addImageProvider('capture',bridge.capture.provider);engine.rootContext().setContextProperty('Bridge',bridge);engine.load(QUrl.fromLocalFile(str(ROOT/'qml/Main.qml')))
if not engine.rootObjects():bridge.stop();print(json.dumps(issues));raise SystemExit(1)
top=engine.rootObjects()[0];bridge.top=top;bridge._window=top;bridge.dock=top.findChild(QObject,'bottomDock');bridge.windowChanged.emit()
def frames():return [w for w in app.allWindows() if w.title().startswith('Panneaux · Cadre')]
def apply(name):
    bridge.walltheme.themes={bridge._screen.name():palettes[name]};bridge.theme_ready();bridge.navigate('controls');bridge.dock.setProperty('shown',True)
def save(name):
    top.grabWindow().save(str(dest/(name+'-controls.png')))
    check(name+' frame exists',bool(frames()));
    if not frames():return
    frame=frames()[0];frameImage=frame.grabWindow();frameImage.save(str(dest/(name+'-frame.png')))
    check(name+' transparent frame center',frameImage.pixelColor(frameImage.width()//2,frameImage.height()//2).alpha()==0)
    check(name+' translucent frame edge',0<frameImage.pixelColor(2,frameImage.height()//2).alpha()<255)
    user32=ctypes.windll.user32;user32.GetWindowLongPtrW.argtypes=[ctypes.c_void_p,ctypes.c_int];user32.GetWindowLongPtrW.restype=ctypes.c_ssize_t
    style=user32.GetWindowLongPtrW(int(frame.winId()),-20);check(name+' frame ignores mouse and focus',style&0x20 and style&0x08000000)
    background=Image.open(dest/(name+'-wallpaper.png')).convert('RGBA').resize((frameImage.width(),frameImage.height()))
    overlay=Image.open(dest/(name+'-frame.png')).convert('RGBA');background.alpha_composite(overlay)
    panel=Image.open(dest/(name+'-controls.png')).convert('RGBA');background.alpha_composite(panel,(round(top.x()-bridge.screenX),round(top.y()-bridge.screenY)))
    dock=bridge.dock.grabWindow();dock.save(str(dest/'dock-snapshot.png'));dockImage=Image.open(dest/'dock-snapshot.png').convert('RGBA');background.alpha_composite(dockImage,(round(bridge.dock.x()-bridge.screenX),round(bridge.dock.y()-bridge.screenY)))
    background.thumbnail((1720,1000));background.save(dest/(name+'-composite.png'))
handles=[]
QTimer.singleShot(150,lambda:apply('blue'))
QTimer.singleShot(1100,lambda:handles.extend(int(w.winId()) for w in frames()))
QTimer.singleShot(1200,lambda:save('blue'))
QTimer.singleShot(1500,lambda:apply('yellow'))
QTimer.singleShot(2500,lambda:save('yellow'))
QTimer.singleShot(2800,lambda:apply('gray'))
QTimer.singleShot(3800,lambda:save('gray'))
def final_checks():
    check('Frame windows survive palette changes',bool(handles) and handles==[int(w.winId()) for w in frames()])
    if not frames():check('Frame available for visibility tests',False);return
    bridge.fullscreen_names=(bridge._screen.name(),);bridge.frameChanged.emit();check('Fullscreen hides frame',not frames()[0].isVisible())
    bridge.fullscreen_names=();bridge.frameChanged.emit();bridge.capture.pending=True;bridge.capture.changed.emit();check('Capture hides frame before taking image',not frames()[0].isVisible());bridge.capture.cancel()
    bridge.navigate('settings')
QTimer.singleShot(4100,final_checks)
QTimer.singleShot(5000,lambda:top.grabWindow().save(str(dest/'theme-settings.png')))
def finish():
    check('No QML warnings',not issues);report={'allPassed':all(c['pass'] for c in checks),'checks':checks,'qmlIssues':issues,'palettes':palettes};(dest/'theme-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({'allPassed':report['allPassed'],'checks':checks,'qmlIssues':issues},indent=2));app.quit()
QTimer.singleShot(5200,finish);app.aboutToQuit.connect(bridge.stop);app.exec();raise SystemExit(0 if all(c['pass'] for c in checks) else 1)
