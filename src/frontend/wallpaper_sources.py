# Python 3.12 / wallpaper_sources.py / Windows. Read-only discovery of active animated wallpapers.
from pathlib import Path
import ctypes,json,os

def desktop_hut_active():
    class ProcessEntry(ctypes.Structure):
        _fields_=[('size',ctypes.c_ulong),('usage',ctypes.c_ulong),('pid',ctypes.c_ulong),('heap',ctypes.c_size_t),('module',ctypes.c_ulong),('threads',ctypes.c_ulong),('parent',ctypes.c_ulong),('priority',ctypes.c_long),('flags',ctypes.c_ulong),('exe',ctypes.c_wchar*260)]
    kernel=ctypes.windll.kernel32
    kernel.CreateToolhelp32Snapshot.restype=ctypes.c_void_p
    kernel.Process32FirstW.argtypes=[ctypes.c_void_p,ctypes.POINTER(ProcessEntry)]
    kernel.Process32NextW.argtypes=kernel.Process32FirstW.argtypes
    kernel.CloseHandle.argtypes=[ctypes.c_void_p]
    handle=kernel.CreateToolhelp32Snapshot(2,0)
    if handle in (None,ctypes.c_void_p(-1).value):return []
    names=set();entry=ProcessEntry();entry.size=ctypes.sizeof(entry)
    try:
        ok=kernel.Process32FirstW(handle,ctypes.byref(entry))
        while ok:
            names.add(entry.exe.lower());ok=kernel.Process32NextW(handle,ctypes.byref(entry))
    finally:kernel.CloseHandle(handle)
    if 'desktophut studio.exe' not in names or 'mpv.exe' not in names:return []
    configs=[Path(os.environ['APPDATA'])/'motion-photo-editor/config.json']
    configs+=list((Path(os.environ['LOCALAPPDATA'])/'Packages').glob('Desktophut.DesktopHutLiveWallpapersforWindows_*/LocalCache/Roaming/motion-photo-editor/config.json'))
    results=[]
    for config in configs:
        try:
            data=json.loads(config.read_text(encoding='utf-8-sig'));library={w['id']:w for w in data.get('wallpaperLibrary',[])}
            for display,identity in data.get('activeWallpapers',{}).items():
                item=library.get(identity,{})
                thumbnail=config.parent/'thumbnails'/Path(item.get('thumbnail','')).name
                if not thumbnail.is_file():thumbnail=Path(item.get('thumbnail',''))
                if thumbnail.is_file():results.append({'display':display,'path':str(thumbnail),'modified':thumbnail.stat().st_mtime_ns,'label':item.get('title',thumbnail.name),'provider':'DesktopHut'})
        except (OSError,ValueError,KeyError):continue
    return results
