# Python 3.12 / Qt 6 / Windows. Render actual QML, stable live delegates, capture pixels and native window lifecycle.
import os,sys,json,ctypes
from pathlib import Path
from types import SimpleNamespace
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
os.environ.setdefault('QSG_RHI_BACKEND','d3d11')
from PySide6.QtCore import QObject,QTimer,QUrl,qInstallMessageHandler
from PySide6.QtGui import QFontDatabase,QImage,QColor
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtQuick import QQuickWindow
from frontend import Bridge,ROOT
from capture_native import render_capture

app=QApplication([]);app.setQuitOnLastWindowClosed(False)
for font in (ROOT/'assets/fonts').glob('*.ttf'):QFontDatabase.addApplicationFont(str(font))
issues=[];qInstallMessageHandler(lambda kind,context,message:issues.append(message))
bridge=Bridge(SimpleNamespace(pipe='PanneauxTools82Test',backend_pid=0,preview=False));bridge.pointer.stop();bridge.city=''
dest=ROOT/'validation-8.2';dest.mkdir(exist_ok=True)
devices=[{'id':'output','name':'Haut-parleurs · Realtek Audio','selected':True,'volume':72,'muted':False,'flow':0},{'id':'headset','name':'Casque Bluetooth','selected':False,'volume':50,'muted':False,'flow':0},{'id':'input','name':'Microphone · Realtek Audio','selected':True,'volume':80,'muted':False,'flow':1}]
sessions=[{'id':'spotify','name':'Spotify','pid':10,'volume':42,'muted':False,'playing':True},{'id':'edge','name':'Microsoft Edge','pid':20,'volume':66,'muted':False,'playing':True},{'id':'system','name':'Sons système','pid':0,'volume':30,'muted':True,'playing':False}]
state={'settings':{'motionScale':1,'clipboardHistory':True},'controls':{'volume':72,'muted':False,'brightnessAvailable':True,'brightness':63,'network':'Connecté','wifiOn':True,'bluetoothOn':True},'audioMixer':{'devices':devices,'applications':sessions,'running':True},'dark':False,'clipboard':[]}
bridge.update_state({'state':state})
engine=QQmlApplicationEngine();engine.addImportPath(str(ROOT/'qml'));engine.addImageProvider('capture',bridge.capture.provider);engine.rootContext().setContextProperty('Bridge',bridge);engine.load(QUrl.fromLocalFile(str(ROOT/'qml/Main.qml')))
if not engine.rootObjects():bridge.stop();print(json.dumps(issues));raise SystemExit(1)
top=engine.rootObjects()[0];bridge.top=top;bridge._window=top;bridge.dock=top.findChild(QObject,'bottomDock');bridge.windowChanged.emit()
checks=[];jobs=[]
def check(name,ok):checks.append({'name':name,'pass':bool(ok)})
def later(delay,call):jobs.append((delay,call))
def save(name):
    check(name+' window visible',top.isVisible());top.grabWindow().save(str(dest/(name+'.png')))
for i,mode in enumerate(['tools','audio','microphone','capture','clipboard','controls','mixer']):
    later(100+i*1100,lambda mode=mode:bridge.navigate(mode))
    later(950+i*1100,lambda mode=mode:save(mode))
later(7900,lambda:bridge.navigate('controls'))
later(8300,lambda:top.setProperty('mixerOpen',True))
later(9300,lambda:save('controls-mixer'))
def stability():
    def visual_find(item,name):
        if item.objectName()==name:return item
        for child in item.childItems():
            found=visual_find(child,name)
            if found:return found
    lst=visual_find(top.contentItem(),'audioSessions');before=lst.property('contentItem').childItems() if lst else []
    state['audioMixer']['applications']=[{**r,'volume':47} for r in sessions];bridge.update_state({'state':state})
    after=lst.property('contentItem').childItems() if lst else []
    check('Live updates preserve audio delegates',bool(before) and before==after)
later(9500,stability)
later(9700,lambda:bridge.action('theme',{'dark':True}))
later(10500,lambda:save('controls-mixer-dark'))
def capture():
    image=QImage(1000,600,QImage.Format_ARGB32);image.fill(QColor('#ece9df'));bridge.capture.provider.image=image;bridge.capture.logical=(1000,600);bridge.capture.active=True;bridge.capture.revision+=1;bridge.capture.changed.emit()
    capture=top.findChild(QObject,'captureWindow');check('Capture independent of hidden dashboard',capture.isVisible() and not top.isVisible())
    capture.setProperty('selection',__import__('PySide6.QtCore',fromlist=['QRectF']).QRectF(100,100,600,300));capture.setProperty('selected',True)
    result=render_capture(image,{'x':100,'y':100,'width':600,'height':300},[{'points':[{'x':120,'y':120},{'x':220,'y':120}],'color':'#ff0000','width':4}],(1000,600))
    check('Capture crop and annotation agree in pixels',result.width()==600 and result.height()==300 and result.pixelColor(40,20).red()>240 and result.pixelColor(40,20).green()<10)
later(10600,bridge.closePanel)
later(11200,capture)
def capture_frame():
    capture=top.findChild(QObject,'captureWindow');frame=capture.grabWindow();frame.save(str(dest/'capture-editor.png'))
    check('Capture provider displays the frozen image',frame.pixelColor(200,200).red()>200)
    bridge.capture.cancel();check('Capture cancel clears image',bridge.capture.provider.image.isNull() and not capture.isVisible())
later(11700,capture_frame)
def finish():
    report={'allPassed':all(c['pass'] for c in checks) and not issues,'checks':checks,'qmlIssues':issues[:]};(dest/'tools-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2));app.quit()
later(12300,finish)
for delay,call in jobs:QTimer.singleShot(delay,call)
app.aboutToQuit.connect(bridge.stop);app.exec();raise SystemExit(0 if all(c['pass'] for c in checks) and not issues else 1)
