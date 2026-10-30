# Python 3.12 / Windows 11. Local launcher matching and bounded arithmetic; no eval or shell execution.
import ast, math, operator, os, threading, time, unicodedata
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
_walls=[];_walls_at=-100.0;_scanning=False
def wallpaper_dirs():
    home=Path(os.environ.get('USERPROFILE',str(Path.home())));windir=Path(os.environ.get('WINDIR','C:/Windows'))
    bundled=Path(os.environ.get('LOCALAPPDATA',str(home/'AppData'/'Local')))/'Panneaux'/'Wallpapers'
    roots=[(bundled,2),(home/'Pictures'/'Wallpapers',2),(Path(os.environ.get('OneDrive',home/'OneDrive'))/'Pictures'/'Wallpapers',2),(home/'Pictures',0),(windir/'Web'/'Wallpaper',2),(windir/'Web'/'4K'/'Wallpaper',2)]
    return roots
def wallpaper_scan(limit=200):
    found=[];seen=set()
    def walk(folder,depth):
        try:
            with os.scandir(folder) as it:
                for e in it:
                    if len(found)>=limit:return
                    if e.is_dir(follow_symlinks=False) and depth>0:walk(e.path,depth-1)
                    elif e.is_file() and os.path.splitext(e.name)[1].lower() in WALL_EXT and e.path.lower() not in seen:
                        seen.add(e.path.lower());found.append({'id':e.path,'path':e.path,'name':os.path.splitext(e.name)[0],'url':Path(e.path).as_uri(),'kind':'wallpaper'})
        except OSError:pass
    for folder,depth in wallpaper_dirs():walk(str(folder),depth)
    return sorted(found,key=lambda w:fold(w['name']))
def wallpaper_refresh(done):
    # Worker-thread scan, at most once per 30 s; `done` is called from the worker when the cache was replaced.
    global _walls_at,_scanning
    now=time.monotonic()
    if _scanning or now-_walls_at<30:return
    _scanning=True;_walls_at=now
    def run():
        global _walls,_scanning
        try:_walls=wallpaper_scan()
        finally:_scanning=False
        done()
    threading.Thread(target=run,daemon=True).start()
def wallpaper_choices(query=''):
    words=fold(query).split()
    return [w for w in _walls if all(x in fold(w['name']) for x in words)]
def wallpaper_known(path):return any(w['path']==path for w in _walls)
