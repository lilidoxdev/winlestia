# Python 3.12 / Windows 11. Local launcher matching and bounded arithmetic; no eval or shell execution.
import ast, math, operator, unicodedata

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
 ('calculator','Calculatrice Windows','Ouvrir la calculatrice','calculate','command')]

def calculate(text):
    text=text.strip()
    if text.startswith('>calc '):text=text[6:]
    if len(text)>160 or not any(c.isdigit() for c in text):return None
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
