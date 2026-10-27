# Python 3.12 / Windows. Compositor-level regression for the black desktop overlay.
import sys,ctypes,json
from pathlib import Path
from PySide6.QtCore import Qt,QTimer,QUrl
from PySide6.QtGui import QColor
from PySide6.QtWidgets import QApplication,QWidget
from PySide6.QtQuick import QQuickView
root=Path(__file__).resolve().parents[1];dest=root/'validation-8.6';dest.mkdir(exist_ok=True)
app=QApplication([]);app.setQuitOnLastWindowClosed(False);screen=app.primaryScreen();bounds=screen.geometry()
background=QWidget();background.setWindowFlags(Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.WindowDoesNotAcceptFocus|Qt.WindowTransparentForInput);background.setStyleSheet('background:#4585bd;');background.setGeometry(bounds);background.show()
qml=dest/'desktop-fixture.qml';qml.write_text('import QtQuick\nRectangle {color:"transparent"}',encoding='utf-8')
view=QQuickView();view.setColor(QColor('transparent'));view.setFlags(Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.WindowDoesNotAcceptFocus|Qt.WindowTransparentForInput);view.setSource(QUrl.fromLocalFile(str(qml)));view.setGeometry(bounds);view.show();view.raise_()
dll=ctypes.WinDLL(sys.argv[1] if len(sys.argv)>1 else str(root.parent/'research/qt86-glass/QtGlassBackdrop.dll'))
for name,args in [('BackdropAttach',[ctypes.c_void_p]),('BackdropUpdate',[ctypes.c_void_p,ctypes.c_double,ctypes.c_int,ctypes.c_int]),('BackdropFrame',[ctypes.c_void_p,ctypes.c_int,ctypes.c_int]),('BackdropRegion',[ctypes.c_void_p,ctypes.c_void_p]),('BackdropDetach',[ctypes.c_void_p]),('BackdropPart',[ctypes.c_void_p,ctypes.c_int])]:getattr(dll,name).argtypes=args
dll.BackdropPart.restype=ctypes.c_void_p;user=ctypes.windll.user32;user.IsWindowVisible.argtypes=[ctypes.c_void_p];user.GetWindowRect.argtypes=[ctypes.c_void_p,ctypes.c_void_p];user.IsWindow.argtypes=[ctypes.c_void_p]
gdi=ctypes.windll.gdi32;gdi.CreateRectRgn.argtypes=[ctypes.c_int]*4;gdi.CreateRectRgn.restype=ctypes.c_void_p;gdi.DeleteObject.argtypes=[ctypes.c_void_p]
checks=[];report={'checks':checks};h=int(view.winId());baseline=[]
def check(name,value):checks.append({'name':name,'pass':bool(value)})
def parts():return [handle for i in range(9) if (handle:=dll.BackdropPart(h,i))]
def pixels():
    image=screen.grabWindow(0).toImage();points=[(image.width()//2,image.height()//2),(image.width()//3,image.height()//2),(2*image.width()//3,image.height()//2)]
    return [image.pixelColor(x,y).rgba() for x,y in points]
def start():
    baseline.extend(pixels());check('Attach creates no visible helper',dll.BackdropAttach(h)==0 and not parts())
    check('Unconfigured backdrop refuses to show',dll.BackdropUpdate(h,0,True,True)<0)
    rect=gdi.CreateRectRgn(0,0,round(bounds.width()*view.devicePixelRatio()),round(bounds.height()*view.devicePixelRatio()))
    check('Screen-sized generic backdrop rejected',dll.BackdropRegion(h,rect)<0);gdi.DeleteObject(rect)
    configure(4)
def configure(thickness):
    scale=view.devicePixelRatio();check(str(thickness)+' px frame configured',dll.BackdropFrame(h,round(thickness*scale),round(26*scale))==0);check(str(thickness)+' px frame shown',dll.BackdropUpdate(h,0,True,True)==0)
def sample(thickness):
    check(str(thickness)+' px desktop pixels unchanged',pixels()==baseline);check(str(thickness)+' px has eight helpers',len(parts())==8)
    for handle in parts():
        rect=(ctypes.c_long*4)();user.GetWindowRect(handle,rect);check(str(thickness)+' px helper stays bounded',min(rect[2]-rect[0],rect[3]-rect[1])<=round(max(thickness,26)*view.devicePixelRatio())+1)
    image=screen.grabWindow(0).toImage();scale=view.devicePixelRatio();t=round(thickness*scale);r=round(26*scale);baseline_color=QColor.fromRgba(baseline[0]);bad=0
    for origin_x,origin_y in [(t,t),(image.width()-t-r,t),(t,image.height()-t-r),(image.width()-t-r,image.height()-t-r)]:
        for x in range(origin_x+2,origin_x+r-2):
            for y in range(origin_y+2,origin_y+r-2):
                color=image.pixelColor(x,y)
                if color.red()<baseline_color.red()-8 or color.green()<baseline_color.green()-8 or color.blue()<baseline_color.blue()-8:bad+=1
    check(str(thickness)+' px corners contain no black backing',bad==0);report[str(thickness)+'pxBlackCornerPixels']=bad
    image.save(str(dest/('desktop-'+str(thickness)+'.png')))
def invalid():
    sample(16);check('Invalid thickness fails closed',dll.BackdropFrame(h,100000,26)<0 and dll.BackdropUpdate(h,0,True,True)<0);check('Failed geometry hides every helper',all(not user.IsWindowVisible(p) for p in parts()));configure(16);view.hide()
def hidden():
    check('Hidden owner automatically hides glass',all(not user.IsWindowVisible(p) for p in parts()));view.show();check('Show after native auto-hide succeeds',dll.BackdropUpdate(h,0,True,True)==0);check('Show restores every helper',all(user.IsWindowVisible(p) for p in parts()));saved=parts();dll.BackdropDetach(h);check('Detach destroys every helper',all(not user.IsWindow(p) for p in saved));report['allPassed']=all(c['pass'] for c in checks);(dest/('baseline-corner-report.json' if len(sys.argv)>1 else 'desktop-regression-report.json')).write_text(json.dumps(report,indent=2));print(json.dumps(report));app.quit()
QTimer.singleShot(300,start);QTimer.singleShot(550,lambda:sample(4));QTimer.singleShot(650,lambda:configure(40));QTimer.singleShot(900,lambda:sample(40));QTimer.singleShot(1000,lambda:configure(16));QTimer.singleShot(1250,invalid);QTimer.singleShot(1500,hidden)
app.exec();raise SystemExit(0 if report.get('allPassed') else 1)
