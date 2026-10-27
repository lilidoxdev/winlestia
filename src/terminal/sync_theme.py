# Python 3.12 / sync_theme.py / Windows. Shared Winlestia wallpaper palette, no screen capture.
from pathlib import Path
import sys,json,time,os,ctypes,winreg,colorsys,argparse,hashlib
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT))
from install import read_settings
SHELL=ROOT.parent/'Caelestia-Windows'
sys.path.insert(0,str(SHELL))
from chromatic_palette import generate_palette
from custom_palette import make_palette
from wallpaper_sources import desktop_hut_active
SETTINGS=Path.home()/'AppData/Local/Packages/Microsoft.WindowsTerminal_8wekyb3d8bbwe/LocalState/settings.json'
def rgb(value):return tuple(int(value[i:i+2],16) for i in (1,3,5))
def blend(a,b,f):return '#'+''.join(f'{round(x*(1-f)+y*f):02x}' for x,y in zip(rgb(a),rgb(b)))
def inputs():
    prefs_path=ROOT.parent/'Glass-v5/preferences-glass.json'
    prefs=json.loads(prefs_path.read_text(encoding='utf-8-sig')) if prefs_path.exists() else {}
    active=desktop_hut_active()
    if active:
        selected=next((w for w in active if w['display'] in ('all','span')),active[0]);path=selected['path'];label=selected['label']
    else:
        buffer=ctypes.create_unicode_buffer(32768);ctypes.windll.user32.SystemParametersInfoW(0x73,len(buffer),buffer,0);path=buffer.value;label=Path(path).name
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,r'Control Panel\Colors') as key:
        value=winreg.QueryValueEx(key,'Background')[0];background='#'+''.join(f'{int(v):02x}' for v in value.split())
    return prefs,path,label,background

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
    c.setdefault('profiles',{}).setdefault('defaults',{}).update(colorScheme='Winlestia Glass',useAcrylic=values.get('glass',True),opacity=round(values.get('opacity',.65)*100))
    scheme=next(s for s in c['schemes'] if s['name']=='Winlestia Glass')
    accent=values['accent'];secondary=values['secondary'];background=values['background'];fg=values['foreground']
    scheme.update(background=background,foreground=fg,white=fg,cursorColor=accent,selectionBackground=blend(background,accent,.30),blue=accent,brightBlue=secondary,purple=blend(accent,'#d8b4fe',.22),brightPurple=blend(secondary,'#ffffff',.15),cyan=secondary,brightCyan=blend(secondary,'#ffffff',.25),black=blend(background,fg,.04),brightBlack=values['muted'])
    theme=next(t for t in c['themes'] if t['name']=='Winlestia');theme['tabRow'].update(background=background+'00',unfocusedBackground=background+'00');theme['tab']['unfocusedBackground']=background
    write_changed(settings_path,json.dumps(c,indent=4,ensure_ascii=False)+'\n')

def run(once=False):
    kernel=ctypes.windll.kernel32;kernel.CreateMutexW.argtypes=[ctypes.c_void_p,ctypes.c_bool,ctypes.c_wchar_p];kernel.CreateMutexW.restype=ctypes.c_void_p
    handle=kernel.CreateMutexW(None,False,'Local\\WinlestiaTerminalPalette-'+hashlib.sha256(str(ROOT).lower().encode()).hexdigest()[:12])
    if not once and kernel.GetLastError()==183:return
    (ROOT/'validation').mkdir(exist_ok=True)
    signature=None
    while True:
        try:
            prefs,path,label,bg=inputs();current=(prefs.get('CustomGlass'),prefs.get('CustomGlassOpacity'),json.dumps(prefs.get('CustomPalette',{}),sort_keys=True),prefs.get('AutoTheme',False),prefs.get('Theme','nuit'),path,Path(path).stat().st_mtime_ns if Path(path).is_file() else 0,bg,tuple(sorted(str(p) for p in (Path(os.environ['LOCALAPPDATA'])/'Packages').glob('Microsoft.WindowsTerminal*/LocalState/settings.json'))))
            if current!=signature:
                if prefs.get('Theme')=='custom':palette=make_palette(prefs.get('CustomPalette'))
                elif prefs.get('AutoTheme',False):palette=generate_palette(path,bg)
                else:
                    light=prefs.get('Theme') in ('porcelaine','shell');palette={'palette':{'m3surface':'#fff8f6' if light else '#101518','m3primary':'#895143' if light else '#afd4f0','m3onSurface':'#261916' if light else '#e5ebef'}}
                values=theme_values(palette,label if prefs.get('AutoTheme') else prefs.get('Theme','nuit'))
                if prefs.get('Theme')=='custom':values.update(glass=bool(prefs.get('CustomGlass',False)),opacity=prefs.get('CustomGlassOpacity',.12) if prefs.get('CustomGlass') else 1)
                apply(values);signature=current
                (ROOT/'validation/sync-status.json').write_text(json.dumps({'ok':True,'source':label,'auto':prefs.get('AutoTheme',False),'updated':time.time()}),encoding='utf-8')
        except Exception as error:
            (ROOT/'validation/sync-status.json').write_text(json.dumps({'ok':False,'error':str(error),'updated':time.time()}),encoding='utf-8')
            if once:raise
        if once or (ROOT/'theme-sync.stop').exists():break
        time.sleep(4)
if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--once',action='store_true');run(parser.parse_args().once)
