# Python 3.12 / sync_theme.py / Windows. Shared Winlestia wallpaper palette, no screen capture.
from pathlib import Path
import sys,json,time,os,ctypes,winreg,colorsys,argparse,hashlib,subprocess
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT))
from install import read_settings,note_written,STATE
SHELL=ROOT.parent/'Caelestia-Windows'
sys.path.insert(0,str(SHELL))
from custom_palette import make_palette
from wallpaper_sources import desktop_hut_active
def generate_palette(path='',background='#182025'):
    # numpy/PIL stay out of the resident daemon: the palette is computed in a short-lived child that frees them on exit.
    python=Path(sys.executable);python=python.with_name('python.exe') if python.with_name('python.exe').exists() else python
    done=subprocess.run([str(python),str(Path(__file__).resolve()),'--palette',path,background],stdin=subprocess.DEVNULL,capture_output=True,timeout=90,creationflags=0x08000000)
    if done.returncode:raise RuntimeError('palette child failed: '+done.stderr.decode('utf-8','replace').strip()[-300:])
    return json.loads(done.stdout.decode('utf-8'))
SETTINGS=Path.home()/'AppData/Local/Packages/Microsoft.WindowsTerminal_8wekyb3d8bbwe/LocalState/settings.json'
def rgb(value):return tuple(int(value[i:i+2],16) for i in (1,3,5))
def blend(a,b,f):return '#'+''.join(f'{round(x*(1-f)+y*f):02x}' for x,y in zip(rgb(a),rgb(b)))
def inputs():
    prefs_path=Path(os.environ.get('WINLESTIA_DATA_DIR') or Path(os.environ.get('LOCALAPPDATA') or str(Path.home()/'AppData'/'Local'))/'Panneaux')/'preferences-glass.json'
    if not prefs_path.exists():prefs_path=ROOT.parent/'Glass-v5/preferences-glass.json'
    prefs=json.loads(prefs_path.read_text(encoding='utf-8-sig')) if prefs_path.exists() else {}
    active=desktop_hut_active()
    if active:
        selected=next((w for w in active if w['display'] in ('all','span')),active[0]);path=selected['path'];label=selected['label']
    else:
        buffer=ctypes.create_unicode_buffer(32768);ctypes.windll.user32.SystemParametersInfoW(0x73,len(buffer),buffer,0);path=buffer.value;label=Path(path).name
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,r'Control Panel\Colors') as key:
        value=winreg.QueryValueEx(key,'Background')[0];background='#'+''.join(f'{int(v):02x}' for v in value.split())
    return prefs,path,label,background

def lum(c):
    f=lambda v:(v/255/12.92) if v/255<=.03928 else ((v/255+.055)/1.055)**2.4
    r_,g,b=(f(v) for v in rgb(c));return .2126*r_+.7152*g+.0722*b
def contrast(a,b):
    x,y=sorted((lum(a),lum(b)),reverse=True);return (x+.05)/(y+.05)
def lighten(color,against,ratio=4.5,minlum=0):
    for i in range(21):
        c=blend(color,'#ffffff',i/20)
        if contrast(c,against)>=ratio and lum(c)>=minlum:return c
    return '#ffffff'
CONSOLE_GREY='#161616'
def shell_palette():
    path=(Path(os.environ.get('WINLESTIA_DATA_DIR') or Path(os.environ.get('LOCALAPPDATA') or str(Path.home()/'AppData'/'Local'))/'Panneaux'))/'palette-shell.json'
    try:
        data=json.loads(path.read_text(encoding='utf-8-sig'))
        if data.get('mode')=='auto' and data.get('palette',{}).get('m3primary'):return {'palette':data['palette']}
    except (OSError,ValueError):pass
    return None
FIXED={'light':{'m3surface':'#fff8f6','m3primary':'#895143','m3onSurface':'#261916'},'dark':{'m3surface':'#101518','m3primary':'#afd4f0','m3onSurface':'#e5ebef'}}
def theme_mode(prefs):
    # Same precedence as the shell: AutoTheme wins (Theme keeps its previous value), then custom, then Porcelaine (shell) / Nuit.
    if prefs.get('AutoTheme',False):return 'auto'
    if prefs.get('Theme')=='custom':return 'custom'
    return 'light' if prefs.get('Theme') in ('porcelaine','shell') else 'dark'
