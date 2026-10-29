# Python 3.12 / Qt Quick 6.10, Windows 11. GPL-3.0-or-later; native host for the upstream Caelestia QML.
from __future__ import annotations
import argparse, ctypes, io, json, os, queue, shutil, sys, threading, time, urllib.request, urllib.parse
from pathlib import Path
from PySide6.QtCore import QObject, Property, Signal, Slot, QThread, QTimer, QUrl, QRect, Qt, qInstallMessageHandler
from PySide6.QtGui import QCursor, QFontDatabase, QKeySequence, QColor
from PySide6.QtWidgets import QApplication, QFileDialog, QInputDialog, QLineEdit
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtQuick import QQuickWindow
from interaction import EdgePolicy, is_fullscreen
from localization import Translator
from custom_palette import make_palette,DEFAULT,valid_color
from screen_picker import ClickPicker
from search import palette_search
from live_model import LiveListModel
from capture_native import CaptureController
from concurrent.futures import ThreadPoolExecutor
from wallpaper_theme import WallpaperTheme
from native_glass import NativeGlass
from surface_geometry import SurfaceScene

ROOT=Path(__file__).resolve().parent
BACKEND=ROOT.parent/'Glass-v5'
CONFIG=Path(os.environ['LOCALAPPDATA'])/'Panneaux'/'Caelestia'
CONFIG.mkdir(parents=True,exist_ok=True)
user32=ctypes.windll.user32
user32.GetForegroundWindow.restype=ctypes.c_void_p
user32.GetWindowLongPtrW.restype=ctypes.c_ssize_t
user32.GetWindowLongPtrW.argtypes=[ctypes.c_void_p,ctypes.c_int]
user32.SetWindowLongPtrW.argtypes=[ctypes.c_void_p,ctypes.c_int,ctypes.c_ssize_t]
user32.GetWindowThreadProcessId.argtypes=[ctypes.c_void_p,ctypes.POINTER(ctypes.c_ulong)]
import ctypes.wintypes
user32.WindowFromPoint.argtypes=[ctypes.wintypes.POINT];user32.WindowFromPoint.restype=ctypes.c_void_p
user32.GetWindowRect.argtypes=[ctypes.c_void_p,ctypes.c_void_p]
user32.GetClientRect.argtypes=[ctypes.c_void_p,ctypes.c_void_p]
user32.ClientToScreen.argtypes=[ctypes.c_void_p,ctypes.c_void_p]
user32.GetDC.argtypes=[ctypes.c_void_p];user32.GetDC.restype=ctypes.c_void_p
user32.ReleaseDC.argtypes=[ctypes.c_void_p,ctypes.c_void_p]
ctypes.windll.gdi32.GetPixel.argtypes=[ctypes.c_void_p,ctypes.c_int,ctypes.c_int];ctypes.windll.gdi32.GetPixel.restype=ctypes.c_ulong
user32.IsZoomed.argtypes=[ctypes.c_void_p]
user32.IsWindow.argtypes=[ctypes.c_void_p];user32.IsIconic.argtypes=[ctypes.c_void_p];user32.IsWindowVisible.argtypes=[ctypes.c_void_p]
user32.MonitorFromWindow.argtypes=[ctypes.c_void_p,ctypes.c_ulong];user32.MonitorFromWindow.restype=ctypes.c_void_p
user32.GetMonitorInfoW.argtypes=[ctypes.c_void_p,ctypes.c_void_p]
class MonitorInfo(ctypes.Structure):
    _fields_=[('size',ctypes.c_ulong),('monitor',ctypes.c_long*4),('work',ctypes.c_long*4),('flags',ctypes.c_ulong),('name',ctypes.c_wchar*32)]
kernel32=ctypes.windll.kernel32
kernel32.CreateEventW.argtypes=[ctypes.c_void_p,ctypes.c_bool,ctypes.c_bool,ctypes.c_wchar_p];kernel32.CreateEventW.restype=ctypes.c_void_p
kernel32.SetEvent.argtypes=[ctypes.c_void_p];kernel32.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_ulong];kernel32.CloseHandle.argtypes=[ctypes.c_void_p]
kernel32.OpenProcess.argtypes=[ctypes.c_ulong,ctypes.c_bool,ctypes.c_ulong];kernel32.OpenProcess.restype=ctypes.c_void_p
SHUTTING_DOWN=False
QML_ISSUES=[]

def message_handler(kind,context,message):
    if SHUTTING_DOWN:return
    if int(kind)>0:QML_ISSUES.append(message)
    with (CONFIG/'frontend.log').open('a',encoding='utf-8') as stream:
        stream.write(f'{time.strftime("%H:%M:%S")} {kind}: {message}\n')

class PipeClient(QThread):
    received=Signal(dict)
    def __init__(self,name):
        super().__init__();self.path='\\\\.\\pipe\\'+name;self.commands=queue.Queue();self.subscription={};self.stopping=False;self.handle=None;self.updates={};self.update_lock=threading.Lock();self.last_state=0
        self.wakeup=kernel32.CreateEventW(None,False,False,'Local\\PanneauxCaelestiaWake')
    def run(self):
        while not self.stopping:
            try:
                with open(self.path,'r+b',buffering=0) as connection:
                    self.handle=connection
                    reader=io.BufferedReader(connection,65536)
                    while not self.stopping:
                        try: request=self.commands.get_nowait()
                        except queue.Empty:
                            with self.update_lock:request=self.updates.pop(next(iter(self.updates)),None) if self.updates else None
                            if request is None:
                                visual=self.subscription.get('visible') and self.subscription.get('page')==1 and time.monotonic()-self.last_state<.6
                                request={'command':'visualizer' if visual else 'state','args':{**self.subscription,'pid':os.getpid()}}
                        if request['command']=='state':self.last_state=time.monotonic()
                        request={'command':request['command'],'args':{**self.subscription,**request['args'],'pid':os.getpid(),'epoch':self.subscription.get('epoch',0)}}
                        payload=(json.dumps(request,ensure_ascii=False)+'\n').encode('utf-8')
                        connection.write(payload)
                        data=reader.readline(8*1024*1024)
                        if not data: break
                        self.received.emit(json.loads(data))
                        if request['command'] in ('state','visualizer'):kernel32.WaitForSingleObject(self.wakeup,55 if self.subscription.get('visible') and self.subscription.get('page')==1 else 180 if self.subscription.get('visible') else 500)
            except (OSError,ValueError,json.JSONDecodeError):self.msleep(300)
            finally:self.handle=None
    def stop(self):
        self.stopping=True
        kernel32.SetEvent(self.wakeup)
        if self.handle:
            try:self.handle.close()
            except OSError:pass

