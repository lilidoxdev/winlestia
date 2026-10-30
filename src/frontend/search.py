# Python 3.12 / Windows 11. Local launcher matching and bounded arithmetic; no eval or shell execution.
import ast, json, math, operator, os, shutil, threading, time, unicodedata
from pathlib import Path

def fold(value):
    return ''.join(c for c in unicodedata.normalize('NFD',value.lower()) if not unicodedata.combining(c))

COMMANDS=[
 ('dashboard','Tableau de bord','Horloge, calendrier et lecture','dashboard','navigate'),
 ('controls','Commandes Windows','Volume, écran et connexions','tune','navigate'),
 ('performance','Performances','Températures et activité du PC','speed','navigate'),
 ('workspace:apps','Applications','Tous tes logiciels','apps','navigate'),
 ('workspace:games','Jeux','Ta bibliothèque de jeux','sports_esports','navigate'),
 ('workspace:projects','Projets','Reprendre tes projets','code','navigate'),
 ('workspace:documents','Documents','Dossiers et médias','folder','navigate'),
 ('clipboard','Presse-papiers','Retrouver un élément copié','content_paste','navigate'),
 ('settings','Paramètres','Apparence et comportement','settings','navigate'),
 ('capture','Capture annotée','Encadrer une zone de l’écran','screenshot_monitor','command'),
 ('notes','Note rapide','Bloc-notes persistant','edit_note','command'),
 ('focus','Minuteur','Une session de concentration','timer','command'),
 ('audio','Son et micro','Périphériques et applications','speaker','command'),
 ('profiles','Profils d’espace','Retrouver une session de travail','workspaces','command'),
 ('quicktabs','Onglets rapides','Personnaliser les raccourcis de cette commande','bolt','command'),
 ('tools','Atelier','Les outils du quotidien','handyman','command'),
 ('taskmgr','Gestionnaire des tâches','Processus et démarrage','monitoring','command'),
 ('calculator','Calculatrice Windows','Ouvrir la calculatrice','calculate','command'),
 ('>wallpaper ','Fond d’écran','Wallpaper · changer le fond du bureau','wallpaper','autocomplete')]

def calculate(text):
    text=text.strip()
    if text.startswith('>calc '):text=text[6:]
    if len(text)>160 or not any(c.isdigit() for c in text) or not any(c in '+-*/%^×÷(' for c in text.lstrip('+-')):return None
    try:
        tree=ast.parse(text.replace('^','**').replace(',','.').replace('×','*').replace('÷','/'),mode='eval')
        if sum(1 for _ in ast.walk(tree))>64:return None
        binary={ast.Add:operator.add,ast.Sub:operator.sub,ast.Mult:operator.mul,ast.Div:operator.truediv,ast.Mod:operator.mod}
        def run(node):
            if isinstance(node,ast.Constant) and type(node.value) in (int,float):value=float(node.value)
            elif isinstance(node,ast.UnaryOp) and isinstance(node.op,(ast.UAdd,ast.USub)):value=run(node.operand)*(1 if isinstance(node.op,ast.UAdd) else -1)
            elif isinstance(node,ast.BinOp):
                left,right=run(node.left),run(node.right)
                if isinstance(node.op,ast.Pow):
                    if abs(right)>16 or abs(left)>1e12:raise ValueError()
                    value=left**right
                else:value=binary[type(node.op)](left,right)
            else:raise ValueError()
            if not isinstance(value,(int,float)) or not math.isfinite(value) or abs(value)>1e100:raise ValueError()
            return value
        return format(run(tree.body),'.12g')
    except (ValueError,SyntaxError,OverflowError,ZeroDivisionError,TypeError,KeyError):return None

def palette_search(query,state):
    raw=query.strip();needle=fold(raw.lstrip('>'));words=needle.split();results=[]
    result=calculate(raw)
    if result is not None:results.append({'id':raw,'name':'= '+result,'comment':'Entrée pour copier le résultat','icon':'calculate','kind':'calc'})
    if not raw and not raw.startswith('>'):
        for entry in state.get('systemRecent',[]):results.append({**entry,'kind':'systemRecent'})
        for entry in state.get('recent',[]):
            if not any(r['id']==entry['id'] for r in results):results.append({**entry,'kind':'app','icon':'apps','comment':'Recent'})
    if not raw.startswith('>'):
        for tab in state.get('quickTabs',[]):
            if not raw or all(w in fold(tab['name']) for w in words):results.append({**tab,'kind':'quickTab','icon':'bolt','comment':'Onglet rapide'})
        entries=state.get('apps',[]) if raw else []
        ranked=[]
        for entry in entries:
            name=fold(entry['name']);haystack=name+' '+fold(entry.get('category',''))
            if all(w in haystack for w in words):ranked.append((0 if name.startswith(needle) else 1,name,entry))
        for _,_,entry in sorted(ranked,key=lambda v:v[:2])[:8]:results.append({**entry,'comment':entry.get('category','')+' · '+entry.get('board',''),'kind':'app','icon':'apps'})
        for entry in state.get('websites',[]):
            if all(w in fold(entry['name']) for w in words):results.append({**entry,'kind':'website','icon':'language','comment':'Site internet'})
    for key,name,detail,icon,kind in COMMANDS:
        if all(w in fold(name+' '+detail) for w in words):results.append({'id':key,'name':name,'comment':detail,'icon':icon,'kind':kind})
    if raw and not results:results.append({'id':raw,'name':'Rechercher « '+raw+' »','comment':'Ouvrir dans le navigateur','kind':'web','icon':'travel_explore'})
    return results[:12]


