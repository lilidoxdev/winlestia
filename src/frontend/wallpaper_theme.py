# Python 3.12 / wallpaper_theme.py / Windows. Material You from local wallpaper thumbnails, never screen captures.
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from PySide6.QtCore import QObject, Signal
import colorsys

from chromatic_palette import generate_palette
from wallpaper_sources import desktop_hut_active
import time

class WallpaperTheme(QObject):
    changed=Signal();result=Signal(dict)
    def __init__(self,parent=None):
        super().__init__(parent);self.executor=ThreadPoolExecutor(max_workers=1,thread_name_prefix='Panneaux Palette');self.cache={};self.monitors=[];self.signature=None;self.closed=False;self.generation=0;self.pending=False;self.last_check=0;self.result.connect(self.accept)
    def update(self,monitors):
        if self.closed or self.pending or time.monotonic()-self.last_check<4:return
        self.last_check=time.monotonic();self.pending=True;self.monitors=monitors;self.generation+=1;generation=self.generation
        def work():
            results={}
            try:
                active=desktop_hut_active()
                for monitor in monitors:
                    override=next((w for w in active if w['display']==monitor.get('name')),None)
                    override=override or next((w for w in active if w['display'] in ('span','all')),None)
                    if override is None and len(monitors)==1 and len(active)==1:override=active[0]
                    data=override or monitor;path=data.get('path','');key=(path,data.get('modified',0),monitor.get('background','#182025'))
                    if key not in self.cache:
                        try:self.cache[key]=generate_palette(path,key[2])
                        except Exception:self.cache[key]=generate_palette('',key[2])
                    result=dict(self.cache[key]);result['provider']=data.get('provider','Windows')
                    if override:result['source']=override['label']
                    results[monitor.get('name','')]=result
                if len(self.cache)>24:self.cache={k:self.cache[k] for k in list(self.cache)[-12:]}
            finally:
                if not self.closed:self.result.emit({'generation':generation,'themes':results})
        self.executor.submit(work)
    def accept(self,result):
        self.pending=False
        if result['generation']!=self.generation or self.closed:return
        if getattr(self,'themes',{})!=result['themes']:self.themes=result['themes'];self.changed.emit()
    def for_screen(self,name):
        themes=getattr(self,'themes',{});return themes.get(name,next(iter(themes.values()),{}))
    def stop(self):self.closed=True;self.executor.shutdown(wait=False,cancel_futures=True)
