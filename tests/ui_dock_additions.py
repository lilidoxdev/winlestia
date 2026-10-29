# Python3.12 / ui0652.py / mixed-DPI fullscreen, persistent application grid and shared shape checks.
import os,sys,json,ctypes
from pathlib import Path
from unittest.mock import patch
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(Path(os.environ.get('WINLESTIA_FRONTEND',root/'src/frontend'))));os.environ['QT_QPA_PLATFORM']=os.environ.get('WINLESTIA_TEST_PLATFORM','offscreen')
import frontend as f
_mouse_guard=patch.object(f.user32,"GetAsyncKeyState",return_value=0);_mouse_guard.start()
from PySide6.QtCore import QObject,QTimer,QPointF,Qt,QPoint
from PySide6.QtTest import QTest
from interaction import is_fullscreen
report={};original=f.QQmlApplicationEngine.load

def find(item,name):
 if item.objectName()==name:return item
 for child in item.childItems():
  result=find(child,name)
  if result:return result
 return None

def wrapped(engine,url):
 original(engine,url);b=engine.rootContext().contextProperty('Bridge')
 def start():
  state={'settings':{'screenFrame':True,'frameThickness':24,'reducedEffects':True,'desktopLogo':True,'autoTile':True},'taskbarEnabled':True,'taskbarApps':[{'id':str(i),'name':str(i),'pinned':True,'active':False,'hwnd':0} for i in range(3)],'runningApps':[{'id':'fixture','name':'Fixture','background':True,'count':0,'canStop':True,'hwnd':0,'path':'C:/fixture.exe'}]}
  b.update_state({'state':state});b.navigate('settings')
  report['calibrationButton']=find(b.top.contentItem(),'calibrateButton') is not None
  def grid():
   from PySide6.QtWidgets import QApplication
   logos=[w for w in QApplication.allWindows() if w.title()=='Winlestia · Logo' and w.property('screenName')==b._screen.name()]
   report['logoVisible']=len(logos)==1 and logos[0].isVisible()
   report['logoClickThrough']=bool(logos[0].flags() & Qt.WindowTransparentForInput)

   b.dock.setProperty('shown',True);QTest.qWait(300)
   first=find(b.dock.contentItem(),'dock-app-0');other=find(b.dock.contentItem(),'dock-app-1')
   start=first.mapToScene(QPointF(20,20)).toPoint()
   QTest.mousePress(b.dock,Qt.LeftButton,Qt.NoModifier,start)
   QTest.mouseMove(b.dock,start+QPoint(95,0));QTest.qWait(200)
   report['dragLivePreview']=b.dock.property('dropIndex')==2 and other.x()<46
   QTest.mouseRelease(b.dock,Qt.LeftButton,Qt.NoModifier,start+QPoint(95,0));QTest.qWait(200)
   report['dragReleaseOrder']=find(b.dock.contentItem(),'dock-app-0').x()>find(b.dock.contentItem(),'dock-app-2').x()
   report['recycleButton']=find(b.dock.contentItem(),'dockRecycle') is not None
   from PySide6.QtCore import QMimeData,QUrl
   from PySide6.QtGui import QDragEnterEvent,QDropEvent
   binwindow=next(w for w in QApplication.allWindows() if w.title()=='Winlestia · Actions fichiers')
   QApplication.instance().findChild(QObject,'fileDragPoll').stop()
   b._file_drag_active=True;b._file_drag_near=True;b.fileDragChanged.emit();QTest.qWait(50)
   mime=QMimeData();mime.setUrls([QUrl.fromLocalFile('C:/Winlestia test/été.txt')])
   enter=QDragEnterEvent(QPoint(80,40),Qt.MoveAction,mime,Qt.LeftButton,Qt.NoModifier);QApplication.sendEvent(binwindow,enter)
   QTest.qWait(300)
   report['fileShelfReplacesDashboard']=b.top.property('panelHeight')==0 and binwindow.property('hasFiles')
   binwindow.grabWindow().save(str(root/'build/file-shelf-preview.png'))
   event=QDropEvent(QPointF(80,40),Qt.MoveAction,mime,Qt.LeftButton,Qt.NoModifier);QApplication.sendEvent(binwindow,event)
   report['externalFileDropAccepted']=event.isAccepted()
   commands=[]
   while not b.client.commands.empty():commands.append(b.client.commands.get_nowait())
   report['dropSendsLocalPath']=any(c.get('command')=='recycleFiles' and c.get('args',{}).get('paths')==['C:\\Winlestia test\\été.txt'] for c in commands)
   b._file_drag_active=False;b._file_drag_near=False;b.fileDragChanged.emit()
   menu=b.dock.findChild(QObject,'');menu=next(x for x in b.dock.findChildren(QObject) if x.metaObject().indexOfMethod('openAt(QVariant,QVariant,QVariant,QVariant)')>=0)
   menu.openAt('running',{},300,600);menu.setProperty('backgroundTab',True)
   def manage():
    cell=find(menu.contentItem(),'running-cell-fixture');report['backgroundGrid']=cell is not None and cell.width()<80
    QTest.mouseClick(menu,Qt.RightButton,Qt.NoModifier,cell.mapToScene(QPointF(20,20)).toPoint());report['rightClickActions']=menu.property('kind')=='app'
    menu.appAction('stop',0);report['stopConfirmation']=menu.property('confirmStop');menu.appAction('stop',0)
    report['panelRemainsOpen']=menu.isVisible() and menu.property('kind')=='running' and menu.property('backgroundTab')
    report['outsideClickPollingActive']=menu.findChild(QObject,'outsideClickTimer').property('running')
    b._menu_down=False
    with patch.object(f.user32,'GetAsyncKeyState',return_value=0x8000),patch.object(f.QCursor,'pos',return_value=QPoint(-10000,-10000)):
     QTest.qWait(130)
    report['outsideClickCloses']=not menu.isVisible()
    menu.setVisible(False)
    b.fullscreen_names=(b._screen.name(),);b.frameChanged.emit();b.surface_scene.refresh()
    spec=b.surface_scene.data[b._screen.name()]['components'];report['fullscreenHidesLogo']=not logos[0].isVisible();report['fullscreenNoFrame']=spec[0]==0;report['ovalSilhouette']=spec[2] is not None and spec[2][-1] is False
    b.top.grabWindow().save(str(root/'build/personalization0652.png'))
    # Native monitor coordinates intentionally differ from Qt's offscreen geometry.
    def info(handle,pointer):
     value=ctypes.cast(pointer,ctypes.POINTER(f.MonitorInfo)).contents;value.name=b._screen.name();value.monitor[:]=(-1920,-1080,0,0);return 1
    def rect(handle,pointer):ctypes.cast(pointer,ctypes.POINTER(ctypes.c_long*4)).contents[:]=(0,0,1920,1080);return 1
    def origin(handle,pointer):ctypes.cast(pointer,ctypes.POINTER(ctypes.c_long*2)).contents[:]=(-1920,-1080);return 1
    def pid(handle,pointer):ctypes.cast(pointer,ctypes.POINTER(ctypes.c_ulong)).contents.value=999999;return 1
    with patch.object(f.user32,'GetForegroundWindow',return_value=123),patch.object(f.user32,'GetWindowThreadProcessId',side_effect=pid),patch.object(f.user32,'MonitorFromWindow',return_value=1),patch.object(f.user32,'GetMonitorInfoW',side_effect=info),patch.object(f.user32,'IsWindow',return_value=1),patch.object(f.user32,'IsWindowVisible',return_value=1),patch.object(f.user32,'IsIconic',return_value=0),patch.object(f.user32,'GetClassNameW',return_value=0),patch.object(f.user32,'GetClientRect',side_effect=rect),patch.object(f.user32,'ClientToScreen',side_effect=origin),patch.object(f.user32,'GetWindowLongPtrW',return_value=0x00C00000),patch.object(f.user32,'IsZoomed',return_value=1):
     report['mixedDpiBrowserFullscreen']=b.fullscreen()
     with patch.object(f.user32,'GetWindowThreadProcessId',side_effect=lambda h,p:setattr(ctypes.cast(p,ctypes.POINTER(ctypes.c_ulong)).contents,'value',os.getpid())),patch.object(f.user32,'GetForegroundWindow',return_value=456):report['fullscreenRetainedUnderDock']=b.fullscreen()
    b.fullscreen_names=();b.frameChanged.emit();QTimer.singleShot(250,finish)
   QTimer.singleShot(250,manage)
  QTimer.singleShot(500,grid)
 def finish():
  expected=('plugin does not support','QFontDatabase: Cannot find font directory','OpenThemeData() failed')
  report['qmlErrors']=[x for x in f.QML_ISSUES if not any(k in x for k in expected)];report['passed']=all(v for k,v in report.items() if k!='qmlErrors') and not report['qmlErrors'];(root/'build/validation0652.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report))
 QTimer.singleShot(1700,start)
f.QQmlApplicationEngine.load=wrapped;sys.argv=['frontend.py','--preview','--pipe','Winlestia0652Test','--exit-after','5500'];code=f.main();raise SystemExit(code or (0 if report.get('passed') else 1))