class WindowsPlayer(QObject):
    changed=Signal()
    def __init__(self,bridge):super().__init__();self.bridge=bridge;self.data={}
    available=Property(bool,lambda s:bool(s.data.get('available')),notify=changed)
    trackTitle=Property(str,lambda s:s.data.get('title',''),notify=changed)
    trackArtist=Property(str,lambda s:s.data.get('artist',''),notify=changed)
    trackAlbum=Property(str,lambda s:s.data.get('album',''),notify=changed)
    trackArtUrl=Property(str,lambda s:s.data.get('art',''),notify=changed)
    identity=Property(str,lambda s:s.data.get('app','Lecteur Windows'),notify=changed)
    isPlaying=Property(bool,lambda s:bool(s.data.get('playing')),notify=changed)
    length=Property(float,lambda s:float(s.data.get('duration',0)),notify=changed)
    volume=Property(float,lambda s:float(s.data.get('volume',70)),notify=changed)
    canGoNext=Property(bool,lambda s:bool(s.data.get('canNext')),notify=changed)
    canGoPrevious=Property(bool,lambda s:bool(s.data.get('canPrevious')),notify=changed)
    canTogglePlaying=Property(bool,lambda s:bool(s.data.get('canToggle')),notify=changed)
    canSeek=Property(bool,lambda s:bool(s.data.get('canSeek')),notify=changed)
    positionSupported=Property(bool,lambda s:bool(s.data.get('canSeek')),notify=changed)
    canRaise=Property(bool,lambda s:bool(s.data.get('available')),notify=changed)
    canQuit=Property(bool,lambda s:False,constant=True)
    position=Property(float,lambda s:float(s.data.get('position',0)),lambda s,v:s.bridge.action('media',{'action':'seek','value':v}),notify=changed)
    @Slot()
    def positionChanged(self):pass
    @Slot()
    def togglePlaying(self):self.bridge.action('media',{'action':'toggle'})
    @Slot()
    def previous(self):self.bridge.action('media',{'action':'previous'})
    @Slot()
    def next(self):self.bridge.action('media',{'action':'next'})
    @Slot(name='raise')
    def raise_player(self):self.bridge.action('raiseMedia',{})
    @Slot()
    def quit(self):pass

