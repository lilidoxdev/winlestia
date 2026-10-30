# Python 3.12 / wallpaper_thumbs.py / cached 480 px JPEG thumbnails for the wallpaper switcher, plus the animated-wallpaper process check.
import hashlib,os,subprocess
from pathlib import Path
from PySide6.QtCore import QSize
from PySide6.QtGui import QImage,QImageReader
THUMB_WIDTH=480
ANIMATED_PROCESSES=('desktophut','wallpaperx','wallpaper x')
def thumb_dir():
    return Path(os.environ.get('LOCALAPPDATA',str(Path.home()/'AppData'/'Local')))/'Panneaux'/'Caelestia'/'thumbs'
def thumb_for(path):
    # Returns the thumbnail file for `path` (path+mtime key), generating it if missing; '' on failure. Only the scaled size is ever decoded.
    try:
        stat=os.stat(path);key=hashlib.sha1(f'{path.lower()}|{stat.st_mtime_ns}|{stat.st_size}'.encode('utf-8')).hexdigest()[:24]
        target=thumb_dir()/f'{key}.jpg'
        if target.exists():return str(target)
        reader=QImageReader(path);reader.setAutoTransform(True);size=reader.size()
        if not size.isValid() or size.width()<=0:return ''
        width=min(THUMB_WIDTH,size.width());reader.setScaledSize(QSize(width,max(1,round(size.height()*width/size.width()))))
        image=reader.read()
        if image.isNull():return ''
        target.parent.mkdir(parents=True,exist_ok=True);part=target.with_suffix('.tmp')
        if not image.save(str(part),'JPEG',82):return ''
        os.replace(part,target);return str(target)
    except OSError:return ''
def animated_wallpaper_running():
    # One tasklist call, only when the switcher opens.
    try:
        out=subprocess.run(['tasklist','/FO','CSV','/NH'],capture_output=True,text=True,timeout=6,creationflags=0x08000000).stdout.lower()
    except (OSError,subprocess.SubprocessError):return False
    return any(line.startswith('"'+name) for line in out.splitlines() for name in ANIMATED_PROCESSES)
