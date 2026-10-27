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
from wallpaper_sources import desktop_hut_active
from native_glass import NativeGlass
import colorsys

dest=ROOT/'validation-8.5';dest.mkdir(exist_ok=True);checks=[];issues=[]
def check(name,value):checks.append({'name':name,'pass':bool(value)})
palettes={}
for name,base,accent in [('blue','#264680','#6b4e9a'),('yellow','#f2cd55','#c68d31'),('gray','#eeeeee','#666666'),('red','#620019','#06212d')]:
    path=dest/(name+'-wallpaper.png');picture=Image.new('RGB',(800,450),base);draw=ImageDraw.Draw(picture);draw.ellipse((400,200,1100,600),fill=accent);picture.save(path)
    theme=generate_palette(str(path));palettes[name]=theme
    p=theme['palette'];check(name+' text contrast',get_contrast_ratio(p['m3surface'],p['m3onSurface'])>=7 and get_contrast_ratio(p['m3primary'],p['m3onPrimary'])>=4.5)
check('Blue and yellow produce different palettes',palettes['blue']['palette']['m3primary']!=palettes['yellow']['palette']['m3primary'])
check('Grayscale wallpaper stays monochrome',palettes['gray']['monochrome'])
app=QApplication([]);app.setQuitOnLastWindowClosed(False)
for font in (ROOT/'assets/fonts').glob('*.ttf'):QFontDatabase.addApplicationFont(str(font))
qInstallMessageHandler(lambda kind,context,message:issues.append(message))
bridge=Bridge(SimpleNamespace(pipe='PanneauxTheme85Test',backend_pid=0,preview=False));bridge.pointer.stop();bridge.city=''
state={'settings':{'themeMode':'auto','screenFrame':True,'motionScale':1},'controls':{'volume':50,'brightnessAvailable':True,'brightness':64},'audioMixer':{'applications':[],'devices':[]},'dark':True,'taskbarEnabled':True}
bridge.update_state({'state':state})
engine=QQmlApplicationEngine();engine.addImportPath(str(ROOT/'qml'));engine.addImageProvider('capture',bridge.capture.provider);engine.rootContext().setContextProperty('Bridge',bridge);engine.load(QUrl.fromLocalFile(str(ROOT/'qml/Main.qml')))
if not engine.rootObjects():bridge.stop();print(json.dumps(issues));raise SystemExit(1)
top=engine.rootObjects()[0];bridge.top=top;bridge._window=top;bridge.dock=top.findChild(QObject,'bottomDock');bridge.windowChanged.emit();bridge.glass=NativeGlass(bridge)
def frames():return [w for w in app.allWindows() if w.title().startswith('Panneaux · Cadre')]
def apply(name):
    bridge.walltheme.themes={bridge._screen.name():palettes[name]};bridge.theme_ready();bridge.navigate('access');bridge.dock.setProperty('shown',True)