def resolve_palette(prefs,path='',bg='#182025'):
    mode=theme_mode(prefs)
    if mode=='auto':return (shell_palette() or generate_palette(path,bg)),mode
    if mode=='custom':return make_palette(prefs.get('CustomPalette')),mode
    return {'palette':dict(FIXED[mode])},mode
def console_values(values,palette,mode='auto'):
    if mode!='auto':
        # Fixed and custom modes: the terminal uses the exact shell palette (surface, text, primary), translucent through acrylic.
        values.update(glass=True,opacity=.70,unfocusedOpacity=.55);return values
    # Console background is always translucent dark grey (about 8% toward the palette neutral); text and accent follow the shell palette, lightened to stay readable on it.
    p=palette['palette'];neutral=p.get('m3surface',CONSOLE_GREY);background=blend(CONSOLE_GREY,neutral,.08) if lum(neutral)<.2 else CONSOLE_GREY
    foreground=lighten(p.get('m3onSurface','#f7f3f4'),background,7,.6);accent=lighten(p['m3primary'],background,4.5,.45)
    values.update(accent=accent,foreground=foreground,background=background,secondary=lighten(blend(accent,foreground,.36),background,4.5,.45),muted=lighten(blend(background,foreground,.52),background,4.5),glass=True,opacity=.70,unfocusedOpacity=.55)
    return values
def theme_values(palette,source):
    p=palette['palette'];accent=p['m3primary'];foreground=p.get('m3onSurface','#f7f3f4');background=p['m3surface']
    return {'accent':accent,'secondary':blend(accent,foreground,.36),'muted':blend(background,foreground,.52),'foreground':foreground,'background':background,'source':source,'sourceColor':palette.get('sourceColor',accent)}

def write_changed(path,text):
    if path.exists() and path.read_text(encoding='utf-8')==text:return False
    temporary=path.with_name(path.name+'.tmp');temporary.write_text(text,encoding='utf-8');os.replace(temporary,path);return True

def apply(values):
    # Publish the shared colour files even if Windows Terminal is absent or malformed.
    write_changed(ROOT/'palette.json',json.dumps(values,indent=2,ensure_ascii=False)+'\n')
    write_changed(ROOT/'palette.colors','\n'.join(k+'='+';'.join(map(str,rgb(values[k]))) for k in ('accent','secondary','muted','foreground'))+'\n')
    settings_paths=list((Path(os.environ['LOCALAPPDATA'])/'Packages').glob('Microsoft.WindowsTerminal*/LocalState/settings.json'))
    for settings_path in settings_paths:
        try:update_terminal(settings_path,values)
        except (OSError,ValueError,KeyError,StopIteration):continue

def update_terminal(settings_path,values):
    c=read_settings(settings_path)
    template=json.loads((ROOT/'theme-template.json').read_text(encoding='utf-8-sig'))
    for key in ('schemes','themes'):
        items=c.setdefault(key,[])
        for item in template.get(key,[]):
            if not any(existing.get('name')==item['name'] for existing in items):items.append(item)
    c.setdefault('profiles',{}).setdefault('defaults',{}).update(colorScheme='Winlestia Glass',useAcrylic=values.get('glass',True),opacity=round(values.get('opacity',.65)*100),unfocusedAppearance={'opacity':round(values.get('unfocusedOpacity',.55)*100)})
    scheme=next(s for s in c['schemes'] if s['name']=='Winlestia Glass')
    accent=values['accent'];secondary=values['secondary'];background=values['background'];fg=values['foreground']
    scheme.update(background=background,foreground=fg,white=fg,cursorColor=accent,selectionBackground=blend(background,accent,.30),blue=accent,brightBlue=accent,purple=blend(accent,'#d8b4fe',.22),brightPurple=blend(secondary,'#ffffff',.15),cyan=secondary,brightCyan=blend(secondary,'#ffffff',.25),black=blend(background,fg,.04),brightBlack=values['muted'],brightWhite=blend(fg,'#ffffff',.5),red='#ff8fa0',green='#a7dcb2',yellow='#f2d397',brightRed='#ffadc0',brightGreen='#c1ecc8',brightYellow='#f7e1b8')
    theme=next(t for t in c['themes'] if t['name']=='Winlestia');theme['tabRow'].update(background=background+'00',unfocusedBackground=background+'00');theme['tab']['unfocusedBackground']=background
    if not (STATE/'state.json').exists():return  # restored meanwhile: never write Windows Terminal settings
    write_changed(settings_path,json.dumps(c,indent=4,ensure_ascii=False)+'\n')
    note_written(settings_path.parent.parent.name,useAcrylic=values.get('glass',True),opacity=round(values.get('opacity',.65)*100))

