# Offscreen check of BookmarksRail.qml (left edge, mirror of SocialRail) + the "Favoris" settings tab with a real Bridge fed a fake `bookmarks` state.
import os,sys,json,queue,tempfile
os.environ.setdefault('QT_QPA_PLATFORM','offscreen')
from pathlib import Path
from types import SimpleNamespace
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PySide6.QtCore import Qt,QTimer,QUrl,QObject,QPointF,QPoint,qInstallMessageHandler
from PySide6.QtGui import QFontDatabase,QImage,QColor
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtTest import QTest
from frontend import Bridge,ROOT
app=QApplication([]);app.setQuitOnLastWindowClosed(False);issues=[];checks={}
qInstallMessageHandler(lambda kind,context,message:issues.append(message) if int(kind)>0 and 'QFontDatabase' not in message and 'window masks' not in message else None)
for font in (ROOT/'assets/fonts').glob('*.ttf'):QFontDatabase.addApplicationFont(str(font))
bridge=Bridge(SimpleNamespace(pipe='WinLestiaBookmarksTest',backend_pid=0,preview=True));bridge.pointer.stop();bridge.city=''
sent=[]
class Recorder(queue.Queue):
    def put(self,item,*a,**k):sent.append(item);super().put(item,*a,**k)
bridge.client.commands=Recorder();nav=[];bridge.navigate=lambda mode,*a:nav.append(mode)
png=Path(tempfile.gettempdir())/'wl_bm_icon.png';img=QImage(32,32,QImage.Format_ARGB32);img.fill(QColor('#2060c0'));img.save(str(png))
icon=QUrl.fromLocalFile(str(png)).toString()
def it(i,title,icon='',folder=False,children=None):return {'id':i,'title':title,'url':'' if folder else f'https://{title.lower()}.example/','icon':icon,'folder':folder,'children':children or []}
BM={'browser':'edge','browsers':[{'id':'edge','name':'Microsoft Edge','installed':True,'isDefault':True},{'id':'chrome','name':'Google Chrome','installed':True,'isDefault':False},{'id':'brave','name':'Brave','installed':False,'isDefault':False}],
    'items':[it('edge:a','Alpha',icon),it('edge:b','Bravo',folder=True,children=[it('edge:c','Charlie'),it('edge:d','Delta')]),it('edge:e','Echo'),it('edge:other','Autres favoris',folder=True,children=[it('edge:f','Foxtrot')])],
    'hiddenCount':2,'canDeleteInBrowser':True,'busy':False,'error':''}
VER=0
def push(**k):
    global BM
    global VER
    BM={**BM,**k};fresh='items' in k or VER==0
    if fresh:VER+=1
    msg={'state':{'settings':{'themeMode':'dark','motionScale':0},'bookmarks':{**{x:y for x,y in BM.items() if x!='items'},'version':VER}}}
    if fresh:msg['bookmarksItems']={'version':VER,'items':BM['items']}
    bridge.update_state(msg)
push()
engine=QQmlApplicationEngine();engine.addImportPath(str(ROOT/'qml'));engine.rootContext().setContextProperty('Bridge',bridge)
engine.load(QUrl.fromLocalFile(str(ROOT/'qml/BookmarksRail.qml')))
rail=engine.rootObjects()[0] if engine.rootObjects() else None
def items(item):
    yield item
    for child in item.childItems():yield from items(child)
def find(name,root=None):return next((i for i in items((root or rail).contentItem()) if i.objectName()==name),None)
def center(w,item):
    p=item.mapToScene(QPointF(item.width()/2,item.height()/2));return QPoint(round(p.x()),round(p.y()))
