# Python3.12 / ui065.py / Qt shortcut recording, icon requests and graphical language controls.
import sys,os,json
from pathlib import Path
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(Path(os.environ.get('WINLESTIA_FRONTEND',root/'src/frontend'))));os.environ['QT_QPA_PLATFORM']='offscreen'
import frontend as f
from PySide6.QtCore import QTimer,QObject,Qt
from PySide6.QtTest import QTest
report={};original=f.QQmlApplicationEngine.load

def wrapped(engine,url):
 original(engine,url);b=engine.rootContext().contextProperty('Bridge')
 def start():
  b.update_state({'state':{'settings':{'shortcut':'Ctrl + F1','paletteShortcut':'Ctrl + F2','reducedEffects':True},'taskbarEnabled':True,'apps':[{'id':'fixture','name':'Fixture','board':'apps'}]}})
  b.navigate('settings')
  def record():
   recorder=b.top.findChild(QObject,'shortcutRecorder');report['recorderFound']=recorder is not None
   recorder.openFor('palette');report['independentPopup']=recorder.isVisible() and not b.top.isVisible()
   QTest.keyClick(recorder,Qt.Key_K,Qt.ControlModifier)
   queued=list(b.client.commands.queue);report['capturesShortcut']=any(x['command']=='shortcut' and x['args'].get('key')==75 and x['args'].get('modifiers')==2 for x in queued)
   b.update_state({'state':{**b._state,'settings':{**b._state['settings'],'paletteShortcut':'Ctrl + K'}},'events':['shortcut-saved:palette']})
   report['autoCloseAndUpdatedLabel']=not recorder.isVisible() and b.top.isVisible() and b.settings['paletteShortcut']=='Ctrl + K' and b.recording_kind is None
   b.iconUrl('fixture',0);b.iconUrl('fixture',0);report['missingIconRequestsCoalesced']=len([x for x in b.client.updates.values() if x['command']=='icon' and x['args'].get('id')=='fixture'])==1
   report['lowMemoryFlag']=b.reducedEffects
   def find(item,name):
    if item.objectName()==name:return item
    for child in item.childItems():
     result=find(child,name)
     if result:return result
    return None
   report['graphicalLanguageChoices']=all(find(b.top.contentItem(),'language-'+mode) is not None for mode in ('auto','fr','en','es'))
   QTimer.singleShot(500,finish)
  QTimer.singleShot(300,record)
 def finish():
  expected=('plugin does not support','QFontDatabase: Cannot find font directory','OpenThemeData() failed')
  report['qmlErrors']=[x for x in f.QML_ISSUES if not any(k in x for k in expected)];report['passed']=all(v for k,v in report.items() if k!='qmlErrors') and not report['qmlErrors']
  (root/'build/validation065.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report))
 QTimer.singleShot(1700,start)
f.QQmlApplicationEngine.load=wrapped;sys.argv=['frontend.py','--preview','--pipe','Winlestia065Test','--exit-after','5000'];code=f.main();raise SystemExit(code or (0 if report.get('passed') else 1))
