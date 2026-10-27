# Python 3.12 / surface_geometry.py / Qt 6. One filled silhouette owns every shell background.
from PySide6.QtCore import QObject,QTimer,QRectF,Qt
from PySide6.QtGui import QPainterPath,QTransform,QRegion
from functools import lru_cache
from PySide6.QtWidgets import QApplication


def panel_path(width,height,attached=True):
    p=QPainterPath()
    if width<1 or height<1:return p
    if not attached:
        p.addRoundedRect(QRectF(20,0,max(1,width-40),height),24,24);return p
    shoulder=min(20,height/2);radius=min(25,height/2)
    p.moveTo(0,0);p.lineTo(width,0);p.quadTo(width-20,0,width-20,shoulder)
    p.lineTo(width-20,height-radius);p.quadTo(width-20,height,width-20-radius,height)
    p.lineTo(20+radius,height);p.quadTo(20,height,20,height-radius)
    p.lineTo(20,shoulder);p.quadTo(20,0,0,0);p.closeSubpath();return p


def dock_path(width,height,attached=True):
    p=QPainterPath()
    if width<1 or height<1:return p
    if not attached:p.addRoundedRect(QRectF(4,4,width-8,height-8),(height-8)/2,(height-8)/2);return p
    r=min(26,height/2);p.moveTo(0,height);p.quadTo(r,height,r,height-r);p.lineTo(r,r)
    p.quadTo(r,0,2*r,0);p.lineTo(width-2*r,0);p.quadTo(width-r,0,width-r,r)
    p.lineTo(width-r,height-r);p.quadTo(width-r,height,width,height);p.closeSubpath();return p


@lru_cache(maxsize=24)
def frame_path(width,height,thickness):
    outer=QPainterPath();outer.addRect(QRectF(0,0,width,height));inner=QPainterPath();inner.addRoundedRect(QRectF(thickness,thickness,width-2*thickness,height-2*thickness),26,26)
    outer.addPath(inner.toReversed());outer.setFillRule(Qt.WindingFill);return outer


def shell_path(width,height,thickness,panel=None,dock=None):
    path=QPainterPath(frame_path(width,height,thickness)) if thickness else QPainterPath()
    path.setFillRule(Qt.WindingFill)
    for spec,builder in ((panel,panel_path),(dock,dock_path)):
        if spec:
            x,y,w,h,attached=spec;path.addPath(QTransform().translate(x,y).map(builder(w,h,attached)))
    return path


def path_commands(path,scale=1):
    path=QTransform().scale(scale,scale).map(path);commands=[];index=0
    while index<path.elementCount():
        point=path.elementAt(index)
        if point.isMoveTo():commands.append((0,point.x,point.y,0,0,0,0))
        elif point.isLineTo():commands.append((1,point.x,point.y,0,0,0,0))
        elif point.isCurveTo():
            second=path.elementAt(index+1);last=path.elementAt(index+2)
            commands.append((2,point.x,point.y,second.x,second.y,last.x,last.y));index+=2
        index+=1
    return commands


def svg_path(path):
    result=[]
    for kind,*points in path_commands(path):
        letter=('M','L','C')[kind];count=6 if kind==2 else 2
        result.append(letter+' '.join(format(v,'.5f').rstrip('0').rstrip('.') or '0' for v in points[:count]))
    return ' '.join(result)


class SurfaceScene(QObject):
    def __init__(self,bridge):
        super().__init__(bridge);self.bridge=bridge;self.paths={};self.data={};self.signature=None;self.queued=False;self.revision=0;self.stopped=False;self.input_masks={}
        bridge.changed.connect(self.request);bridge.geometryChanged.connect(self.request);bridge.screensChanged.connect(self.request);bridge.frameChanged.connect(self.request);bridge.capture.changed.connect(self.request)
        for window in (bridge.top,bridge.dock):
            if window:
                for signal in (window.xChanged,window.yChanged,window.widthChanged,window.heightChanged,window.visibleChanged):signal.connect(self.request)
        self.timer=QTimer(self);self.timer.setInterval(200);self.timer.timeout.connect(self.refresh);self.timer.start();self.request()
    def request(self,*args):
        if not self.stopped and not self.queued:self.queued=True;QTimer.singleShot(0,self.refresh)
    def refresh(self):
        self.queued=False
        if self.stopped:return
        bridge=self.bridge
        def view(window,dock=False):
            if not window or not window.isVisible():return None
            offset=float(window.property('offset') or 0) if dock else 0
            width=float(window.property('panelWidth') or window.width()) if not dock else window.width()
            height=float(window.property('panelHeight') or window.height()) if not dock else window.height()
            return (window.x()+(window.width()-width)/2,window.y()+offset,width,height,bridge.frameEnabled and not bridge.screenFullscreen)
        top=view(bridge.top);dock=view(bridge.dock,True)
        for window,spec,builder,is_dock in ((bridge.top,top,panel_path,False),(bridge.dock,dock,dock_path,True)):
            if window and spec:
                offset=spec[1]-window.y() if is_dock else 0
                mask_key=(window.width(),window.height(),round(spec[2]),round(spec[3]),spec[4],round(offset))
                handle=int(window.winId())
                if self.input_masks.get(handle)!=mask_key:
                    path=QTransform().translate((window.width()-spec[2])/2,offset).map(builder(spec[2],spec[3],spec[4]))
                    window.setMask(QRegion(path.toFillPolygon().toPolygon(),Qt.WindingFill));self.input_masks[handle]=mask_key
        displays=[(s.name(),s.geometry().x(),s.geometry().y(),s.geometry().width(),s.geometry().height()) for s in QApplication.screens()]
        signature=(tuple(displays),top,dock,bridge.frameEnabled,bridge.frameThickness,bridge.capture.busy,bridge.picking,tuple(bridge.fullscreen_names))
        if signature==self.signature:return
        self.signature=signature;paths={};data={}
        for name,x,y,w,h in displays:
            def local(spec):
                if spec and spec[0]<x+w and spec[0]+spec[2]>x and spec[1]<y+h and spec[1]+spec[3]>y:
                    return (spec[0]-x,spec[1]-y,*spec[2:])
                return None
            thickness=bridge.frameThickness if bridge.frameEnabled and name not in bridge.fullscreen_names else 0
            path=QPainterPath() if bridge.capture.busy or bridge.picking else shell_path(w,h,thickness,local(top),local(dock))
            # Qt's curve renderer needs a normalized silhouette for overlapping contours.
            outline=svg_path(path.simplified())
            paths[name]=path;data[name]={'path':outline,'outline':outline,'visible':not path.isEmpty(),'components':(thickness,local(top),local(dock))}
        self.paths=paths;self.data=data;self.revision+=1;bridge.surfaceChanged.emit()
    def stop(self):self.stopped=True;self.timer.stop()
