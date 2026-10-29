# Python 3.12 / wallpaper_theme.py / Windows. Material You from the wallpaper really on screen. Event-driven: the backend bumps wallpaperRevision, we re-resolve once (debounced).
import json,subprocess,sys
from pathlib import Path
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from PySide6.QtCore import QObject, QTimer, Signal

import wallpaper_sources

def _in_process(request):
    import chromatic_palette  # fallback: compute in-process as before
    if request.get('op')=='images':return chromatic_palette.palette_from_images(list(request['images']),background=request['background'],previous_dark=request.get('previous_dark'),slot=request.get('slot'),source=request.get('source',''))
    return chromatic_palette.generate_palette(request.get('path',''),request['background'])
def _palette(request):
    # numpy/PIL/material_color_utilities stay out of the resident frontend: the palette is computed in a short-lived child (palette_child.py) that frees them on exit.
    if getattr(sys,'frozen',False):return _in_process(request)
    python=Path(sys.executable);python=python.with_name('python.exe') if python.with_name('python.exe').exists() else python
    try:done=subprocess.run([str(python),str(Path(__file__).resolve().with_name('palette_child.py')),json.dumps(request)],stdin=subprocess.DEVNULL,capture_output=True,timeout=90,creationflags=0x08000000)
    except (OSError,subprocess.TimeoutExpired):return _in_process(request)
    if done.returncode:
        if b'ModuleNotFoundError' in done.stderr or b'ImportError' in done.stderr:return _in_process(request)
        raise RuntimeError('palette child failed: '+done.stderr.decode('utf-8','replace').strip()[-300:])
    return json.loads(done.stdout.decode('utf-8'))
def generate_palette(path='',background='#182025'):return _palette({'op':'generate','path':path,'background':background})

class WallpaperTheme(QObject):
    changed=Signal();result=Signal(dict)
    def __init__(self,parent=None):
        super().__init__(parent);self.executor=ThreadPoolExecutor(max_workers=1,thread_name_prefix='Panneaux Palette');self.cache=OrderedDict();self.monitors=[];self.signature=None;self.closed=False;self.generation=0;self.pending=False;self.again=False;self.client=None;self.captures=OrderedDict();self.revision=None;self.requested=set();self.dark_state=OrderedDict();self.result.connect(self.accept)
        self.debounce=QTimer(self);self.debounce.setSingleShot(True);self.debounce.setInterval(300);self.debounce.timeout.connect(self.resolve)
    def update(self,monitors,revision=None,client=None):
        # Called with every state message; costs one tuple comparison. A real change (revision or monitor paths) arms a single 300 ms debounce.
        if self.closed:return
        if client is not None:self.client=client
        signature=(revision,tuple((m.get('name'),m.get('path'),m.get('modified'),m.get('background')) for m in monitors))
        if signature==self.signature:return
        self.signature=signature;self.revision=revision;self.monitors=monitors;self.debounce.start()
    def captured(self,reply):
        # Reply of the backend one-shot capture of the live host; remember it for the current revision and re-resolve.
        if self.closed or not isinstance(reply,dict):return
        self.captures[reply.get('token')]=reply.get('path','') if reply.get('ok') else '';self.captures.move_to_end(reply.get('token'))
        while len(self.captures)>8:self.captures.popitem(last=False)
        self.debounce.start()
    def resolve(self):
        if self.closed:return
        if self.pending:self.again=True;return
        self.pending=True;self.generation+=1;generation=self.generation;monitors=list(self.monitors);revision=self.revision
        def work():
            results={}
            try:
                sources=wallpaper_sources.resolve(monitors)
                for monitor in monitors or [{'name':''}]:
                    name=monitor.get('name','');source=sources[name];background=monitor.get('background','#182025')
                    if source.needs_capture:
                        token=f'{source.key}:{revision}'
                        if token not in self.captures and token not in self.requested and self.client is not None:
                            self.requested.add(token);self.client.commands.put({'command':'wallpaperCapture','args':dict({'token':token},**({'x':source.rect[0],'y':source.rect[1],'w':source.rect[2],'h':source.rect[3]} if source.rect else {}))})
                        shot=self.captures.get(token)
                        if shot:source.images=[shot];source.key=token;source.modified=wallpaper_sources._mtime(shot)
                        else:source=wallpaper_sources.windows_source(wallpaper_sources.default_bases(),monitor,0)  # until the capture lands (or when it is black)
                    key=(source.key,source.modified,background)
                    if key not in self.cache:
                        images=wallpaper_sources.representative_images(source)
                        try:self.cache[key]=self.analyze(images,background,source,name)
                        except Exception:self.cache[key]=generate_palette('',background)
                    self.cache.move_to_end(key);entry=dict(self.cache[key]);entry['provider']=source.provider
                    if source.provider not in ('Windows','Slideshow','Spotlight'):entry['source']=source.label
                    results[name]=entry
                while len(self.cache)>24:self.cache.popitem(last=False)
                if len(self.requested)>32:self.requested.clear()
            finally:
                if not self.closed:self.result.emit({'generation':generation,'themes':results})
        self.executor.submit(work)
    def analyze(self,images,background,source,slot=''):
        # Single or several frames -> palette_from_images in the child; the light/dark hysteresis memory (per monitor slot, <=16) stays here and is passed along.
        images=[i for i in images if i]
        if not images:return generate_palette('',background)
        slot=str(slot);result=_palette({'op':'images','images':[str(i) for i in images],'background':background,'slot':slot,'previous_dark':self.dark_state.get(slot),'source':source.label})
        self.dark_state.pop(slot,None);self.dark_state[slot]=result['dark']
        while len(self.dark_state)>16:self.dark_state.popitem(last=False)
        return result
    def accept(self,result):
        self.pending=False
        again,self.again=self.again,False
        if result['generation']==self.generation and not self.closed and getattr(self,'themes',{})!=result['themes']:self.themes=result['themes'];self.changed.emit()
        if again and not self.closed:self.debounce.start()
    def for_screen(self,name):
        themes=getattr(self,'themes',{});return themes.get(name,next(iter(themes.values()),{}))
    def stop(self):self.closed=True;self.debounce.stop();self.executor.shutdown(wait=False,cancel_futures=True)
