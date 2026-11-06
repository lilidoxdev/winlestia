# Python 3.12 / Windows 11. Local launcher matching and bounded arithmetic; no eval or shell execution.
import ast, json, math, operator, os, re, shutil, threading, time, unicodedata
from pathlib import Path

def tr(text,language='fr'):
    # Same table as the QML qsTr(); French stays the source text. Matching always uses the French originals.
    try:from localization import TABLE
    except ImportError:return text
    return TABLE.get(text,{}).get(language,text)

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
 ('>wallpaper ','Fond d’écran','Wallpaper · changer le fond du bureau','wallpaper','autocomplete'),
 ('>emoji ','Emojis','Emoji · chercher et copier un emoji','mood','autocomplete'),
 ('>file ','Fichiers','File · chercher dans l’index Windows','folder_search','autocomplete'),
 ('shutdown','Éteindre','Shutdown · éteindre le PC (confirmation demandée)','power_settings_new','session'),
 ('restart','Redémarrer','Reboot · redémarrer le PC (confirmation demandée)','cached','session'),
 ('logout','Se déconnecter','Logout · fermer la session (confirmation demandée)','exit_to_app','session'),
 ('lock','Verrouiller','Lock · verrouiller la session (confirmation demandée)','lock','session'),
 ('sleep','Mettre en veille','Sleep · mise en veille (confirmation demandée)','bedtime','session'),
 ('hibernate','Hibernation','Hibernate · hiberner le PC (confirmation demandée)','downloading','session')]

# Windows Settings pages: (ms-settings URI, fr, en, es, extra keywords). Only these URIs can be opened from the palette.
SETTINGS_PAGES=(
 ('ms-settings:network-wifi','Wi-Fi','Wi-Fi','Wi-Fi','wifi reseau sans fil network wireless'),
 ('ms-settings:network','Réseau et Internet','Network & internet','Red e Internet','reseau network ethernet'),
 ('ms-settings:network-vpn','VPN','VPN','VPN',''),
 ('ms-settings:bluetooth','Bluetooth et appareils','Bluetooth & devices','Bluetooth y dispositivos','bluetooth peripheriques devices'),
 ('ms-settings:printers','Imprimantes et scanners','Printers & scanners','Impresoras y escáneres','printer imprimante scanner'),
 ('ms-settings:display','Écran','Display','Pantalla','ecran display resolution luminosite brightness'),
 ('ms-settings:nightlight','Éclairage nocturne','Night light','Luz nocturna','night light lumiere bleue'),
 ('ms-settings:sound','Son','Sound','Sonido','audio volume sound haut-parleur speaker microphone'),
 ('ms-settings:notifications','Notifications','Notifications','Notificaciones','alertes alerts'),
 ('ms-settings:batterysaver','Batterie','Battery','Batería','batterie battery economie energie power'),
 ('ms-settings:powersleep','Alimentation et mise en veille','Power & sleep','Energía y suspensión','veille sleep power'),
 ('ms-settings:storagesense','Stockage','Storage','Almacenamiento','disque disk espace storage'),
 ('ms-settings:multitasking','Multitâche','Multitasking','Multitarea','snap fenetres'),
 ('ms-settings:about','À propos','About','Acerca de','systeme system info version'),
 ('ms-settings:personalization','Personnalisation','Personalization','Personalización','theme fond wallpaper couleurs'),
 ('ms-settings:personalization-colors','Couleurs','Colors','Colores','mode sombre dark accent'),
 ('ms-settings:taskbar','Barre des tâches','Taskbar','Barra de tareas',''),
 ('ms-settings:appsfeatures','Applications installées','Installed apps','Aplicaciones instaladas','desinstaller uninstall programmes programs'),
 ('ms-settings:defaultapps','Applications par défaut','Default apps','Aplicaciones predeterminadas','navigateur browser'),
 ('ms-settings:startupapps','Applications au démarrage','Startup apps','Aplicaciones de inicio','demarrage startup'),
 ('ms-settings:yourinfo','Comptes','Accounts','Cuentas','compte utilisateur account profil'),
 ('ms-settings:dateandtime','Date et heure','Date & time','Fecha y hora','fuseau timezone horloge clock'),
 ('ms-settings:regionformatting','Région','Region','Región','format pays country'),
 ('ms-settings:regionlanguage','Langue','Language','Idioma','langue language'),
 ('ms-settings:typing','Clavier','Keyboard','Teclado','saisie typing'),
 ('ms-settings:mousetouchpad','Souris et pavé tactile','Mouse & touchpad','Ratón y panel táctil','souris mouse touchpad curseur'),
 ('ms-settings:easeofaccess','Accessibilité','Accessibility','Accesibilidad','loupe magnifier narrator'),
 ('ms-settings:privacy','Confidentialité','Privacy','Privacidad','securite security'),
 ('ms-settings:privacy-webcam','Caméra (confidentialité)','Camera privacy','Privacidad de la cámara','camera webcam'),
 ('ms-settings:privacy-microphone','Microphone (confidentialité)','Microphone privacy','Privacidad del micrófono','micro'),
 ('ms-settings:windowsupdate','Windows Update','Windows Update','Windows Update','mise a jour updates'))
