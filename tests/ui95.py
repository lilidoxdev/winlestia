# Python 3.12 / ui95.py / Qt Quick validation with isolated IPC and synthetic application state.
import sys,os,json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
front=Path(os.environ.get('WINLESTIA_FRONTEND',root/'src/frontend'))
sys.path.insert(0,str(front));os.environ['QT_QPA_PLATFORM']='offscreen'
import frontend as f
from PySide6.QtCore import QTimer,QObject,QPointF,Qt
from PySide6.QtTest import QTest
report={};load=f.QQmlApplicationEngine.load

def wrapped(engine,url):
 load(engine,url);b=engine.rootContext().contextProperty('Bridge')
 def check():
  b.update_state({'state':{'settings':{'screenFrame':True,'frameThickness':16,'topActivationMargin':50,'dockWidth':900},'taskbarEnabled':True,'taskbarApps':[{'id':i,'name':i,'pinned':True,'active':False,'hwnd':0} for i in ('a','b')], 'runningApps':[{'id':'a','name':'Test','background':False,'count':1},{'id':'bg','name':'Background','background':True,'count':0}],'media':{'available':True,'app':'Spotify','playing':True,'title':'Validation','duration':180,'position':45},'controls':{'volume':37,'brightness':42,'brightnessAvailable':True}}})
  b.openTab(1);report['rootLoaded']=bool(engine.rootObjects())
  def nextcheck():
   dock=b.dock;report['dockCentered']=abs(dock.x()-(b.screenX+(b.screenWidth-dock.width())/2))<=1
   def find(item,name):
    if item.objectName()==name:return item
    for child in item.childItems():
     found=find(child,name)
     if found:return found
    return None
   item=find(dock.contentItem(),'dock-app-a')
   if item is None:
    report['missingDelegate']=list(f.QML_ISSUES);finish();return
   start=item.mapToScene(QPointF(20,20)).toPoint()
   QTest.mousePress(dock,Qt.LeftButton,Qt.NoModifier,start);QTest.mouseMove(dock,start+QPointF(48,0).toPoint(),40);QTest.mouseRelease(dock,Qt.LeftButton,Qt.NoModifier,start+QPointF(48,0).toPoint())
   report['dragEnqueuesReorder']=any(v.get('command')=='dockApp' and v['args'].get('action')=='reorder' for v in list(b.client.commands.queue))
   menu=next(x for x in dock.findChildren(QObject) if x.metaObject().indexOfMethod('openAt(QVariant,QVariant,QVariant,QVariant)')>=0)
   menu.openAt('running',{},500,600);menu.setProperty('backgroundTab',True);report['backgroundTab']=menu.property('backgroundTab')
   menu.openAt('power',{},500,600);report['powerAwaitsConfirmation']=menu.property('powerChoice')==''
   b.navigate('settings');b.action('volume',{'value':26});b.update_state({'state':{**b._state,'controls':{'volume':100,'brightness':42}}});report['sliderStaleGuard']=b._state['controls']['volume']==26
   QTimer.singleShot(600,finish)
  QTimer.singleShot(700,nextcheck)
 def finish():
  expected=('plugin does not support','QFontDatabase: Cannot find font directory','OpenThemeData() failed')
  errors=[x for x in f.QML_ISSUES if not any(t in x for t in expected)]
  report['qmlErrors']=errors;report['passed']=all(v for k,v in report.items() if k!='qmlErrors') and not errors
  (root/'build/validation95.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report))
 QTimer.singleShot(1600,check)
f.QQmlApplicationEngine.load=wrapped
sys.argv=['frontend.py','--preview','--pipe','Winlestia95Validation','--exit-after','5500']
code=f.main()
raise SystemExit(code if code else 0 if report.get('passed') else 1)