def wait_for_change():
    # Event-driven replacement of the 4 s poll: block on registry (wallpaper keys) and folder (Winlestia prefs, wallpaper app configs) change notifications.
    # 60 s timeout only so the theme-sync.stop file is still honoured; a change wakes us at once (0.5 s debounce).
    kernel=ctypes.windll.kernel32;advapi=ctypes.windll.advapi32;kernel.CreateEventW.restype=ctypes.c_void_p;kernel.FindFirstChangeNotificationW.restype=ctypes.c_void_p
    handles=[];keys=[]
    for sub in (r'Control Panel\Desktop',r'Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers'):
        key=ctypes.c_void_p()
        if advapi.RegOpenKeyExW(ctypes.c_void_p(0xFFFFFFFF80000001),sub,0,0x10,ctypes.byref(key))==0:
            event=kernel.CreateEventW(None,True,False,None);advapi.RegNotifyChangeKeyValue(key,True,5,ctypes.c_void_p(event),True);keys.append(key);handles.append(event)
    folders=[ROOT.parent/'Glass-v5',Path(os.environ.get('WINLESTIA_DATA_DIR') or Path(os.environ.get('LOCALAPPDATA') or '')/'Panneaux'),Path(os.environ.get('APPDATA',''))/'motion-photo-editor']
    folders+=list((Path(os.environ.get('LOCALAPPDATA',''))/'Packages').glob('Desktophut.*/LocalCache/Roaming/motion-photo-editor'))
    for folder in folders:
        if folder.is_dir():
            h=kernel.FindFirstChangeNotificationW(str(folder),False,0x1|0x10)
            if h and h!=ctypes.c_void_p(-1).value:handles.append(h)
    try:
        if handles:
            array=(ctypes.c_void_p*len(handles))(*handles)
            if kernel.WaitForMultipleObjects(len(handles),array,False,60000)!=0x102:time.sleep(.5)
        else:time.sleep(4)
    finally:
        for key in keys:advapi.RegCloseKey(key)
        for h in handles:
            kernel.CloseHandle(ctypes.c_void_p(h))

def run(once=False):
    kernel=ctypes.windll.kernel32;kernel.CreateMutexW.argtypes=[ctypes.c_void_p,ctypes.c_bool,ctypes.c_wchar_p];kernel.CreateMutexW.restype=ctypes.c_void_p
    handle=kernel.CreateMutexW(None,False,'Local\\WinlestiaTerminalPalette-'+hashlib.sha256(str(ROOT).lower().encode()).hexdigest()[:12])
    if not once and kernel.GetLastError()==183:return
    (ROOT/'validation').mkdir(exist_ok=True)
    signature=None
    while True:
        if not (STATE/'state.json').exists():return  # Winlestia terminal integration absent or restored: never touch Windows Terminal.
        try:
            prefs,path,label,bg=inputs();current=(prefs.get('CustomGlass'),prefs.get('CustomGlassOpacity'),json.dumps(prefs.get('CustomPalette',{}),sort_keys=True),prefs.get('AutoTheme',False),prefs.get('Theme','nuit'),path,shell_palette(),Path(path).stat().st_mtime_ns if Path(path).is_file() else 0,bg,tuple(sorted(str(p) for p in (Path(os.environ['LOCALAPPDATA'])/'Packages').glob('Microsoft.WindowsTerminal*/LocalState/settings.json'))))
            if current!=signature:
                palette,mode=resolve_palette(prefs,path,bg)
                values=theme_values(palette,label if mode=='auto' else prefs.get('Theme','nuit'))
                values=console_values(values,palette,mode)
                apply(values);signature=current
                (ROOT/'validation/sync-status.json').write_text(json.dumps({'ok':True,'source':label,'auto':prefs.get('AutoTheme',False),'updated':time.time()}),encoding='utf-8')
        except Exception as error:
            (ROOT/'validation/sync-status.json').write_text(json.dumps({'ok':False,'error':str(error),'updated':time.time()}),encoding='utf-8')
            if once:raise
        if once or (ROOT/'theme-sync.stop').exists():break
        wait_for_change()
if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--once',action='store_true');parser.add_argument('--palette',nargs=2,metavar=('PATH','BACKGROUND'))
    args=parser.parse_args()
    if args.palette:
        from chromatic_palette import generate_palette as _generate
        sys.stdout.write(json.dumps(_generate(*args.palette)))
    else:run(args.once)