SETTINGS_URIS=frozenset(page[0] for page in SETTINGS_PAGES)
SETTINGS_LABEL={'fr':'Paramètres Windows','en':'Windows Settings','es':'Configuración de Windows'}

SESSION_KEYWORDS={'shutdown':('shut down','poweroff','eteindre','arreter','apagar'),'restart':('reboot','redemarrer','reiniciar'),'logout':('log out','sign out','deconnecter','deconnexion','cerrar sesion'),'lock':('verrouiller','bloquear'),'sleep':('veille','mettre en veille','suspend','suspender'),'hibernate':('hibernation','hiberner','hibernar')}

def settings_uri_known(uri):return uri in SETTINGS_URIS

def settings_results(words,language='fr',limit=4):
    column={'fr':1,'en':2,'es':3}.get(language,1);found=[]
    for page in SETTINGS_PAGES:
        if all(w in fold(' '.join(page[1:])) for w in words):found.append({'id':page[0],'name':page[column],'comment':SETTINGS_LABEL.get(language,SETTINGS_LABEL['fr']),'icon':'settings','kind':'setting'})
    return found[:limit]

def window_results(needle,windows,limit=5,language='fr'):
    # Open windows whose title or executable name contains every typed word; the shell's own windows (titles "Panneaux · ...") are left out.
    words=fold(needle).split();ranked=[]
    for entry in windows or []:
        try:hwnd=int(entry.get('hwnd',0))
        except (AttributeError,TypeError,ValueError):continue
        title=str(entry.get('title') or '');exe=str(entry.get('exe') or '')
        if not hwnd or not title or fold(title).startswith('panneaux ·'):continue
        if words and all(w in fold(title+' '+exe) for w in words):ranked.append((0 if fold(title).startswith(words[0]) else 1,hwnd,title,exe))
    ranked.sort(key=lambda v:v[0])
    return [{'id':'window:%d'%hwnd,'hwnd':hwnd,'name':title,'comment':exe or tr('Fenêtre',language),'icon':'web_asset','kind':'window'} for _,hwnd,title,exe in ranked[:limit]]

CALC_WORDS=((re.compile(r'(?<=[\d)\s])\s*divis[eé]e?\s+par\s*',re.I),'/'),(re.compile(r'\sfois\s',re.I),'*'),(re.compile(r'\splus\s',re.I),'+'),(re.compile(r'\smoins\s',re.I),'-'))
TIME_LIKE=re.compile(r'^\d{1,2}:\d{2}(:\d{2})?\s*(am|pm)?$',re.I)
def calculate(text,language='fr'):
    text=text.strip()
    if text.startswith('>calc '):text=text[6:].strip()
    if len(text)>160 or TIME_LIKE.match(text):return None
    for pattern,symbol in CALC_WORDS:text=pattern.sub(symbol,text)
    text=text.replace(':','/')
    if not any(c.isdigit() for c in text) or not any(c in '+-*/%^×÷(' for c in text.lstrip('+-')):return None
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
        shown=format(run(tree.body),'.12g')
        return shown.replace('.',',') if language in ('fr','es') else shown
    except (ValueError,SyntaxError,OverflowError,ZeroDivisionError,TypeError,KeyError):return None