class Bridge(QObject):
    colorPicked=Signal(str);surfaceChanged=Signal();changed=Signal();perfChanged=Signal();frameChanged=Signal();screensChanged=Signal();spectrumChanged=Signal();catalogChanged=Signal();dockChanged=Signal();clipboardChanged=Signal();noticeChanged=Signal();searchChanged=Signal();navigationRequested=Signal(str);weatherResult=Signal(dict);windowChanged=Signal();geometryChanged=Signal();shellEvent=Signal(str);tabRequested=Signal(int)
    def __init__(self,args):
        super().__init__();self.surface_scene=None;self.args=args;self._window=None;self._state={};self._pending_controls={};self._pending_audio={};self._icon_requests={};self._dark=False;self._player=WindowsPlayer(self);self.top=None;self.dock=None;self.social_win=None;self.away_social=0;self.bookmarks_win=None;self.away_bookmarks=0;self.last_reply=0;self.peek_until=0;self.away_top=0;self.away_dock=0;self.force_dock=0
        self.top_seen=False;self._screen=QApplication.primaryScreen();self.client=PipeClient(args.pipe);self.client.received.connect(self.update_state);self.client.start()
        self.pointer=QTimer(self);self.pointer.setInterval(90);self.pointer.timeout.connect(self.poll_pointer);self.pointer.start()
        self._weather={};self.weather_busy=False;self.weather_at=0;self.modal=False;self.weatherResult.connect(self.weather_ready)
        self.edge_policy=EdgePolicy();self._results=[];self.query='';self.opening_until=0;self.mode='dashboard';self.workspace_board='apps';self.health_at=0
        self._notice='';self.navigation_epoch=0;self.recording_kind=None;self.walltheme=WallpaperTheme(self);self.walltheme.changed.connect(self.theme_ready);self.fullscreen_names=();self._theme_revision=0;QApplication.instance().screenAdded.connect(self.screen_list_changed);QApplication.instance().screenRemoved.connect(self.screen_list_changed)
        for display in QApplication.screens():display.geometryChanged.connect(lambda rect:self.screensChanged.emit())
        try:self.city=json.loads((CONFIG/'weather.json').read_text(encoding='utf-8')).get('city','')
        except (OSError,ValueError):self.city=''
    languageMode=Property(str,lambda s:TRANSLATOR.mode,notify=changed)
    @Slot(str)
    def setLanguage(self,mode):TRANSLATOR.select(mode);self.changed.emit();self.engine.retranslate()
    def current_theme(self):return self.walltheme.for_screen(self._screen.name())
    def theme_ready(self):self._theme_revision+=1;self.publish_palette();self.changed.emit();self.frameChanged.emit()
    # Winlestia Fichiers (WPF) cannot see a live-wallpaper override, so the palette actually shown here is handed over in Glass-v5/palette-shell.json (auto mode only, written only when it changed; the file is event-watched there, never polled).
    def publish_palette(self):
        # Computed here, written off the UI thread (ordered single-worker executor). Leaving auto mode writes {"mode":"custom"} so Palette.cs drops the stale auto palette.
        try:
            if self.themeMode=='auto':
                palette=self.current_theme().get('palette',{})
                if not palette:return
                text=json.dumps({'mode':'auto','palette':palette},sort_keys=True,default=str)
            else:text='{"mode":"custom"}'
            if text==getattr(self,'_published_palette',None):return
            self._published_palette=text
            if not hasattr(self,'_publisher'):self._publisher=ThreadPoolExecutor(max_workers=1,thread_name_prefix='Panneaux Publish')
            def write(text=text):
                try:
                    target=BACKEND/'palette-shell.json';tmp=target.with_suffix('.tmp');tmp.write_text(text,encoding='utf-8');os.replace(tmp,target)
                except Exception:
                    if getattr(self,'_published_palette',None)==text:self._published_palette=None
            self._publisher.submit(write)
        except Exception:pass
    def theme_inputs_changed(self):
        # Event-driven replacement of the explorer 4 s poll: runs only when a theme-relevant setting, the folder list or the wallpaper palette actually changed.
        s=self.settings;key=(s.get('themeMode'),json.dumps(s.get('customPalette'),sort_keys=True,default=str),s.get('explorerTheme'),s.get('accentBorders'),str(s.get('explorerFolders')),self._theme_revision)
        if key==getattr(self,'_theme_inputs',None):return
        self._theme_inputs=key;self.publish_palette();self.sync_explorer_theme()
    def screen_list_changed(self,screen):self.screensChanged.emit()
    themeRevision=Property(int,lambda s:s._theme_revision,notify=frameChanged)
    fullscreenScreens=Property('QVariantList',lambda s:list(s.fullscreen_names),notify=frameChanged)
    @Slot(str,int,result='QVariantMap')
    def paletteForScreen(self,name,revision):return self.walltheme.for_screen(name).get('palette',{})
    themeMode=Property(str,lambda s:s._state.get('settings',{}).get('themeMode','dark' if s._dark else 'light'),notify=changed)
    adaptivePalette=Property('QVariantMap',lambda s:make_palette(s.settings.get('customPalette'))['palette'] if s.themeMode=='custom' else s.current_theme().get('palette',{}) if s.themeMode=='auto' else {},notify=changed)
    wallpaperInfo=Property('QVariantMap',lambda s:s.current_theme(),notify=changed)
    glassOpacity=Property(float,lambda s:max(.02,min(.95,float(s.settings.get('customGlassOpacity',.12) if s.themeMode=='custom' else s.settings.get('glassOpacity',.12)))),notify=changed)
    glassTintOpacity=Property(float,lambda s:s.glassOpacity*.75/(1-s.glassOpacity*.25),notify=changed)
    surfaceRevision=Property(int,lambda s:s.surface_scene.revision if s.surface_scene else 0,notify=surfaceChanged)
    @Slot(str,int,result='QVariantMap')
    def surfaceForScreen(self,name,revision):return self.surface_scene.data.get(name,{}) if self.surface_scene else {}
    fileDragChanged=Signal()
    @Property(bool,notify=fileDragChanged)
    def fileDragActive(self):return getattr(self,'_file_drag_active',False)
    @Property(bool,notify=fileDragChanged)
    def fileDragNearTop(self):return getattr(self,'_file_drag_near',False)
    def poll_file_drag(self):
        down=bool(user32.GetAsyncKeyState(1)&0x8000);point=QCursor.pos();now=time.monotonic()
        if down and not getattr(self,'_file_drag_down',False):
            self._file_drag_origin=point;self._file_drag_start=now;self._file_drag_foreign=False
            try:
                cursor=ctypes.wintypes.POINT()
                if user32.GetCursorPos(ctypes.byref(cursor)):
                    owner=ctypes.c_ulong(0);user32.GetWindowThreadProcessId(user32.WindowFromPoint(cursor),ctypes.byref(owner))
                    self._file_drag_foreign=owner.value!=os.getpid()
            except Exception:pass
        active=down and getattr(self,'_file_drag_foreign',False) and now-getattr(self,'_file_drag_start',now)>.25 and (point-getattr(self,'_file_drag_origin',point)).manhattanLength()>12
        self._file_drag_down=down
        screen=QApplication.screenAt(point);near=bool(active and screen and point.y()<=screen.geometry().top()+150)
        changed=active!=getattr(self,'_file_drag_active',False) or near!=getattr(self,'_file_drag_near',False)
        self._file_drag_active=active;self._file_drag_near=near
        if changed:self.fileDragChanged.emit()
    def sync_explorer_theme(self):
        if not self.settings.get('explorerTheme',False) and not self.settings.get('accentBorders',True):self._explorer_signature=None;return
        palette=self.adaptivePalette
        args={'accent':palette.get('m3primary','#afd4f0' if self.dark else '#895143'),'background':palette.get('m3surface','#101518' if self.dark else '#fff8f6'),'text':palette.get('m3onSurface','#e5ebef' if self.dark else '#261916')}
        signature=json.dumps(args,sort_keys=True)+str(self._state.get('settings',{}).get('explorerFolders',[]))
        if signature!=getattr(self,'_explorer_signature',None):self.action('explorerPalette',args);self._explorer_signature=signature
    @Slot()
    def updateSurfaces(self):
        if self.surface_scene:self.surface_scene.request()
    frameThickness=Property(int,lambda s:max(4,min(40,int(s.settings.get('frameThickness',16)))),notify=changed)
    frameEnabled=Property(bool,lambda s:bool(s._state.get('settings',{}).get('screenFrame',True)),notify=changed)
    @Property('QVariantList',notify=screensChanged)
    def frameScreens(self):
        result=[]
        for screen in QApplication.screens():
            rect=screen.geometry()
            result.append({'name':screen.name(),'x':rect.x(),'y':rect.y(),'width':rect.width(),'height':rect.height()})
        return result
    @Property(QObject,constant=True)
    def capture(self):
        if not hasattr(self,'_capture'):self._capture=CaptureController(self);self.config_path=CONFIG
        return self._capture
    @Property(QObject,constant=True)
    def audioApplications(self):
        if not hasattr(self,'_audio_apps'):self._audio_apps=LiveListModel(self)
        return self._audio_apps
    @Property(QObject,constant=True)
    def audioOutputs(self):
        if not hasattr(self,'_audio_outputs'):self._audio_outputs=LiveListModel(self)
        return self._audio_outputs
    @Property(QObject,constant=True)
    def audioInputs(self):
        if not hasattr(self,'_audio_inputs'):self._audio_inputs=LiveListModel(self)
        return self._audio_inputs
    player=Property(QObject,lambda s:s._player,constant=True)
    dark=Property(bool,lambda s:make_palette(s.settings.get("customPalette"))["dark"] if s.themeMode=="custom" else bool(s.current_theme().get("dark",True)) if s.themeMode=="auto" else s._dark,notify=changed)
    performance=Property('QVariantMap',lambda s:s._state.get('performance',{}),notify=perfChanged)
    sysInfo=Property('QVariantMap',lambda s:s._state.get('sysInfo',{}),notify=perfChanged)
    weather=Property('QVariantMap',lambda s:s._weather,notify=changed)
    audioReady=Property(bool,lambda s:bool(s._state.get('audioMixer',{}).get('ready',True)),notify=changed)
    controls=Property('QVariantMap',lambda s:s._state.get('controls',{}),notify=changed)
    apps=Property('QVariantList',lambda s:s._state.get('apps',[]),notify=catalogChanged)
    favorites=Property('QVariantList',lambda s:s._state.get('favorites',[]),notify=catalogChanged)
    recent=Property('QVariantList',lambda s:s._state.get('recent',[]),notify=catalogChanged)
    websites=Property('QVariantList',lambda s:s._state.get('websites',[]),notify=catalogChanged)
    profiles=Property('QVariantList',lambda s:s._state.get('profiles',[]),notify=catalogChanged)
    runningApps=Property('QVariantList',lambda s:s._state.get('runningApps',[]),notify=dockChanged)
    taskbarApps=Property('QVariantList',lambda s:s._state.get('taskbarApps',[]),notify=dockChanged)
    clipboardItems=Property('QVariantList',lambda s:s._state.get('clipboard',[]),notify=clipboardChanged)
    settings=Property('QVariantMap',lambda s:s._state.get('settings',{}),notify=changed)
    paletteResults=Property('QVariantList',lambda s:s._results,notify=searchChanged)
    workspaceBoard=Property(str,lambda s:s.workspace_board,notify=searchChanged)
    notice=Property(str,lambda s:s._notice,notify=noticeChanged)
    networkKind=Property(str,lambda s:s._state.get('networkKind') if s._state.get('networkKind') in ('ethernet','wifi','none') else 'wifi',notify=changed)
    taskbarEnabled=Property(bool,lambda s:bool(s._state.get('taskbarEnabled',s.args.preview)),notify=changed)
    spectrum=Property('QVariantList',lambda s:getattr(s,'_spectrum',[0.0]*48),notify=spectrumChanged)
    iconRevision=Property(int,lambda s:int(s._state.get('iconRevision',0)),notify=changed)
    assetRoot=Property(str,lambda s:(ROOT/'assets').as_posix(),constant=True)
    configRoot=Property(str,lambda s:CONFIG.as_posix(),constant=True)
    window=Property(QObject,lambda s:s._window,notify=windowChanged)
    screenX=Property(int,lambda s:s._screen.geometry().x(),notify=geometryChanged)
    screenY=Property(int,lambda s:s._screen.geometry().y(),notify=geometryChanged)
    screenWidth=Property(int,lambda s:s._screen.geometry().width(),notify=geometryChanged)
    screenHeight=Property(int,lambda s:s._screen.geometry().height(),notify=geometryChanged)
    glassEnabled=Property(bool,lambda s:s.themeMode=='auto' or (s.themeMode=='custom' and bool(s.settings.get('customGlass',False))),notify=changed)
    picking=Property(bool,lambda s:getattr(s,'_picking',False),notify=changed)
    pickColor=Property(str,lambda s:getattr(s,'_pick_color','#ffffff'),notify=changed)
    pickX=Property(int,lambda s:QCursor.pos().x()+20,notify=changed)
    pickY=Property(int,lambda s:QCursor.pos().y()+22,notify=changed)
    @Slot(str,result=bool)
    def validHex(self,value):return valid_color(value)
    @Slot(str,result='QVariantList')
    def colorHSV(self,value):
        h,s,v,a=QColor(value).getHsvF();return [max(0,h),s,v]
    @Slot(float,float,float,result=str)
    def hsvColor(self,h,s,v):return QColor.fromHsvF(h%1,max(0,min(1,s)),max(0,min(1,v))).name()
    @Slot(bool)
    def editTheme(self,enabled):
        self.modal=enabled
        if enabled:self.top.hide();self.shellEvent.emit('dockHide')
        elif self.top:self.top.show();self.navigate('settings')
    @Slot()
    def startColorPick(self):
        if getattr(self,'_picking',False):return
        self._click_picker=ClickPicker();self._picking=True;self._pick_after=time.monotonic()+.3;self.changed.emit()
        self._pick_timer=QTimer(self);self._pick_timer.setInterval(35);self._pick_timer.timeout.connect(self.sampleColor);self._pick_timer.start()
    def sampleColor(self):
        point=(ctypes.c_long*2)();user32.GetCursorPos(ctypes.byref(point))
        if self._click_picker.choice:point[:]=self._click_picker.choice
        dc=user32.GetDC(None)
        try:pixel=ctypes.windll.gdi32.GetPixel(dc,point[0],point[1])
        finally:user32.ReleaseDC(None,dc)
        if pixel!=0xffffffff:self._pick_color=f'#{pixel&255:02x}{pixel>>8&255:02x}{pixel>>16&255:02x}'
        self.changed.emit()
        if time.monotonic()<self._pick_after:return
        cancel=bool(user32.GetAsyncKeyState(27)&0x8000 or self._click_picker.cancel or self._click_picker.failed)
        if cancel or self._click_picker.choice:
            self._click_picker.stop();self._pick_timer.stop();self._pick_timer.deleteLater();self._picking=False;self.changed.emit();self.colorPicked.emit('' if cancel else self._pick_color)
    bookmarks=Property('QVariantMap',lambda s:{'browser':'','browsers':[],'items':[],'hiddenCount':0,'canDeleteInBrowser':False,'busy':False,'error':'',**(s._state.get('bookmarks') or {}),'items':getattr(s,'_bm_items',[])},notify=changed)
    social=Property('QVariantMap',lambda s:{'muted':False,'sound':True,'volume':70,'networks':[],'history':[],**(s._state.get('social') or {})},notify=changed)
    reducedEffects=Property(bool,lambda s:bool(s._state.get('settings',{}).get('reducedEffects',False)),notify=changed)
    preview=Property(bool,lambda s:s.args.preview,constant=True)
    @Slot(str,'QVariant')
    def action(self,name,args):
        if hasattr(args,'toVariant'):args=args.toVariant()
        args=args or {}
        if name in ('palette','settings','controls','clipboard','workspace','tools','audio','microphone','capture','mixer'):
            self.navigate('workspace:'+args.get('id','apps') if name=='workspace' else 'settings:'+str(args['section']) if name=='settings' and args.get('section') else name);return
        if name=='customTheme':
            self._state.setdefault('settings',{}).update(themeMode='custom',customPalette=args.get('colors',DEFAULT),customGlass=bool(args.get('glass')),customGlassOpacity=args.get('opacity',.12));self.changed.emit();self.frameChanged.emit()
        if name=='theme':
            self._state.setdefault('settings',{})['themeMode']=args.get('mode') if args.get('mode') in ('auto','custom') else 'dark' if args.get('dark') else 'light'
            if args.get('mode')!='auto':self._dark=bool(args.get('dark'))
            self.changed.emit();self.frameChanged.emit()
        if name=='preference' and args.get('key') in ('glassOpacity','frameThickness'):
            key=args['key'];value=max(4,min(40,round(float(args.get('value',16))))) if key=='frameThickness' else max(.02,min(.95,float(args.get('value',.12))))
            self._state.setdefault('settings',{})[key]=value;self.changed.emit();self.frameChanged.emit()
        if name in ('sessionVolume','deviceVolume'):
            self._pending_audio[(name,args.get('id',''))]=(float(args.get('value',0)),time.monotonic()+1.5)
        if name=='mediaVolume':
            self._pending_controls['mediaVolume']=(float(args.get('value',0)),time.monotonic()+1.5)
        if name in ('volume','brightness'):
            self._pending_controls[name]=(float(args.get('value',0)),time.monotonic()+1.5)
            self._state.setdefault('controls',{})[name]=float(args.get('value',0));self.changed.emit()
        if self.args.backend_pid:user32.AllowSetForegroundWindow(self.args.backend_pid)
        request={'command':name,'args':args}
        if name in ('icon','volume','brightness','preference','sessionVolume','deviceVolume','mediaVolume'):
            with self.client.update_lock:self.client.updates[name+':'+str(args.get('key',args.get('id','')))]=request
        else:self.client.commands.put(request)
        kernel32.SetEvent(self.client.wakeup)
    @Slot(str)
    def navigate(self,mode):
        if mode=='close' and self.recording_kind:self.recordShortcut(False,self.recording_kind)
        self.navigation_epoch+=1;self.client.subscription['epoch']=self.navigation_epoch
        self.top_seen=False;self.away_top=0;self.opening_until=time.monotonic()+.8
        if mode.startswith('workspace:'):self.workspace_board=mode.split(':',1)[1];self.searchChanged.emit()
        self.mode=mode.split(':',1)[0];self.navigationRequested.emit(mode)
    @Slot(str)
    def searchPalette(self,text):
        self.query=text;self._results=palette_search(text,self._state);self.searchChanged.emit()
    @Slot('QVariant')
    def executeResult(self,value):
        if hasattr(value,'toVariant'):value=value.toVariant()
        if not value:return
        kind=value.get('kind','app');key=value.get('id','')
        if kind=='navigate':self.navigate(key)
        elif kind=='calc':self.closePanel();self.action('copyText',{'text':value['name'][2:]})
        else:
            self.closePanel()
            self.action('paletteRun',{'kind':kind,'id':key,'query':self.query})
    @Slot()
    def focusPalette(self):
        def visit(item):
            if item.objectName()=='paletteQuery':item.setProperty('text','');item.forceActiveFocus();return True
            return any(visit(child) for child in item.childItems())
        if self.top:visit(self.top.contentItem())
    @Slot(str)
    def prefillPalette(self,text):
        def visit(item):
            if item.objectName()=='paletteQuery':item.setProperty('text',text);return True
            return any(visit(child) for child in item.childItems())
        if self.top:visit(self.top.contentItem())
        self.searchPalette(text)
    @Slot(int,int,result=str)
    def shortcutText(self,key,modifiers):return QKeySequence(key|modifiers).toString(QKeySequence.NativeText)
    @Slot(str,int,int)
    def captureShortcut(self,kind,key,modifiers):
        virtual=0x70+key-0x01000030 if 0x01000030<=key<=0x01000052 else key
        mods=(2 if modifiers&0x04000000 else 0)|(4 if modifiers&0x02000000 else 0)|(1 if modifiers&0x08000000 else 0)|(8 if modifiers&0x10000000 else 0)
        self.action('shortcut',{'kind':kind,'key':virtual,'modifiers':mods})
    @Slot(bool,str)
    def recordShortcut(self,enabled,kind):
        self.recording_kind=kind if enabled else None;self.modal=enabled
        if enabled:self._notice='';self.noticeChanged.emit()
        self.action('captureShortcutMode',{'enabled':enabled})
    @Slot(str)
    def chooseEntryIcon(self,key):
        self.modal=True
        try:
            path,_=QFileDialog.getOpenFileName(None,'Icône de cet accès','','Images (*.png *.ico *.jpg *.jpeg *.webp)')
            if path:self.action('entryIcon',{'id':key,'path':path})
        finally:self.modal=False
    @Slot(bool,int)
    def subscribe(self,visible,page):
        visible=visible or bool(self.recording_kind)
        updated={**self.client.subscription,'visible':visible,'page':page,'controls':bool(self.top and self.top.property('controlsOpen')),'mixer':bool(self.top and self.top.property('audioSampling'))}
        if updated!=self.client.subscription:self.client.subscription=updated;kernel32.SetEvent(self.client.wakeup)
    @Slot(int)
    def openTab(self,index):
        self.navigation_epoch+=1;self.client.subscription['epoch']=self.navigation_epoch;self.mode='dashboard';self.top_seen=False;self.opening_until=time.monotonic()+.8;self.tabRequested.emit(index)
    @Slot(str,result=str)
    def localUrl(self,path):
        if not path:return ''
        if path.endswith('/.face') and not Path(path).exists():return ''
        if path.startswith(('file:','image:','http:','https:','qrc:')):return path
        return QUrl.fromLocalFile(path).toString()
    @Slot(str,result=str)
    def iconPath(self,name):
        needle=name.lower().replace('.exe','')
        entry=next((e for e in self._state.get('apps',[]) if needle in e.get('name','').lower()),None)
        return self.iconUrl(entry['id'],self.iconRevision) if entry else ''
    @Slot(str,int,result=str)
    def iconUrl(self,key,revision=0):
        path=self._state.get('icons',{}).get(key,'')
        if not path and time.monotonic()-self._icon_requests.get(key,-10)>5:
            self._icon_requests[key]=time.monotonic();self.action('icon',{'id':key})
        return self.localUrl(path) if path else ''
    @Slot(result=str)
    def pickAvatar(self):
        path,_=QFileDialog.getOpenFileName(None,'Photo de profil','','Images (*.png *.jpg *.jpeg *.webp)')
        if path:shutil.copyfile(path,CONFIG/'.face');self.changed.emit()
        return self.localUrl(str(CONFIG/'.face')) if path else ''
    @Slot(str,str,result=bool)
    def copyFile(self,source,target):
        try:
            source=QUrl(source).toLocalFile() or source;target=QUrl(target).toLocalFile() or target
            if Path(source).resolve()!=Path(target).resolve():shutil.copyfile(source,target)
            return True
        except OSError:return False
    @Slot()
    def refreshWeather(self):
        if self.weather_busy or time.monotonic()-self.weather_at<900:return
        self.weather_busy=True
        def fetch():
            try:
                def read(url):
                    with urllib.request.urlopen(url,timeout=8) as response:return json.load(response)
                city=self.city
                if not city:
                    geo=read('https://wttr.in/?format=j1')['nearest_area'][0];city=geo['areaName'][0]['value']
                results=read('https://geocoding-api.open-meteo.com/v1/search?'+urllib.parse.urlencode({'name':city,'count':1,'language':'en'})).get('results',[])
                if not results:raise ValueError('Ville introuvable')
                location=results[0];data=read('https://api.open-meteo.com/v1/forecast?'+urllib.parse.urlencode({'latitude':location['latitude'],'longitude':location['longitude'],'current':'temperature_2m,weather_code','timezone':'auto'}))['current'];code=data['weather_code']
                icon='sunny' if code==0 else 'partly_cloudy_day' if code<3 else 'cloud' if code==3 else 'foggy' if code<50 else 'rainy' if code<70 else 'weather_snowy' if code<80 else 'thunderstorm' if code>=95 else 'rainy'
                self.weatherResult.emit({'temperature':f"{round(data['temperature_2m'])}°C",'description':location['name'],'icon':icon})
            except Exception:self.weatherResult.emit({'description':'Météo indisponible · réessayer'})
        threading.Thread(target=fetch,daemon=True).start()
    @Slot(dict)
    def weather_ready(self,value):self.weather_busy=False;self.weather_at=time.monotonic();self._weather=value;self.changed.emit()
    @Slot()
    def chooseCity(self):
        self.modal=True
        try:
            city,ok=QInputDialog.getText(None,'Météo · Open-Meteo','Ville, pays',QLineEdit.Normal,self.city)
            if ok and city.strip():self.city=city.strip()[:120];(CONFIG/'weather.json').write_text(json.dumps({'city':self.city}),encoding='utf-8');self.weather_at=0;self.refreshWeather()
        finally:self.modal=False
    @Slot()
    def activate(self):
        if self.top:
            self.top.requestActivate()
            QTimer.singleShot(80,lambda:self.top.requestActivate() if self.mode!='close' and self.top and self.top.isVisible() else None)
    @Slot()
    def revealDock(self):self.force_dock=time.monotonic()+3;self.shellEvent.emit('dock')
    @Slot()
    def closePanel(self):
        if self.recording_kind:self.recordShortcut(False,self.recording_kind)
        self.navigate('close')
    @Slot(dict)
    def update_state(self,message):
        if 'state' in message:
            self.walltheme.update(message['state'].get('wallpapers',[]),message['state'].get('wallpaperRevision'),self.client)
        if 'wallpaperCapture' in message:self.walltheme.captured(message['wallpaperCapture'])
        if message.get('notice'):self._notice=message['notice'];self.noticeChanged.emit()
        if 'spectrum' in message and message['spectrum']!=getattr(self,'_spectrum',None):self._spectrum=message['spectrum'];self.spectrumChanged.emit()
        if 'state' in message:
            self.last_reply=time.monotonic()
        if 'state' in message and any(value!=self._state.get(key) for key,value in message['state'].items() if key!='diagnostics'):
            catalog_changed=any(self._state.get(key)!=message['state'].get(key) for key in ('apps','favorites','recent','websites','profiles','quickTabs'))
            for key,(value,until) in list(self._pending_controls.items()):
                target=message['state'].setdefault('media' if key=='mediaVolume' else 'controls',{})
                field='volume' if key=='mediaVolume' else key
                remote=target.get(field,0)
                if abs(remote-value)<1 or time.monotonic()>until:self._pending_controls.pop(key,None)
                else:target[field]=value
            for (kind,ident),(value,until) in list(self._pending_audio.items()):
                rows=message['state'].get('audioMixer',{}).get('applications' if kind=='sessionVolume' else 'devices',[])
                row=next((r for r in rows if r.get('id')==ident),None)
                if time.monotonic()>until or (row and abs(row.get('volume',0)-value)<1):self._pending_audio.pop((kind,ident),None)
                elif row:row['volume']=value
            audio=message['state'].get('audioMixer',{});self.audioApplications.sync(audio.get('applications',[]));self.audioOutputs.sync([d for d in audio.get('devices',[]) if d['flow']==0]);self.audioInputs.sync([d for d in audio.get('devices',[]) if d['flow']==1])
            dock_changed=any(self._state.get(key)!=message['state'].get(key) for key in ('taskbarApps','runningApps'))
            clipboard_changed=self._state.get('clipboard')!=message['state'].get('clipboard')
            media_changed=self._state.get('media')!=message['state'].get('media')
            # Perf counters change on almost every poll; they have their own signal so the Bridge.changed storm (every binding re-evaluating) only fires for real changes.
            quiet=('diagnostics','performance','sysInfo')
            core_changed=any(value!=self._state.get(key) for key,value in message['state'].items() if key not in quiet)
            perf_changed=any(self._state.get(key)!=message['state'].get(key) for key in ('performance','sysInfo'))
            self._state=message['state'];self._dark=bool(self._state.get('dark'));self._player.data=self._state.get('media',{})
            if core_changed:self.changed.emit()
            if perf_changed:self.perfChanged.emit()
            if media_changed:self._player.changed.emit()
            if catalog_changed:self.catalogChanged.emit()
            if dock_changed:self.dockChanged.emit()
            if clipboard_changed:self.clipboardChanged.emit()
            if catalog_changed and self.mode=='palette':self.searchPalette(self.query)
        elif 'state' in message:self._state=message['state']
        if message.get('bookmarksItems'):
            self._bm_items=message['bookmarksItems'].get('items') or [];self._bm_items_version=message['bookmarksItems'].get('version');self.changed.emit()
        bm_version=(self._state.get('bookmarks') or {}).get('version')
        if bm_version is not None and bm_version!=getattr(self,'_bm_items_version',None) and bm_version!=getattr(self,'_bm_requested',None):
            self._bm_requested=bm_version;self.action('bookmarksItems',{})
        for event in message.get('events',[]):
            if event.startswith('shortcut-key:'):
                if self.recording_kind:
                    _,modifiers,key=event.split(':');self.action('shortcut',{'kind':self.recording_kind,'key':int(key),'modifiers':int(modifiers)})
                continue
            if event in ('capture-select','capture-ocr'):
                self.capture.start('text' if event=='capture-ocr' else 'region');continue
            if event=='dock':self.force_dock=time.monotonic()+3
            if event in ('toggle','access','media','performance','controls','palette','settings','clipboard','tools','audio','microphone','capture','mixer','close','settings:social','settings:bookmarks') or event.startswith('workspace:'):self.navigate(event)
            else:self.shellEvent.emit(event)
    @Slot(float,float,float,float,result=bool)
    def menuOutsideClick(self,x,y,w,h):
        down=bool(user32.GetAsyncKeyState(1)&0x8000 or user32.GetAsyncKeyState(2)&0x8000)
        was=getattr(self,'_menu_down',False);self._menu_down=down;p=QCursor.pos()
        return down and not was and not (x<=p.x()<x+w and y<=p.y()<y+h)
    screenFullscreen=Property(bool,lambda s:s._screen.name() in s.fullscreen_names,notify=frameChanged)
    def fullscreen(self,screen=None):
        screen=screen or self._screen
        hwnd=user32.GetForegroundWindow();pid=ctypes.c_ulong();user32.GetWindowThreadProcessId(hwnd,ctypes.byref(pid))
        remembered=getattr(self,'_full_candidates',{})
        if pid.value not in (os.getpid(),self.args.backend_pid) and hwnd:
            info=MonitorInfo();info.size=ctypes.sizeof(info)
            if user32.GetMonitorInfoW(user32.MonitorFromWindow(hwnd,2),ctypes.byref(info)):
                remembered[info.name]=hwnd;self._full_candidates=remembered
        hwnd=remembered.get(screen.name(),0)
        if not hwnd or not user32.IsWindow(hwnd) or user32.IsIconic(hwnd) or not user32.IsWindowVisible(hwnd):return False
        info=MonitorInfo();info.size=ctypes.sizeof(info)
        if not user32.GetMonitorInfoW(user32.MonitorFromWindow(hwnd,2),ctypes.byref(info)) or info.name!=screen.name():return False
        name=ctypes.create_unicode_buffer(128);user32.GetClassNameW(hwnd,name,128)
        if name.value in ('Progman','WorkerW','Shell_TrayWnd'):return False
        rect=(ctypes.c_long*4)();origin=(ctypes.c_long*2)(0,0)
        if not user32.GetClientRect(hwnd,ctypes.byref(rect)) or not user32.ClientToScreen(hwnd,ctypes.byref(origin)):return False
        return is_fullscreen(user32.GetWindowLongPtrW(hwnd,-16),bool(user32.IsZoomed(hwnd)),(origin[0],origin[1],origin[0]+rect[2],origin[1]+rect[3]),tuple(info.monitor))
    def edge_zone(self,rail,edge,side='right'):
        # Hover zone = displayed bump (content + drawer + chevron, aligned to its screen edge in the fixed-size window), +-16 px vertically, up to the screen edge.
        rg=rail.geometry();bw=float(rail.property('bumpWidth') or rg.width());bh=float(rail.property('bumpHeight') or rg.height());offset=float(rail.property('offset') or 0)
        by=rg.y()+(rg.height()-bh)/2
        if side=='left':
            bx=rg.x()-offset+bw;return QRect(int(edge),int(by)-16,int(bx-edge)+17,int(bh)+32)
        bx=rg.x()+rg.width()-bw+offset
        return QRect(int(bx)-16,int(by)-16,int(edge-bx)+17,int(bh)+32)
    def social_zone(self,screen_right):return self.edge_zone(self.social_win,screen_right,'right')
    def edge_rail(self,rail,side,point,geometry,full,now):
        # Shared trigger/hide logic of the hidden edge rails: peek at the screen edge, hide 0.5 s after the pointer leaves the displayed bump.
        key='social' if side=='right' else 'bookmarks';attr='away_'+key;event=(key+'Peek',key+'Hide')
        margin=int(self._state.get("settings",{}).get("rightActivationMargin" if side=='right' else "leftActivationMargin",2))
        rail_h=float(rail.property('bumpHeight') or rail.height() or 300)
        at_edge=point.x()>=geometry.right()-margin if side=='right' else point.x()<=geometry.left()+margin
        # A neighbouring screen on that side makes the shared edge a pass-through, not a trigger.
        edge_x=geometry.right() if side=='right' else geometry.left()
        adjacent=any(o.geometry()!=geometry and (o.geometry().left()<=edge_x+1 if side=='right' else o.geometry().right()>=edge_x-1) and (o.geometry().left()>=edge_x-1 if side=='right' else o.geometry().right()<=edge_x+1) and o.geometry().top()<geometry.bottom() and o.geometry().bottom()>geometry.top() for o in QApplication.screens())
        trigger=at_edge and not adjacent and abs(point.y()-geometry.center().y())<rail_h/2+60
        inside=rail.isVisible() and self.edge_zone(rail,geometry.right() if side=='right' else geometry.left(),side).contains(point)
        if trigger and not full:self.shellEvent.emit(event[0]);setattr(self,attr,0)
        elif inside or not rail.property('shown'):setattr(self,attr,0)
        else:
            if not getattr(self,attr):setattr(self,attr,now)
            if now-getattr(self,attr)>.5 and not self.args.preview:self.shellEvent.emit(event[1]);setattr(self,attr,0)
    def poll_pointer(self):
        self.poll_file_drag()
        if not self.top:return
        if self.modal:return
        if self.args.backend_pid and time.monotonic()>self.health_at:
            self.health_at=time.monotonic()+2
            handle=ctypes.windll.kernel32.OpenProcess(0x1000,False,self.args.backend_pid)
            if not handle:QApplication.quit();return
            ctypes.windll.kernel32.CloseHandle(handle)
        point=QCursor.pos();screen=QApplication.screenAt(point) or QApplication.primaryScreen()
        if screen!=self._screen:self._screen=screen;self.geometryChanged.emit();self.frameChanged.emit();self.changed.emit()
        now=time.monotonic();geometry=screen.geometry();full=self.fullscreen()
        fullscreen_names=tuple(s.name() for s in QApplication.screens() if self.fullscreen(s))
        if fullscreen_names!=self.fullscreen_names:self.fullscreen_names=fullscreen_names;self.frameChanged.emit()
        panel_width=float(self.top.property('panelWidth') or self.top.width());panel_height=float(self.top.property('panelHeight') or self.top.height())
        panel_rect=QRect(round(self.top.x()+(self.top.width()-panel_width)/2),self.top.y(),round(panel_width),round(panel_height))
        top_inside=self.top.isVisible() and panel_rect.adjusted(-12,-2,12,16).contains(point)
        top_edge=geometry.top()<=point.y()<=geometry.top()+int(self._state.get("settings",{}).get("topActivationMargin",2)) and abs(point.x()-geometry.center().x())<460
        if self.fileDragNearTop:
            if self.top.isVisible():self.navigate('close')
            self.away_top=0
        elif top_edge and not full:self.shellEvent.emit('peek');self.away_top=0
        elif top_inside:self.away_top=0;self.top_seen=True
        elif self.top.isVisible() and panel_height<95 and now>self.opening_until:
            if not self.away_top:self.away_top=now
            if now-self.away_top>.55 and not self.args.preview:self.navigate('close')
        if self.social_win:self.edge_rail(self.social_win,'right',point,geometry,full,now)
        if self.bookmarks_win:self.edge_rail(self.bookmarks_win,'left',point,geometry,full,now)
        if self.dock and self.taskbarEnabled:
            self.client.subscription['dock']=bool(self.dock.property('shown'))
            dock_inside=self.dock.isVisible() and self.dock.geometry().adjusted(-8,-10,8,4).contains(point)
            menu_open=bool(self.dock.property('menuOpen'))
            if menu_open:dock_inside=True
            bottom=point.y()>=geometry.bottom()-7
            decision=self.edge_policy.update(now,bottom,dock_inside,full,now<self.force_dock)
            if decision is True:self.shellEvent.emit('dock')
            elif decision is False and not self.args.preview:self.shellEvent.emit('dockHide')
    def stop(self):
        if getattr(self,'_click_picker',None):self._click_picker.stop()
        if getattr(self,'glass',None):self.glass.stop()
        if self.surface_scene:self.surface_scene.stop()
        self.walltheme.stop();self.pointer.stop();self.client.stop();self.client.wait(1500)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--pipe',default='PanneauxCaelestia');parser.add_argument('--backend-pid',type=int,default=0);parser.add_argument('--preview',action='store_true');parser.add_argument('--verify',action='store_true');parser.add_argument('--verify-tools82',action='store_true');parser.add_argument('--page',type=int,default=0);parser.add_argument('--capture');parser.add_argument('--exit-after',type=int,default=0);args=parser.parse_args()
    if args.verify:args.preview=True
    os.environ.setdefault('QSG_RHI_BACKEND','d3d11')
    global TRANSLATOR
    app=QApplication(sys.argv);TRANSLATOR=Translator(CONFIG);app.installTranslator(TRANSLATOR);app.setApplicationName('Panneaux · Caelestia Windows');app.setQuitOnLastWindowClosed(False);qInstallMessageHandler(message_handler)
    for font in (ROOT/'assets/fonts').glob('*.ttf'):QFontDatabase.addApplicationFont(str(font))
    bridge=Bridge(args);engine=QQmlApplicationEngine();bridge.engine=engine;engine.addImportPath(str(ROOT/'qml'));engine.addImageProvider('capture',bridge.capture.provider);engine.rootContext().setContextProperty('Bridge',bridge)
    bridge.changed.connect(bridge.theme_inputs_changed);QTimer.singleShot(0,bridge.theme_inputs_changed)
    engine.load(QUrl.fromLocalFile(str(ROOT/'qml/Main.qml')))
    if not engine.rootObjects():bridge.stop();return 1
    root=engine.rootObjects()[0];bridge.top=root;bridge._window=root;bridge.dock=root.findChild(QObject,'bottomDock');bridge.social_win=root.findChild(QObject,'socialRail');bridge.bookmarks_win=root.findChild(QObject,'bookmarksRail');bridge.windowChanged.emit();bridge.surface_scene=SurfaceScene(bridge);bridge.glass=NativeGlass(bridge)
    if os.environ.get('PANNEAUX_VERIFY_THICKNESS85')=='1':
        from validation85 import run
        run(bridge,root,app,QML_ISSUES,ROOT/'validation-8.5')
    if os.environ.get('PANNEAUX_VERIFY_MATERIAL84')=='1':
        from validation84 import run
        run(bridge,root,app,QML_ISSUES,ROOT/'validation-8.4')
    if os.environ.get('PANNEAUX_PROOF91')=='1':
        def proof91():
            native=bridge.glass;rectangles=[]
            for handle in native.windows:
                for index in range(256):
                    part=native.dll.BackdropPart(handle,index)
                    if not part:break
                    rect=(ctypes.c_long*4)();user32.GetWindowRect(part,ctypes.byref(rect));rectangles.append(list(rect))
            report={'mode':bridge.themeMode,'opacity':bridge.glassOpacity,'tintOpacity':bridge.glassTintOpacity,'frameThickness':bridge.frameThickness,'surfaceRevision':bridge.surface_scene.revision,'nativeErrors':native.errors,'qmlIssues':QML_ISSUES,'helpers':rectangles,'wallpaper':bridge.current_theme().get('source',''),'favorites':len(bridge.favorites),'dockEnabled':bridge.taskbarEnabled}
            report['allPassed']=not native.errors and not QML_ISSUES and bool(rectangles) and all(r[2]-r[0]<=592 and r[3]-r[1]<=592 for r in rectangles)
            (ROOT/'validation-9.1/live-startup-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
        QTimer.singleShot(6500,proof91)
    if args.preview:QTimer.singleShot(1200,lambda:bridge.openTab(args.page))
    if args.capture:
        def capture():root.grabWindow().save(args.capture)
        QTimer.singleShot(3500,capture)
    if args.exit_after:QTimer.singleShot(args.exit_after,app.quit)
    if args.verify_tools82:
        from validation82 import run
        run(bridge,root,app,QML_ISSUES,ROOT/'validation-8.3')
    if args.verify:
        report={'started':time.time(),'views':[],'frames':[]};proof=ROOT/'validation-8.1';proof.mkdir(exist_ok=True)
        sequence=[('dashboard',None),('media',None),('performance',None),('access',None),('workspace:apps',None),('workspace:games',None),('workspace:projects',None),('workspace:documents',None),('palette',''),('palette','chrome'),('palette','24*7'),('palette','> volume'),('controls',None),('settings',None),('clipboard',None)]
        def check(mode,query,index):
            bridge.navigate(mode)
            if query is not None:QTimer.singleShot(250,lambda:bridge.prefillPalette(query))
            def save():
                root.grabWindow().save(str(proof/f'{index:02}-{mode.replace(":","-")}.png'))
                report['views'].append({'mode':mode,'actualMode':root.property('pageMode'),'visible':root.isVisible(),'width':root.width(),'height':root.height(),'favorites':len(bridge._state.get('favorites',[])),'results':[{k:v for k,v in r.items() if k in ('name','kind')} for r in bridge._results],'performance':bridge._state.get('performance',{}),'diagnostics':bridge._state.get('diagnostics',{})})
            QTimer.singleShot(2200,save)
        for i,(mode,query) in enumerate(sequence):QTimer.singleShot(2000+i*3000,lambda m=mode,q=query,i=i:check(m,q,i))
        QTimer.singleShot(43300,lambda:bridge.shellEvent.emit('verifyRecorder'))
        def recorder_proof():
            root.grabWindow().save(str(proof/'shortcut-recorder.png'));report['shortcutRecorder']=bridge._state.get('diagnostics',{})
        QTimer.singleShot(43800,recorder_proof)
        QTimer.singleShot(43900,lambda:bridge.shellEvent.emit('verifyRecorderClose'))
        QTimer.singleShot(47500,bridge.closePanel)
        def idle_start():report['idleStart']={'qtCpu':time.process_time(),'time':time.perf_counter(),'backend':bridge._state.get('diagnostics',{})}
        QTimer.singleShot(49500,idle_start)
        root.frameSwapped.connect(lambda:report['frames'].append(time.perf_counter()))
        QTimer.singleShot(46000,lambda:bridge.dock.grabWindow().save(str(proof/'dock.png')))
        def finish():
            report['idleEnd']={'qtCpu':time.process_time(),'time':time.perf_counter(),'backend':bridge._state.get('diagnostics',{})};report['closed']={'visible':root.isVisible(),'performance':bridge._state.get('performance',{})};report['qmlIssues']=list(dict.fromkeys(QML_ISSUES));(proof/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');app.quit()
        QTimer.singleShot(55500,finish)
    def shutting_down():
        global SHUTTING_DOWN
        SHUTTING_DOWN=True;bridge.stop()
    app.aboutToQuit.connect(shutting_down)
    return app.exec()
if __name__=='__main__':raise SystemExit(main())
