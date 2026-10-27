# Python3.12 / custom07.py / custom palette, editor, glass modes and deterministic screen sampling.
import sys,os,json,ctypes
from pathlib import Path
from unittest.mock import patch
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(Path(os.environ.get('WINLESTIA_FRONTEND',root/'src/frontend'))));os.environ['QT_QPA_PLATFORM']='offscreen'
from custom_palette import make_palette,DEFAULT,valid_color
assert make_palette()['palette']['m3surface']=='#fff8f6'
assert make_palette({'primary':'invalid'})['palette']['m3primary']==DEFAULT['primary']
assert not valid_color('#1234') and valid_color('#abcdef')
import frontend as f
from PySide6.QtCore import QTimer,QObject
from PySide6.QtTest import QTest
report={};original=f.QQmlApplicationEngine.load

def wrapped(engine,url):
 original(engine,url);b=engine.rootContext().contextProperty('Bridge')
 def run():
  b.update_state({'state':{'settings':{'themeMode':'custom','customPalette':DEFAULT,'customGlass':False,'customGlassOpacity':.2,'reducedEffects':True},'dark':False,'taskbarEnabled':True}});b.navigate('settings')
  studio=b.top.findChild(QObject,'customThemeStudio');studio.openEditor();studio.setProperty('selected',2);studio.putColor('#ff315b',True)
  report['independentEditor']=studio.isVisible() and not b.top.isVisible()
  QTest.qWait(250)
  studio.grabWindow().save(str(root/'build/custom-studio07.png'))
  b.action('customTheme',{'colors':{**DEFAULT,'primary':'#ff315b'},'glass':False,'opacity':.2})
  report['fiveRolePalette']=b.adaptivePalette['m3primary']=='#ff315b' and b.adaptivePalette['m3surface']==DEFAULT['background']
  report['opaqueMode']=not b.glassEnabled and not b.dark
  b.action('customTheme',{'colors':{**DEFAULT,'primary':'#ff315b'},'glass':True,'opacity':.2});report['glassMode']=b.glassEnabled and b.glassOpacity==.2
  studio.setVisible(False);b.startColorPick();b._pick_after=0
  b._click_picker.choice=(0,0)
  with patch.object(ctypes.windll.gdi32,'GetPixel',return_value=0x563412),patch.object(f.user32,'GetAsyncKeyState',side_effect=lambda key:0x8000 if key==1 else 0):b.sampleColor()
  draft=studio.property('draft');draft=draft.toVariant() if hasattr(draft,'toVariant') else draft
  report['eyedropper']=not b.picking and studio.isVisible() and draft['primary']=='#123456'
  studio.setVisible(False);b.startColorPick();b._pick_after=0
  with patch.object(ctypes.windll.gdi32,'GetPixel',return_value=0xffffff),patch.object(f.user32,'GetAsyncKeyState',side_effect=lambda key:0x8000 if key==27 else 0):b.sampleColor()
  draft=studio.property('draft');draft=draft.toVariant() if hasattr(draft,'toVariant') else draft;report['cancelSampling']=draft['primary']=='#123456' and not b.picking
  studio.finish();QTimer.singleShot(700,finish)
 def finish():
  expected=('plugin does not support','QFontDatabase: Cannot find font directory','OpenThemeData() failed')
  report['qmlErrors']=[x for x in f.QML_ISSUES if not any(k in x for k in expected)];report['passed']=all(v for k,v in report.items() if k!='qmlErrors') and not report['qmlErrors'];(root/'build/validation07.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report))
 QTimer.singleShot(1800,run)
f.QQmlApplicationEngine.load=wrapped;sys.argv=['frontend.py','--preview','--pipe','Winlestia07Test','--exit-after','5500'];code=f.main();raise SystemExit(code or (0 if report.get('passed') else 1))
