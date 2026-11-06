# Offscreen check of the custom palette editor (qml/CustomThemeEditor.qml + ThemePreviewMock.qml).
# Loads it with the real Bridge, changes colours, asserts the live preview follows, no QML warnings, optional PNG.
# Usage: python check_theme_editor.py [OUT_PNG]
import os,sys,json,tempfile
os.environ.setdefault('QT_QPA_PLATFORM','offscreen')
os.environ.setdefault('WINLESTIA_DATA_DIR',tempfile.mkdtemp(prefix='wl-theme-'))
from pathlib import Path
from types import SimpleNamespace
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PySide6.QtCore import QTimer,QUrl,qInstallMessageHandler
from PySide6.QtGui import QFontDatabase
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
import frontend
from frontend import Bridge,ROOT
from localization import Translator
from custom_palette import make_palette
import faulthandler;faulthandler.dump_traceback_later(int(os.environ.get('WL_DUMP','0')) or 10**6,exit=True)
OUT=Path(sys.argv[1]) if len(sys.argv)>1 else None
app=QApplication([]);app.setQuitOnLastWindowClosed(False);issues=[];checks={}
qInstallMessageHandler(lambda kind,context,message:issues.append(message) if int(kind) in (1,2,3) and 'QFontDatabase' not in message and 'window masks' not in message else None)
for font in (ROOT/'assets/fonts').glob('*.ttf'):QFontDatabase.addApplicationFont(str(font))
frontend.TRANSLATOR=Translator(tempfile.mkdtemp(prefix="wl-lang-"))
bridge=Bridge(SimpleNamespace(pipe='WinLestiaThemeEditorTest',backend_pid=0,preview=True));bridge.pointer.stop();bridge.city=''
class Noop:
    def __getattr__(self,n):return lambda *a,**k:None
bridge.top=Noop()
bridge.update_state({'state':{'settings':{'themeMode':'custom','customPalette':{'background':'#fff8f6','text':'#261916','primary':'#895143','secondary':'#775b52','tertiary':'#68613c'},'customGlass':False,'customGlassOpacity':.12}}})
HARNESS=b'''import QtQuick
import QtQuick.Window
Window{
 width:1200;height:760;color:"#222";visible:true
 property alias ed:ed
 CustomThemeEditor{id:ed}
}'''
def items(item):
    yield item
    for c in item.childItems():yield from items(c)
def find(win,name):return next((i for i in items(win.contentItem()) if i.objectName()==name),None)
def run():
    e=QQmlApplicationEngine();e.addImportPath(str(ROOT/'qml'));e.rootContext().setContextProperty('Bridge',bridge);bridge.engine=e
    e.loadData(HARNESS,QUrl.fromLocalFile(str(ROOT/'qml'/'EditorHarness.qml')))
    root=e.rootObjects()[0] if e.rootObjects() else None
    checks['loads']=root is not None
    if root is None:finish();return
    yield 700
    from PySide6.QtCore import QMetaObject,Q_ARG,QObject
    ed=root.findChild(QObject,'customThemeStudio')
    QMetaObject.invokeMethod(ed,'openEditor');yield 700
    pv=next(i for i in items(ed.contentItem()) if i.objectName()=='themePreview')
    cal=next(i for i in items(pv) if i.objectName()=='previewCalendar')
    today=next((i for i in items(pv) if i.objectName()=='previewToday'),None)
    media=next(i for i in items(pv) if i.objectName()=='previewMedia')
    checks['hasToday']=today is not None
    pal=make_palette(ed.property('draft').toVariant())['palette']
    same=lambda a,b:a.name().lower()==str(b).lower()
    checks['mapSurface']=same(media.property('color'),pal['m3surfaceContainer'])
    checks['mapTodayPrimary']=today is not None and same(today.property('color'),pal['m3primary'])
    before=today.property('color').name()
    # Edit "Principal" (index 2) through the real controls: pick role, then type hex.
    from PySide6.QtCore import QPointF
    ed.setProperty('selected',2);yield 200
    QMetaObject.invokeMethod(ed,'putColor',Q_ARG('QString','#2255cc'),Q_ARG(bool,True));yield 300
    after=today.property('color').name()
    checks['previewTodayChanged']=before!=after and after=='#2255cc'
    checks['swatchChanged']=find(ed,'custom-swatch-primary').property('color').name()=='#2255cc'
    pal=make_palette(ed.property('draft').toVariant())['palette']
    checks['mapMatchesPython']=same(today.property('color'),pal['m3primary'])
    # Background edit changes the media card (surfaceContainer is derived from it).
    cb=media.property('color').name()
    ed.setProperty('selected',0);yield 100
    QMetaObject.invokeMethod(ed,'putColor',Q_ARG('QString','#14181c'),Q_ARG(bool,True));yield 300
    pal=make_palette(ed.property('draft').toVariant())['palette']
    checks['bgDerivedMapping']=media.property('color').name()!=cb and same(media.property('color'),pal['m3surfaceContainer'])
    ed.setProperty('glass',True);yield 200
    checks['glassAlpha']=abs(pv.childItems()[0].property('color').alphaF()-0.12)<0.02
    for n in ('customHex','customEyedropper','customGlassSwitch','customTransparency','applyCustomTheme','customColorPicker','customClarity'):
        checks['has:'+n]=find(ed,n) is not None
    # Eyedropper sits right of the hex field
    h,eye=find(ed,'customHex'),find(ed,'customEyedropper')
    checks['eyedropperBesideHex']=eye.mapToItem(None,0,0).x()>=h.mapToItem(None,h.width(),0).x()-1 and abs(eye.mapToItem(None,0,eye.height()/2).y()-h.mapToItem(None,0,h.height()/2).y())<8
    if OUT:
        OUT.parent.mkdir(parents=True,exist_ok=True)
        # Offscreen screen is 800x600: first the stacked (narrow) layout, then the real 1366x768 size.
        ed.setProperty('glass',True);ed.setProperty('selected',0)
        QMetaObject.invokeMethod(ed,'putColor',Q_ARG('QString','#14181c'),Q_ARG(bool,True))
        ed.setProperty('selected',1);QMetaObject.invokeMethod(ed,'putColor',Q_ARG('QString','#e9e1dd'),Q_ARG(bool,True));yield 300
        ed.setProperty('glass',False);yield 200;ed.grabWindow().save(str(OUT.with_name(OUT.stem+'-narrow.png')))
        ed.setProperty('glass',True);ed.setProperty('width',1120);ed.setProperty('height',720);yield 600
        fl=find(ed,'customSide')
        checks['sideFitsNoScroll']=fl.property('contentHeight')<=fl.height()+1
        checks['wideSideVisible']=find(ed,'customHex').property('visible') and find(ed,'applyCustomTheme').mapToItem(None,0,0).y()<720
        ed.grabWindow().save(str(OUT))
    finish()
def finish():
    checks['noQmlWarnings']=not issues
    ok=all(checks.values())
    print(json.dumps({'checks':checks,'issues':issues[:10],'allPassed':ok}),flush=True)
    bridge.stop();os._exit(0 if ok else 1)
gen=run()
def drive():
    try:d=next(gen)
    except StopIteration:return
    except BaseException:
        import traceback;traceback.print_exc();os._exit(2)
    QTimer.singleShot(d,drive)
QTimer.singleShot(300,drive);app.exec()
