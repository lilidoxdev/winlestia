# Python 3.12 / capture_native.py / Qt 6. In-memory capture and matching annotation export.
from PySide6.QtCore import QObject, Signal, Slot, Property, QRect, QPointF, Qt
from PySide6.QtGui import QImage, QPainter, QPen, QColor, QGuiApplication
from PySide6.QtQuick import QQuickImageProvider
from PySide6.QtWidgets import QFileDialog
import uuid,time

class CaptureProvider(QQuickImageProvider):
    def __init__(self):
        super().__init__(QQuickImageProvider.Image);self.image=QImage()
    def requestImage(self, name, size, requested):
        size.setWidth(self.image.width());size.setHeight(self.image.height())
        return self.image

def render_capture(image, region, strokes, logical_size):
    if image.isNull(): raise ValueError('Aucune capture disponible.')
    sx=image.width()/logical_size[0];sy=image.height()/logical_size[1]
    x,y,w,h=[float(region[k]) for k in ('x','y','width','height')]
    crop=QRect(round(x*sx),round(y*sy),round(w*sx),round(h*sy)).intersected(image.rect())
    if crop.width()<2 or crop.height()<2:raise ValueError('Sélectionne une zone plus grande.')
    output=image.copy(crop);output.setDevicePixelRatio(1)
    painter=QPainter(output);painter.setRenderHint(QPainter.Antialiasing);painter.scale(sx,sy);painter.translate(-crop.x()/sx,-crop.y()/sy)
    for stroke in strokes:
        color=QColor(stroke.get('color','#9d4d3a'));color.setAlphaF(.3 if stroke.get('tool')=='highlight' else 1)
        pen=QPen(color,float(stroke.get('width',4)));pen.setCapStyle(Qt.RoundCap);pen.setJoinStyle(Qt.RoundJoin);painter.setPen(pen)
        points=stroke.get('points',[])
        if len(points)==1:painter.drawPoint(QPointF(points[0]['x'],points[0]['y']))
        for a,b in zip(points,points[1:]):painter.drawLine(QPointF(a['x'],a['y']),QPointF(b['x'],b['y']))
    painter.end();return output

class CaptureController(QObject):
    changed=Signal()
    def __init__(self,bridge):
        super().__init__(bridge);self.bridge=bridge;self.provider=CaptureProvider();self.active=False;self.revision=0;self.ocr=False;self.logical=(1,1);self.error='';self.pending=False
    visible=Property(bool,lambda s:s.active,notify=changed)
    busy=Property(bool,lambda s:s.active or s.pending,notify=changed)
    source=Property(str,lambda s:f'image://capture/{s.revision}',notify=changed)
    textOnly=Property(bool,lambda s:s.ocr,notify=changed)
    errorText=Property(str,lambda s:s.error,notify=changed)
    @Slot(str)
    def start(self,mode):
        from PySide6.QtCore import QTimer
        if self.active or self.bridge.modal:return
        self.error='';self.pending=True;self.changed.emit();self.bridge.closePanel();self.bridge.force_dock=0;self.bridge.modal=True;self.bridge.shellEvent.emit('dockHide')
        deadline=time.monotonic()+3
        def grab():
            try:
                if (self.bridge.top and self.bridge.top.isVisible()) or (self.bridge.dock and self.bridge.dock.isVisible()):
                    if time.monotonic()<deadline:QTimer.singleShot(80,grab);return
                    raise ValueError('Le panneau ne s’est pas masqué. Relance la capture.')
                screen=self.bridge._screen;self.provider.image=screen.grabWindow(0).toImage()
                if self.provider.image.isNull():raise ValueError('Windows ne permet pas cette capture.')
                self.logical=(screen.geometry().width(),screen.geometry().height());self.ocr=mode=='text';self.active=True;self.revision+=1;self.changed.emit()
                if mode=='screen':self.export('copy',{'x':0,'y':0,'width':self.logical[0],'height':self.logical[1]},[])
            except Exception as error:self.bridge._notice=str(error);self.bridge.noticeChanged.emit();self.cancel();self.bridge.navigate('capture')
        QTimer.singleShot(450,grab)
    @Slot()
    def cancel(self):
        self.active=False;self.pending=False;self.bridge.modal=False;self.provider.image=QImage();self.changed.emit()
    @Slot(str,'QVariant','QVariant')
    def export(self,action,region,strokes):
        if hasattr(region,'toVariant'):region=region.toVariant()
        if hasattr(strokes,'toVariant'):strokes=strokes.toVariant()
        try:
            output=render_capture(self.provider.image,region,strokes,self.logical)
            if action=='save':
                self.active=False;self.changed.emit()
                path,_=QFileDialog.getSaveFileName(None,'Enregistrer la capture','Capture.png','Image PNG (*.png)')
                if not path:self.active=True;self.changed.emit();return
                if not path.lower().endswith('.png'):path+='.png'
                if not output.save(path,'PNG'):raise ValueError('Impossible d’enregistrer la capture.')
            elif action in ('pin','ocr'):
                folder=self.bridge.config_path/'captures';folder.mkdir(exist_ok=True)
                path=folder/(uuid.uuid4().hex+'.png')
                if not output.save(str(path),'PNG'):raise ValueError('Impossible de préparer la capture.')
                self.bridge.action('captureExport',{'path':str(path),'action':action})
            else:QGuiApplication.clipboard().setImage(output)
            self.cancel()
        except Exception as error:
            self.error=str(error);self.active=True;self.changed.emit()
