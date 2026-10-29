# Python 3.12 / native_glass.py / Windows 11. One shared shell geometry and one material.
from pathlib import Path
import ctypes
from PySide6.QtCore import QObject,QTimer
from PySide6.QtWidgets import QApplication
from surface_geometry import path_commands

class SurfaceCommand(ctypes.Structure):
    _fields_=[('kind',ctypes.c_uint32),('points',ctypes.c_float*6)]

class NativeGlass(QObject):
    def __init__(self,bridge,library=None):
        super().__init__(bridge);self.bridge=bridge;self.windows={};self.errors=[];self.dll=None;self.stopped=False;self.queued=False
        try:
            self.dll=ctypes.WinDLL(str(library or Path(__file__).with_name('QtGlassBackdrop.dll')))
            signatures={'BackdropAttach':[ctypes.c_void_p],'BackdropUpdate':[ctypes.c_void_p,ctypes.c_double,ctypes.c_int,ctypes.c_int],'BackdropSurface':[ctypes.c_void_p,ctypes.POINTER(SurfaceCommand),ctypes.c_uint32],'BackdropMaterial':[ctypes.c_void_p,ctypes.c_float],'BackdropDetach':[ctypes.c_void_p],'BackdropPart':[ctypes.c_void_p,ctypes.c_int]}
            for name,args in signatures.items():getattr(self.dll,name).argtypes=args
            self.dll.BackdropPart.restype=ctypes.c_void_p
            if hasattr(self.dll,'BackdropSurfaceDirty'):self.dll.BackdropSurfaceDirty.argtypes=[ctypes.c_void_p,ctypes.POINTER(SurfaceCommand),ctypes.c_uint32]+[ctypes.c_float]*4
        except (OSError,AttributeError) as error:self.errors.append(str(error));self.dll=None
        self.user=ctypes.windll.user32;self.user.SetWindowPos.argtypes=[ctypes.c_void_p,ctypes.c_void_p,ctypes.c_int,ctypes.c_int,ctypes.c_int,ctypes.c_int,ctypes.c_uint]
        bridge.changed.connect(self.request_sync);bridge.surfaceChanged.connect(self.request_sync)
        self.timer=QTimer(self);self.timer.setInterval(250);self.timer.timeout.connect(self.sync);self.timer.start();self.request_sync()
    def request_sync(self,*args):
        if not self.stopped and not self.queued:self.queued=True;QTimer.singleShot(0,self.sync)
    def sync(self):
        self.queued=False
        if self.stopped:return
        scene=self.bridge.surface_scene
        if not scene or not self.dll:return
        live=set();native_bump={3:-1,4:-1}
        for window in QApplication.allWindows():
            if not window.title().startswith('Panneaux · Cadre '):continue
            handle=int(window.winId());live.add(handle);record=self.windows.get(handle);name=window.property('screenName')
            if not self.bridge.glassEnabled or self.bridge.reducedEffects:
                if record:self.dll.BackdropDetach(handle);self.windows.pop(handle,None)
                continue
            path=scene.paths.get(name)
            if not window.isVisible() or path is None or path.isEmpty():
                if record:self.dll.BackdropUpdate(handle,0,False,True)
                continue
            if record is None:
                status=self.dll.BackdropAttach(handle)
                if status<0:self.error('Attach',status);continue
                record={'key':None,'opacity':None,'components':None};self.windows[handle]=record
            if record['opacity']!=self.bridge.glassOpacity:
                status=self.dll.BackdropMaterial(handle,self.bridge.glassOpacity)
                if status<0:self.error('Material',status);continue
                record['opacity']=self.bridge.glassOpacity
            components=scene.data[name].get('components')
            key=(window.width(),window.height(),window.devicePixelRatio(),components)
            if record['key']!=key:
                commands=path_commands(path,window.devicePixelRatio());array=(SurfaceCommand*len(commands))()
                for index,(kind,*points) in enumerate(commands):array[index].kind=kind;array[index].points[:]=points
                old=record['components'];dirty=None
                if old and components and old[0]==components[0] and record['key'][:3]==key[:3]:
                    boxes=[spec for previous,current in zip(old[1:],components[1:]) if previous!=current for spec in (previous,current) if spec]
                    if boxes:
                        scale=window.devicePixelRatio();dirty=((min(b[0] for b in boxes)-2)*scale,(min(b[1] for b in boxes)-2)*scale,(max(b[0]+b[2] for b in boxes)+2)*scale,(max(b[1]+b[3] for b in boxes)+2)*scale)
                if dirty and hasattr(self.dll,'BackdropSurfaceDirty'):status=self.dll.BackdropSurfaceDirty(handle,array,len(commands),*dirty)
                else:status=self.dll.BackdropSurface(handle,array,len(commands))
                if status<0:
                    self.error('Geometry',status);self.dll.BackdropUpdate(handle,0,False,True);continue
                record['key']=key;record['components']=components
            status=self.dll.BackdropUpdate(handle,0,True,True)
            if status<0:self.error('Update',status)
            elif components:
                for slot in (3,4):
                    if len(components)>slot and components[slot]:native_bump[slot]=components[slot][2]
        # The rail content follows the bump width last sent to the native backdrop (-1 = no native backdrop: QML follows its own animation).
        for slot,attr in ((3,'social_win'),(4,'bookmarks_win')):
            rail=getattr(self.bridge,attr,None)
            if rail and rail.property('nativeBump')!=native_bump[slot]:rail.setProperty('nativeBump',native_bump[slot])
        for handle in set(self.windows)-live:self.dll.BackdropDetach(handle);self.windows.pop(handle,None)
        # Content-only windows stay above the shared background and never tint their text twice.
        for window in (self.bridge.top,self.bridge.dock,getattr(self.bridge,'social_win',None),getattr(self.bridge,'bookmarks_win',None)):
            if window and window.isVisible():self.user.SetWindowPos(int(window.winId()),ctypes.c_void_p(-1),0,0,0,0,0x13)
    def error(self,phase,status):
        message=f'{phase}: {status:#x}'
        if message not in self.errors:self.errors.append(message)
    def stop(self):
        self.stopped=True;self.timer.stop()
        if self.dll:
            for handle in self.windows:self.dll.BackdropDetach(handle)
        self.windows.clear()