def rows():return sorted([i for i in items(rail.contentItem()) if i.objectName().startswith('bookmark-') and i.property('visible')],key=lambda i:i.property('index'))
def ids():return [r.property('id') for r in rows()]
def click(w,item,button=Qt.LeftButton):QTest.mouseClick(w,button,Qt.NoModifier,center(w,item))
menu=None
anim={'ok':True,'n':0,'w':set()}
def run():
    global menu
    menu=rail.findChild(QObject,'bookmarksMenu')
    from surface_geometry import shell_path,rail_path,SurfaceScene
    W,H=rail.property('bumpWidth'),rail.property('bumpHeight')
    checks['sideLeft']=rail.property('isLeft') is True and rail.x()<=bridge.screenX+40
    checks['windowFixedSize']=abs(rail.width()-W)<.5
    checks['railWidthCollapsed']=abs(W-(54+2*15))<.5
    b=rail_path(W,H,True,'left').boundingRect();r=rail_path(W,H,True,'right').boundingRect()
    checks['leftPathMirror']=abs(b.width()-r.width())<.5 and abs(b.height()-r.height())<.5 and abs(b.left()+2)<.5
    p=rail_path(W,H,True,'left');checks['leftPathConvexTip']=p.contains(QPointF(W-3,H/2)) and not p.contains(QPointF(W+3,H/2))
    SW,SH,T=1000,600,22;band=True
    for off in (0,10,40,W):
        Q=shell_path(SW,SH,T,left=(T-off,(SH-H)/2,W,H,True)).simplified()
        band=band and Q.contains(QPointF(T/2,SH/2)) and Q.contains(QPointF(T/2,SH/2+H/2-40)) and Q.contains(QPointF(T/2,SH/2-H/2+40))
    checks['bandNeverVanishes']=band
    checks['bandHasTip']=shell_path(SW,SH,T,left=(T,(SH-H)/2,W,H,True)).simplified().contains(QPointF(T+W/2,SH/2))
    # mask + zone
    bridge.bookmarks_win=rail;bridge.surface_scene=SurfaceScene(bridge);bridge.surface_scene.refresh();m=rail.mask()
    tiles=rows()
    checks['tiles']=len(tiles)==4 and tiles[0].property('id')=='edge:a'
    pts=[center(rail,t) for t in tiles]+[center(rail,find('bookmarksGear'))]
    checks['maskContainsTilesAndGear']=not m.isEmpty() and all(m.contains(q) for q in pts)
    checks['maskExcludesRightOfBump']=not m.contains(QPoint(round(W+10),round(rail.height()/2)))
    z=bridge.edge_zone(rail,rail.screen().geometry().left(),'left')
    checks['zoneContainsTiles']=all(z.contains(QPoint(rail.x()+q.x(),rail.y()+q.y())) for q in pts)
    checks['zoneExcludesRight']=not z.contains(QPoint(rail.x()+round(W)+40,rail.y()+round(rail.height()/2)))
    # content inside the displayed bump while sliding in
    def sample():
        bw=rail.property('bumpWidth');off=rail.property('offset');anim['w'].add(round(off/10))
        for q in [find('bookmark-0'),find('bookmarksGear'),find('bookmarksList')]:
            a=q.mapToScene(QPointF(0,0)).x();c=a+q.width()
            if a<-off-1.5 or c>bw-off+.5:anim['ok']=False
        anim['n']+=1
    rail.setProperty('shown',False)
    def reveal():
        sent.clear();rail.setProperty('shown',True)
        for t in (30,80,140,220,320):QTimer.singleShot(t,sample)
        QTimer.singleShot(500,after_anim)
    QTimer.singleShot(400,reveal)
def after_anim():
    checks['contentInsideBumpWhileSliding']=anim_ok()
    refresh=[c for c in sent if c['command']=='bookmarksRefresh']
    checks['refreshOncePerReveal']=len(refresh)==1 and refresh[0]['args']=={'ifDirty':True}
    # initial / favicon
    r0,r2=rows()[0],rows()[2]
    checks['faviconShownOrInitialHidden']=bool(find('initial-0') is not None and not find('initial-0').property('visible') or find('favicon-0').property('status')!=1)
    checks['initialWhenNoIcon']=find('initial-2').property('visible') and find('initial-2').property('text')=='E'
    # click opens
    sent.clear();click(rail,rows()[2])
    QTimer.singleShot(150,step_open)
def anim_ok():
    return anim['ok'] and anim['n']==5 and len(anim['w'])>=3
def step_open():
    checks['clickBookmarksOpen']=any(c['command']=='bookmarksOpen' and c['args']=={'id':'edge:e'} for c in sent)
    checks['hideAfterClick']=rail.property('shown') is False
    rail.setProperty('shown',True);QTimer.singleShot(900,step_folder)
def step_folder():
    n0=len(rows());click(rail,rows()[1]);QTimer.singleShot(200,lambda:step_folder2(n0))
def step_folder2(n0):
    checks['folderExpandsInPlace']=n0==4 and ids()==['edge:a','edge:b','edge:c','edge:d','edge:e','edge:other'] and rows()[2].property('depth')==1
    global keep
    keep={r.property('id'):r for r in rows()}
    # sync by id: new row on top, renamed folder, reordered tail; surviving delegates must be the same objects
    new=[it('edge:n','November')]+[dict(x,title='Bravo2') if x['id']=='edge:b' else x for x in BM['items']]
    push(items=new);QTimer.singleShot(250,step_sync)
def step_sync():
    now={r.property('id'):r for r in rows()}
    same=all(now.get(k) is v for k,v in keep.items() if k in ('edge:a','edge:c','edge:e'))
    checks['syncByIdKeepsDelegates']=same and ids()[0]=='edge:n' and now['edge:b'].property('title')=='Bravo2'
    # state removal
    push(items=[x for x in BM['items'] if x['id']!='edge:a']);QTimer.singleShot(250,step_state)
def step_state():
    checks['syncRemovesById']='edge:a' not in ids()
    # right click -> menu
    click(rail,rows()[0],Qt.RightButton);QTimer.singleShot(250,step_menu)
def step_menu():
    checks['menuOpensOnRightClick']=bool(menu.property('visible'))
    hide=find('bookmarksMenuHide',menu);dele=find('bookmarksMenuDelete',menu)
    checks['menuHasHideAndDelete']=hide.property('visible') and dele.property('visible')
    sent.clear();click(menu,hide);QTimer.singleShot(200,step_hide)
