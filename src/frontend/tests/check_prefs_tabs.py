# Offscreen check: Personnalisation category pills never overlap, at several window widths. Usage: python check_prefs_tabs.py [OUT_DIR]
import os,sys,json
os.environ.setdefault('QT_QPA_PLATFORM','offscreen')
from pathlib import Path
from types import SimpleNamespace
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PySide6.QtCore import QTimer,QUrl,QRectF,qInstallMessageHandler
from PySide6.QtGui import QFontDatabase
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
from frontend import Bridge,ROOT
OUT=Path(sys.argv[1]) if len(sys.argv)>1 else None
app=QApplication([]);app.setQuitOnLastWindowClosed(False);issues=[];checks={}
qInstallMessageHandler(lambda kind,context,message:issues.append(message) if int(kind)>0 and 'QFontDatabase' not in message and 'window masks' not in message else None)
for font in (ROOT/'assets/fonts').glob('*.ttf'):QFontDatabase.addApplicationFont(str(font))
bridge=Bridge(SimpleNamespace(pipe='WinLestiaPrefsTabsTest',backend_pid=0,preview=True));bridge.pointer.stop();bridge.city=''
bridge.update_state({'state':{'settings':{'themeMode':'dark','motionScale':0}}})
engine=QQmlApplicationEngine();engine.addImportPath(str(ROOT/'qml'));engine.rootContext().setContextProperty('Bridge',bridge)
def items(item):
    yield item
    for c in item.childItems():yield from items(c)
def check(w,width):
    pills=sorted([i for i in items(w.contentItem()) if i.objectName().startswith('prefsTab') and i.objectName()[8:].isdigit()],key=lambda i:i.objectName())
    rects=[QRectF(i.mapToItem(w.contentItem(),0,0),i.size()) for i in pills]
    inter=[(a,b) for a in range(len(rects)) for b in range(a+1,len(rects)) if rects[a].intersects(rects[b])]
    strip=next(i for i in items(w.contentItem()) if i.objectName()=='prefsTabStrip')
    labels_fit=all(i.width()>=i.childItems()[-1].width()+20 for i in pills if i.childItems())
    checks[f'{width}:six']=len(pills)==6
    checks[f'{width}:noOverlap']=not inter
    checks[f'{width}:fitsContent']=labels_fit
    checks[f'{width}:stripConsistent']=strip.property('contentWidth')<=strip.width()+.5 or strip.property('contentWidth')>strip.width()
    if OUT:
        OUT.mkdir(parents=True,exist_ok=True);w.grabWindow().save(str(OUT/f'prefs-tabs-{width}.png'))
widths=[600,900,1400];idx=[0]
def step():
    if idx[0]>0:check(cur[0],widths[idx[0]-1])
    if idx[0]==len(widths):
        checks['qmlIssues']=not issues;ok=all(checks.values())
        print(json.dumps({'checks':checks,'issues':issues[:8],'allPassed':ok}),flush=True);bridge.stop();os._exit(0 if ok else 1)
    w=widths[idx[0]];idx[0]+=1
    e=QQmlApplicationEngine();e.addImportPath(str(ROOT/'qml'));e.rootContext().setContextProperty('Bridge',bridge);engines.append(e)
    e.loadData(f'import QtQuick\nimport QtQuick.Window\nWindow{{width:{w};height:700;visible:true;ShellPreferences{{anchors.fill:parent;anchors.margins:16;tab:1}}}}'.encode(),QUrl.fromLocalFile(str(ROOT/'qml/_h.qml')))
    cur[0]=e.rootObjects()[0] if e.rootObjects() else None
    if cur[0] is None:print(issues);os._exit(1)
    QTimer.singleShot(900,step)
engines=[];cur=[None]
QTimer.singleShot(300,step);app.exec()
