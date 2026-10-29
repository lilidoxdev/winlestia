# Python 3.12 / prefs_shots.py / offscreen screenshots of the Personnaliser panel (preview bridge, isolated pipe, hard 60 s timeout).
# usage: python prefs_shots.py OUTDIR PREFIX [WxH] [pages]   e.g. prefs_shots.py out prefs-red 1920x1080 0,1,2,3
import sys,os,json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
out=Path(sys.argv[1]);prefix=sys.argv[2];size=sys.argv[3] if len(sys.argv)>3 else '1920x1080';pages=[int(x) for x in (sys.argv[4] if len(sys.argv)>4 else '-1').split(',')]
sys.path.insert(0,str(Path(os.environ.get('WINLESTIA_FRONTEND',root/'src/frontend'))))
os.environ['QT_QPA_PLATFORM']='offscreen'
import threading;threading.Timer(60,lambda:os._exit(3)).start()
import frontend as f
from PySide6.QtCore import QTimer,QObject,QRect
from PySide6.QtGui import QScreen
W,H=(int(x) for x in size.split('x'))
QScreen.geometry=lambda s:QRect(0,0,W,H);QScreen.availableGeometry=lambda s:QRect(0,0,W,H)
from PySide6.QtTest import QTest
PAL={'red':{'background':'#fff8f6','text':'#261916','primary':'#c0152f','secondary':'#8a4a50','tertiary':'#7a5a30'},'teal':{'background':'#0e1a1a','text':'#dff2f0','primary':'#2fbfb0','secondary':'#5a8f8a','tertiary':'#7aa0c0'}}
colour='teal' if 'teal' in prefix else 'red'
report={};original=f.QQmlApplicationEngine.load
SETTINGS={'themeMode':'custom','customPalette':PAL[colour],'glassOpacity':.35,'reducedEffects':False,'autoTile':True,'desktopLogo':False,'screenFrame':True,'frameThickness':16,'iconSize':56,'topActivationMargin':2,'accentBorders':True,'explorerTheme':True,'filesDefaultHandler':False,'filesPreload':False,'autostart':True,'clipboardHistory':True,'shortcut':'Ctrl + F1','paletteShortcut':'Ctrl + F2'}
def wrapped(engine,url):
 original(engine,url);b=engine.rootContext().contextProperty('Bridge')
 def run():
  b.update_state({'state':{'settings':SETTINGS,'taskbarEnabled':True}});b.navigate('settings')
  seq=list(pages)
  def step():
   if not seq:finish();return
   p=seq.pop(0);pr=b.top.findChild(QObject,'shellPreferences')
   if p>=0 and pr is not None:pr.setProperty('tab',p)
   def snap():
    name=f'{prefix}-{p}.png' if p>=0 else f'{prefix}.png'
    b.top.grabWindow().save(str(out/name));report[name]=[b.top.width(),b.top.height(),b.screenWidth,b.screenHeight];step()
   QTimer.singleShot(900,snap)
  step()
 def finish():
  expected=('plugin does not support','QFontDatabase: Cannot find font directory','OpenThemeData() failed')
  report['qmlErrors']=[x for x in f.QML_ISSUES if not any(k in x for k in expected)];print(json.dumps(report),flush=True);os._exit(0 if not report['qmlErrors'] else 2)
 QTimer.singleShot(1800,run)
f.QQmlApplicationEngine.load=wrapped;sys.argv=['frontend.py','--preview','--pipe','WinlestiaPrefsShots'+prefix];code=f.main();raise SystemExit(code)
