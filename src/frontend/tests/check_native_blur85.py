# Python 3.12 / Native compositing verification over a deterministic checkerboard.
import os,sys,ctypes,json
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PySide6.QtCore import Qt,QTimer,QUrl
from PySide6.QtWidgets import QApplication,QWidget
from PySide6.QtGui import QPainter,QColor
from PySide6.QtQuick import QQuickView
from PIL import Image,ImageStat
root=Path(__file__).resolve().parents[1];dest=root/'validation-8.5';app=QApplication([]);app.setQuitOnLastWindowClosed(False)
class Pattern(QWidget):
    def paintEvent(self,event):
        p=QPainter(self)
        for x in range(0,self.width(),8):p.fillRect(x,0,8,self.height(),QColor('#ffffff' if (x//8)%2 else '#000000'))
background=Pattern();background.setWindowFlags(Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.WindowDoesNotAcceptFocus);background.setGeometry(120,300,500,220);background.show()
qml=dest/'blur-fixture.qml';qml.write_text('import QtQuick\nRectangle {width:500;height:220;color:"#60100c0c"}',encoding='utf-8')
view=QQuickView();view.setColor(QColor('transparent'));view.setFlags(Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.WindowDoesNotAcceptFocus);view.setSource(QUrl.fromLocalFile(str(qml)));view.setGeometry(120,300,500,220);view.show();view.raise_()
dll=ctypes.WinDLL(str(root/'QtGlassBackdrop.dll'))
for name,args in [('BackdropAttach',[ctypes.c_void_p]),('BackdropUpdate',[ctypes.c_void_p,ctypes.c_double,ctypes.c_int,ctypes.c_int]),('BackdropDetach',[ctypes.c_void_p]),('BackdropRegion',[ctypes.c_void_p,ctypes.c_void_p]),('BackdropHandle',[ctypes.c_void_p])]:getattr(dll,name).argtypes=args
dll.BackdropHandle.restype=ctypes.c_void_p
report={}
def capture(name):
    app.primaryScreen().grabWindow(0,120,300,500,220).save(str(dest/(name+'.png')))
    image=Image.open(dest/(name+'.png')).convert('L');report[name+'StdDev']=ImageStat.Stat(image.crop((120,70,380,150))).stddev[0]
def attach():
    capture('blur-before');h=int(view.winId());report['attach']=dll.BackdropAttach(h);gdi=ctypes.windll.gdi32;gdi.CreateRectRgn.restype=ctypes.c_void_p;gdi.DeleteObject.argtypes=[ctypes.c_void_p];region=gdi.CreateRectRgn(0,0,round(view.width()*view.devicePixelRatio()),round(view.height()*view.devicePixelRatio()));report['region']=dll.BackdropRegion(h,region);gdi.DeleteObject(region);report['update']=dll.BackdropUpdate(h,0,True,True);report['handle']=dll.BackdropHandle(h)
    user=ctypes.windll.user32;user.IsWindowVisible.argtypes=[ctypes.c_void_p];report['nativeVisible']=bool(user.IsWindowVisible(report['handle']));view.raise_()
    user.GetWindow.argtypes=[ctypes.c_void_p,ctypes.c_uint];user.GetWindow.restype=ctypes.c_void_p
    user.GetTopWindow.argtypes=[ctypes.c_void_p];user.GetTopWindow.restype=ctypes.c_void_p
    user.GetWindowThreadProcessId.argtypes=[ctypes.c_void_p,ctypes.POINTER(ctypes.c_ulong)]
    user.GetWindowRect.argtypes=[ctypes.c_void_p,ctypes.c_void_p]
    order=[];current=user.GetTopWindow(None);i=0
    while current and i<200:
        pid=ctypes.c_ulong();user.GetWindowThreadProcessId(current,ctypes.byref(pid))
        if pid.value==os.getpid():
            name=ctypes.create_unicode_buffer(200);user.GetClassNameW(ctypes.c_void_p(current),name,200);rect=(ctypes.c_long*4)();user.GetWindowRect(current,rect);order.append({'hwnd':current,'class':name.value,'rect':list(rect),'z':i})
        current=user.GetWindow(current,2);i+=1
    report['zOrder']=order;report['qtHandles']={'top':h,'background':int(background.winId())}

def finish():
    capture('blur-after');report['allPassed']=report['blur-afterStdDev']<report['blur-beforeStdDev']*.6
    dll.BackdropDetach(int(view.winId()));(dest/'native-blur-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report));app.quit()
QTimer.singleShot(500,attach);QTimer.singleShot(1500,finish);app.exec();raise SystemExit(0 if report.get('allPassed') else 1)