def palette_search(query,state,language='fr',windows=None):
    raw=query.strip();needle=fold(raw.lstrip('>'));words=needle.split();results=[]
    result=calculate(raw,language)
    if result is not None:results.append({'id':raw,'name':'= '+result,'comment':tr('Entrée pour copier le résultat',language),'icon':'calculate','kind':'calc'})
    if not raw and not raw.startswith('>'):
        for entry in state.get('systemRecent',[]):results.append({**entry,'kind':'systemRecent'})
        for entry in state.get('recent',[]):
            if not any(r['id']==entry['id'] for r in results):results.append({**entry,'kind':'app','icon':'apps','comment':tr('Récents',language)})
    if not raw.startswith('>'):
        for tab in state.get('quickTabs',[]):
            name=tr(tab['name'],language)
            if not raw or all(w in fold(tab['name']) or w in fold(name) for w in words):results.append({**tab,'name':name,'kind':'quickTab','icon':'bolt','comment':tr('Onglet rapide',language)})
        if raw:results.extend(window_results(needle,windows,language=language))
        entries=state.get('apps',[]) if raw else []
        ranked=[]
        for entry in entries:
            name=fold(entry['name']);haystack=name+' '+fold(entry.get('category',''))
            if all(w in haystack for w in words):ranked.append((0 if name.startswith(needle) else 1,name,entry))
        for _,_,entry in sorted(ranked,key=lambda v:v[:2])[:8]:results.append({**entry,'comment':entry.get('category','')+' · '+entry.get('board',''),'kind':'app','icon':'apps'})
        for entry in state.get('websites',[]):
            if all(w in fold(entry['name']) for w in words):results.append({**entry,'kind':'website','icon':'language','comment':tr('Site internet',language)})
    for key,name,detail,icon,kind in COMMANDS:
        if kind=='session':
            # Session actions are destructive: whole-query prefix of the name, an explicit keyword or the id, at least 3 letters, never a substring.
            if len(needle)<3 or not any(c.startswith(needle) for c in [fold(name),key,*SESSION_KEYWORDS.get(key,())]):continue
        elif not all(w in fold(name+' '+detail) for w in words):continue
        results.append({'id':key,'name':tr(name,language),'comment':tr(detail,language),'icon':icon,'kind':kind})
    if raw and not raw.startswith('>'):results.extend(settings_results(words,language))
    if raw and not results:results.append({'id':raw,'name':tr('Rechercher « %1 »',language).replace('%1',raw),'comment':tr('Ouvrir dans le navigateur',language),'kind':'web','icon':'travel_explore'})
    return results[:12]


_emoji_index=None
def emoji_choices(query):
    # Bundled table read on first use only; matches every typed word against French and English keywords (accent-insensitive).
    global _emoji_index
    if _emoji_index is None:
        from emoji_data import EMOJI_ROWS
        _emoji_index=[(glyph,fold(fr+' '+en),fr.split(' ')[0]) for glyph,fr,en in EMOJI_ROWS]
    words=fold(query).split()
    return [{'id':glyph,'name':glyph,'comment':label,'kind':'emoji'} for glyph,hay,label in _emoji_index if all(w in hay for w in words)]

# Wallpaper picker data: scanned only when the launcher wallpaper mode asks for it, scanned on a worker thread at most every 30 s, capped.
WALL_EXT={'.jpg','.jpeg','.png','.bmp','.webp'}
_walls=[];_walls_at=-100.0;_scanning=False;_rescan=False
def user_wallpaper_dir():
    return Path(os.environ.get('WINLESTIA_DATA_DIR') or (Path(os.environ.get('LOCALAPPDATA',str(Path.home()/'AppData'/'Local')))/'Panneaux'))/'Wallpapers'
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