def step_hide():
    checks['hideAction']=any(c['command']=='bookmarksHide' and c['args']=={'id':'edge:n'} for c in sent) and not menu.property('visible')
    # folders that cannot be deleted in the browser
    other=next(r for r in rows() if r.property('id')=='edge:other')
    rail.setProperty('shown',True)
    pos=center(rail,other)
    QTest.mouseClick(rail,Qt.RightButton,Qt.NoModifier,pos);QTimer.singleShot(250,step_other)
def step_other():
    dele=find('bookmarksMenuDelete',menu)
    checks['noDeleteOnOtherFolder']=bool(menu.property('visible')) and not dele.property('visible')
    menu.setProperty('visible',False)
    r=next(r for r in rows() if r.property('id')=='edge:e');QTest.mouseClick(rail,Qt.RightButton,Qt.NoModifier,center(rail,r));QTimer.singleShot(250,step_del)
def step_del():
    dele=find('bookmarksMenuDelete',menu);checks['deleteVisibleOnNormalItem']=bool(dele.property('visible'))
    click(menu,dele);QTimer.singleShot(150,step_confirm)
def step_confirm():
    checks['confirmShown']=menu.property('mode')=='confirm' and find('bookmarksMenuConfirm',menu).property('visible')
    sent.clear();click(menu,find('bookmarksMenuConfirm',menu));QTimer.singleShot(150,step_synced)
def step_synced():
    d=[c for c in sent if c['command']=='bookmarksDeleteInBrowser']
    checks['deleteSentWithoutConfirmSync']=len(d)==1 and d[0]['args']=={'id':'edge:e'}
    push(error='sync');QTimer.singleShot(300,step_sync_warn)
def step_sync_warn():
    checks['syncWarningShown']=menu.property('mode')=='sync' and bool(menu.property('visible'))
    sent.clear();click(menu,find('bookmarksMenuConfirm',menu));QTimer.singleShot(150,step_sync_confirm)
def step_sync_confirm():
    d=[c for c in sent if c['command']=='bookmarksDeleteInBrowser']
    checks['deleteSentWithConfirmSync']=len(d)==1 and d[0]['args']=={'id':'edge:e','confirmSync':True}
    push(error='');step_states()
def step_states():
    push(items=[],busy=True)
    def a():
        s=find('bookmarksState');checks['busyState']=s.property('visible') and s.property('kind')=='busy'
        push(items=[],busy=False,error='running');QTimer.singleShot(250,b)
    def b():
        s=find('bookmarksState');checks['errorState']=s.property('visible') and s.property('kind')=='error'
        push(items=[],busy=False,error='');QTimer.singleShot(250,c)
    def c():
        s=find('bookmarksState');checks['emptyState']=s.property('visible') and s.property('kind')=='empty'
        many=[it(f'edge:{i}',f'Site{i}') for i in range(60)]
        push(items=many);QTimer.singleShot(300,d)
    def d():
        lv=find('bookmarksView');box=find('bookmarksList')
        checks['listScrolls']=lv.property('contentHeight')>box.height()+1 and rail.height()<=bridge.screenHeight-80+.5 and box.height()<=rail.property('maxList')+.5
        push(items=BM['items'][:2]);QTimer.singleShot(200,gear)
    QTimer.singleShot(250,a)
def gear():
    rail.setProperty('shown',True)
    def go():
        sent.clear();click(rail,find('bookmarksGear'));QTimer.singleShot(150,prefs)
    QTimer.singleShot(900,go)
def prefs():
    checks['gearOpensSettings']=nav==['settings:bookmarks'] and rail.property('shown') is False
    qml='import QtQuick\nimport QtQuick.Window\nWindow{width:900;height:700;visible:true;ShellPreferences{objectName:"prefs";anchors.fill:parent;tab:5}}'
    global prefs_engine
    prefs_engine=QQmlApplicationEngine();prefs_engine.addImportPath(str(ROOT/'qml'));prefs_engine.rootContext().setContextProperty('Bridge',bridge)
    prefs_engine.loadData(qml.encode(),QUrl.fromLocalFile(str(ROOT/'qml/_h.qml')))
    QTimer.singleShot(800,prefs_done)
def prefs_done():
    w=prefs_engine.rootObjects()[0] if prefs_engine.rootObjects() else None
    names=[i.objectName() for i in items(w.contentItem())] if w else []
    checks['prefsBrowsers']='bookmarksBrowser-edge' in names and 'bookmarksBrowser-chrome' in names and 'bookmarksBrowser-brave' not in names
    unhide=next((i for i in items(w.contentItem()) if i.objectName()=='bookmarksUnhideAll'),None) if w else None
    checks['prefsUnhide']=unhide is not None and bool(unhide.property('enabled'))
    checks['qmlIssues']=not issues
    ok=bool(rail) and all(checks.values())
    print(json.dumps({'checks':checks,'issues':issues[:8],'allPassed':ok}),flush=True)
    bridge.stop();os._exit(0 if ok else 1)
if rail is None:print(issues);os._exit(1)
QTimer.singleShot(400,run);app.exec()
