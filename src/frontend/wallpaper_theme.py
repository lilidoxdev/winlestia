# Python 3.12 / wallpaper_theme.py / Windows. Material You from the wallpaper really on screen. Event-driven: the backend bumps wallpaperRevision, we re-resolve once (debounced).
from pathlib import Path
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from PySide6.QtCore import QObject, QTimer, Signal

import chromatic_palette
from chromatic_palette import generate_palette
import wallpaper_sources

class WallpaperTheme(QObject):
    changed=Signal();result=Signal(dict)
    def __init__(self,parent=None):
        super().__init__(parent);self.executor=ThreadPoolExecutor(max_workers=1,thread_name_prefix='Panneaux Palette');self.cache=OrderedDict();self.monitors=[];self.signature=None;self.closed=False;self.generation=0;self.pending=False;self.again=False;self.client=None;self.captures=OrderedDict();self.revision=None;self.requested=set();self.result.connect(self.accept)
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
        # Several frames (video) -> palette_from_images; otherwise analyze_images, then the single-image generate_palette.
        images=[i for i in images if i]
        if not images:return generate_palette('',background)
        build=getattr(chromatic_palette,'palette_from_images',None)
        if build is not None:return build(list(images),background=background,key=(source.key,source.modified,background,tuple(images)),slot=str(slot),source=source.label)
        function=getattr(chromatic_palette,'analyze_images',None)
        if function is None or len(images)==1:return generate_palette(images[0],background)
        return function(list(images),key=source.key)
    def accept(self,result):
        self.pending=False
        again,self.again=self.again,False
        if result['generation']==self.generation and not self.closed and getattr(self,'themes',{})!=result['themes']:self.themes=result['themes'];self.changed.emit()
        if again and not self.closed:self.debounce.start()
    def for_screen(self,name):
        themes=getattr(self,'themes',{});return themes.get(name,next(iter(themes.values()),{}))
    def stop(self):self.closed=True;self.debounce.stop();self.executor.shutdown(wait=False,cancel_futures=True)