def save(name):
    top.grabWindow().save(str(dest/(name+'-controls.png')))
    check(name+' frame exists',bool(frames()));
    if not frames():return
    frame=frames()[0];frameImage=frame.grabWindow();frameImage.save(str(dest/(name+'-frame.png')))
    check(name+' transparent frame center',frameImage.pixelColor(frameImage.width()//2,frameImage.height()//2).alpha()==0)
    check(name+' translucent frame edge',0<frameImage.pixelColor(2,frameImage.height()//2).alpha()<255)
    edge=frameImage.pixelColor(2,frameImage.height()//2)
    panelImage=top.grabWindow();dockImage=bridge.dock.grabWindow()
    check(name+' identical frame dashboard dock material',edge.rgba()==panelImage.pixelColor(panelImage.width()//2,2).rgba()==dockImage.pixelColor(dockImage.width()//2,2).rgba())
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
def native_checks():
    check('Native backdrop DLL loaded',bridge.glass.dll is not None)
    check('Native backdrops have no errors',not bridge.glass.errors)
    check('Frame dashboard and dock have native backdrops',len(bridge.glass.windows)==3)
    if frames():
        frame=frames()[0];native=bridge.glass.dll;user=ctypes.windll.user32;user.GetWindowRect.argtypes=[ctypes.c_void_p,ctypes.c_void_p]
        parts=[]
        for index in range(9):
            handle=native.BackdropPart(int(frame.winId()),index)
            if handle:
                rect=(ctypes.c_long*4)();user.GetWindowRect(handle,rect);parts.append(list(rect))
        check('Frame uses eight bounded native pieces',len(parts)==8)
        center=(frame.x()+frame.width()//2,frame.y()+frame.height()//2)
        check('No native frame window covers desktop center',all(not (r[0]<=center[0]<r[2] and r[1]<=center[1]<r[3]) for r in parts))
        check('No screen-sized native acrylic window exists',all(min(r[2]-r[0],r[3]-r[1])<=45*frame.devicePixelRatio() for r in parts))
    bridge.action('preference',{'key':'glassOpacity','value':.25})

def low_opacity():
    frame=frames()[0];image=frame.grabWindow();check('Transparency slider changes frame alpha',60<=image.pixelColor(2,image.height()//2).alpha()<=67)
    top.grabWindow().save(str(dest/'red-transparency-75.png'))
    bridge.action('preference',{'key':'glassOpacity','value':.85})

def high_opacity():
    frame=frames()[0];image=frame.grabWindow();check('Opacity changes apply immediately',214<=image.pixelColor(2,image.height()//2).alpha()<=220)
    bridge.action('theme',{'mode':'light','dark':False})

def porcelain_check():
    frame=frames()[0];image=frame.grabWindow();check('Porcelain frame opaque',image.pixelColor(2,image.height()//2).alpha()==255)
    check('Fixed modes detach glass',not bridge.glass.windows)
    top.grabWindow().save(str(dest/'porcelain-access.png'))
    bridge.action('theme',{'mode':'dark','dark':True})

def night_check():
    image=frames()[0].grabWindow();check('Night frame opaque',image.pixelColor(2,image.height()//2).alpha()==255)
    top.grabWindow().save(str(dest/'night-access.png'))
    bridge.action('theme',{'mode':'auto'});bridge.action('preference',{'key':'glassOpacity','value':.6})

def final_checks():
    check('Frame windows survive palette changes',bool(handles) and handles==[int(w.winId()) for w in frames()])
    if not frames():check('Frame available for visibility tests',False);return
    bridge.fullscreen_names=(bridge._screen.name(),);bridge.frameChanged.emit();check('Fullscreen hides frame',not frames()[0].isVisible())
    bridge.fullscreen_names=();bridge.frameChanged.emit();bridge.capture.pending=True;bridge.capture.changed.emit();check('Capture hides frame before taking image',not frames()[0].isVisible());bridge.capture.cancel()
    bridge.navigate('settings')
QTimer.singleShot(4000,lambda:apply('red'))
QTimer.singleShot(4900,lambda:save('red'))
QTimer.singleShot(5100,native_checks)
QTimer.singleShot(5500,low_opacity)
QTimer.singleShot(5800,high_opacity)
QTimer.singleShot(6150,porcelain_check)
QTimer.singleShot(6500,night_check)
QTimer.singleShot(6900,final_checks)
def change_thickness(value):bridge.action('preference',{'key':'frameThickness','value':value})
def check_thickness(value):
    frame=frames()[0];image=frame.grabWindow();scale=frame.devicePixelRatio()
    check(str(value)+' px thickness applied',bridge.frameThickness==value and top.y()==bridge.screenY+value)
    check(str(value)+' px frame shape updated',image.pixelColor(round((value-1)*scale),image.height()//2).alpha()>0 and image.pixelColor(round((value+2)*scale),image.height()//2).alpha()==0)
    check(str(value)+' px native geometry refreshed',bridge.glass.windows[int(frame.winId())]['key'][-1]==value)
QTimer.singleShot(7400,lambda:change_thickness(4))
QTimer.singleShot(7700,lambda:check_thickness(4))
QTimer.singleShot(7900,lambda:change_thickness(40))
QTimer.singleShot(8300,lambda:check_thickness(40))
QTimer.singleShot(8500,lambda:change_thickness(16))
QTimer.singleShot(8900,lambda:top.grabWindow().save(str(dest/'theme-settings.png')))
def finish():
    check('No QML warnings',not issues);check('No native glass errors',not bridge.glass.errors);report={'allPassed':all(c['pass'] for c in checks),'checks':checks,'qmlIssues':issues,'nativeErrors':bridge.glass.errors,'palettes':palettes};(dest/'theme-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({'allPassed':report['allPassed'],'checks':checks,'qmlIssues':issues},indent=2));app.quit()
QTimer.singleShot(9300,finish);app.aboutToQuit.connect(bridge.stop);app.exec();raise SystemExit(0 if all(c['pass'] for c in checks) else 1)