# Wallpaper picker data: scanned only when the launcher wallpaper mode asks for it, scanned on a worker thread at most every 30 s, capped.
WALL_EXT={'.jpg','.jpeg','.png','.bmp','.webp'}
_walls=[];_walls_at=-100.0;_scanning=False;_rescan=False
def user_wallpaper_dir():
    return Path(os.environ.get('LOCALAPPDATA',str(Path.home()/'AppData'/'Local')))/'Panneaux'/'Wallpapers'
def bundled_wallpaper_dir():
    # Installed: <app>\wallpapers next to <app>\Caelestia-Windows; from the sources: <repo>ssets\wallpapers. Read-only.
    here=Path(__file__).resolve().parent
    for candidate in (here.parent/'wallpapers',here.parent.parent/'assets'/'wallpapers'):
        if candidate.is_dir():return candidate
    return here.parent/'wallpapers'
def wallpaper_dirs():
    home=Path(os.environ.get('USERPROFILE',str(Path.home())));windir=Path(os.environ.get('WINDIR','C:/Windows'))
    roots=[(user_wallpaper_dir(),2),(bundled_wallpaper_dir(),2),(home/'Pictures'/'Wallpapers',2),(Path(os.environ.get('OneDrive',home/'OneDrive'))/'Pictures'/'Wallpapers',2),(home/'Pictures',0),(windir/'Web'/'Wallpaper',2),(windir/'Web'/'4K'/'Wallpaper',2)]
    return roots
def _under(path,folder):
    try:
        p=os.path.normcase(os.path.abspath(path));f=os.path.normcase(os.path.abspath(str(folder)))
        return p.startswith(f+os.sep)
    except (OSError,ValueError):return False
def wallpaper_deletable(path):
    # Only files from the user folder; never bundled or Pictures images.
    return _under(path,user_wallpaper_dir()) and os.path.splitext(path)[1].lower() in WALL_EXT and os.path.isfile(path)
def wallpaper_import(paths):
    # Copies picked images into the user folder (unique name on collision); returns the new paths in order.
    dest=user_wallpaper_dir();added=[]
    try:dest.mkdir(parents=True,exist_ok=True)
    except OSError:return added
    for source in paths:
        source=str(source)
        if source.startswith('file:///'):source=source[8:].replace('/',os.sep)
        stem,ext=os.path.splitext(os.path.basename(source))
        if ext.lower() not in WALL_EXT or not os.path.isfile(source):continue
        target=dest/(stem+ext);n=2
        while target.exists():target=dest/f'{stem} ({n}){ext}';n+=1
        try:shutil.copy2(source,target);added.append(str(target))
        except OSError:pass
    return added
def wallpaper_remove(path):
    if not wallpaper_deletable(path):return False
    try:os.remove(path)
    except OSError:return False
    return True
def hidden_wallpaper_file():
    return user_wallpaper_dir().parent/'wallpapers-hidden.json'
def wallpaper_hidden():
    # File names (lower case) of bundled wallpapers the user hid; the install folder itself is never touched.
    try:data=json.loads(hidden_wallpaper_file().read_text(encoding='utf-8'))
    except (OSError,ValueError):return set()
    return {str(n).lower() for n in data} if isinstance(data,list) else set()
def _hidden_write(names):
    f=hidden_wallpaper_file()
    try:
        if names:f.parent.mkdir(parents=True,exist_ok=True);f.write_text(json.dumps(sorted(names),ensure_ascii=False),encoding='utf-8')
        elif f.exists():f.unlink()
    except OSError:return False
    return True
def wallpaper_hide(path):
    # Only bundled images can be hidden.
    if not _under(path,bundled_wallpaper_dir()) or os.path.splitext(path)[1].lower() not in WALL_EXT:return False
    return _hidden_write(wallpaper_hidden()|{os.path.basename(path).lower()})
def wallpaper_restore_hidden():return _hidden_write(set())
def wallpaper_scan(limit=200):
    found=[];seen=set();names=set();user=user_wallpaper_dir();bundled=bundled_wallpaper_dir()
    def walk(folder,depth):
        try:
            with os.scandir(folder) as it:
                for e in it:
                    if len(found)>=limit:return
                    if e.is_dir(follow_symlinks=False) and depth>0:walk(e.path,depth-1)
                    elif e.is_file() and os.path.splitext(e.name)[1].lower() in WALL_EXT and e.path.lower() not in seen and e.name.lower() not in names and os.path.isfile(e.path):
                        seen.add(e.path.lower());names.add(e.name.lower());origin='user' if _under(e.path,user) else 'bundled' if _under(e.path,bundled) else 'other'
                        found.append({'id':e.path,'path':e.path,'name':os.path.splitext(e.name)[0],'url':Path(e.path).as_uri(),'kind':'wallpaper','origin':origin})
        except OSError:pass
    for folder,depth in wallpaper_dirs():walk(str(folder),depth)
    return sorted(found,key=lambda w:fold(w['name']))
def wallpaper_refresh(done,force=False):
    # Worker-thread scan, at most once per 30 s; `done` is called from the worker when the cache was replaced.
    global _walls_at,_scanning,_rescan
    now=time.monotonic()
    if _scanning:
        if force:_rescan=True
        return
    if now-_walls_at<30 and not force:return
    _scanning=True;_walls_at=now
    def run():
        global _walls,_scanning,_rescan
        try:
            _walls=wallpaper_scan()
            while _rescan:_rescan=False;_walls=wallpaper_scan()
        finally:_scanning=False
        done()
    threading.Thread(target=run,daemon=True).start()
def wallpaper_choices(query=''):
    words=fold(query).split();hidden=wallpaper_hidden()
    return [w for w in _walls if os.path.isfile(w['path']) and not (w['origin']=='bundled' and os.path.basename(w['path']).lower() in hidden) and all(x in fold(w['name']) for x in words)]
def wallpaper_known(path):return os.path.isfile(path) and any(w['path']==path for w in _walls)
