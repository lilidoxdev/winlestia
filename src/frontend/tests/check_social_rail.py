# Offscreen check of SocialRail.qml + the "Réseaux sociaux" settings tab with a real Bridge fed a fake `social` state.
import os,sys,json,queue
os.environ.setdefault('QT_QPA_PLATFORM','offscreen')
from pathlib import Path
from types import SimpleNamespace
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PySide6.QtCore import Qt,QTimer,QUrl,QObject,QPointF,QPoint,qInstallMessageHandler
from PySide6.QtGui import QFontDatabase
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtTest import QTest
from frontend import Bridge,ROOT
app=QApplication([]);app.setQuitOnLastWindowClosed(False);issues=[];checks={}
qInstallMessageHandler(lambda kind,context,message:issues.append(message) if int(kind)>0 and 'QFontDatabase' not in message and 'window masks' not in message else None)
for font in (ROOT/'assets/fonts').glob('*.ttf'):QFontDatabase.addApplicationFont(str(font))
bridge=Bridge(SimpleNamespace(pipe='WinLestiaSocialTest',backend_pid=0,preview=True));bridge.pointer.stop();bridge.city=''
sent=[]
class Recorder(queue.Queue):
    def put(self,item,*a,**k):sent.append(item);super().put(item,*a,**k)
bridge.client.commands=Recorder()
def net(i,n,enabled,connected,count,muted=False):return {'id':i,'name':n,'enabled':enabled,'connected':connected,'muted':muted,'count':count,'busy':False}
import time
now=int(time.time()*1000)
history=[{'seq':3,'net':'whatsapp','title':'Alice','body':'Salut, tu viens ? '*8,'kind':'notif','at':now-5*60000},{'seq':2,'net':'discord','title':'3 nouveaux messages','body':'','kind':'count','at':now-1000},{'seq':1,'net':'gmail','title':'Bob','body':'Facture','kind':'notif','at':now-90*60000}]
social={'history':history,'muted':False,'sound':True,'volume':70,'networks':[net('discord','Discord',True,True,3),net('instagram','Instagram',True,False,5),net('snapchat','Snapchat',False,False,0),net('whatsapp','WhatsApp',True,True,120,True),net('telegram','Telegram',False,False,0),net('gmail','Gmail',True,True,7)]}
bridge.update_state({'state':{'settings':{'themeMode':'dark','motionScale':0},'social':social}})
engine=QQmlApplicationEngine();engine.addImportPath(str(ROOT/'qml'));engine.rootContext().setContextProperty('Bridge',bridge)
engine.load(QUrl.fromLocalFile(str(ROOT/'qml/SocialRail.qml')))
rail=engine.rootObjects()[0] if engine.rootObjects() else None
def items(item):
    yield item
    for child in item.childItems():yield from items(child)
def find(name):return next((i for i in items(rail.contentItem()) if i.objectName()==name),None)
def zone_ok():
    z=bridge.social_zone(rail.screen().geometry().right());ok=True
    for n in ('socialChevron','social-discord','social-gmail','social-instagram','social-whatsapp','socialDrawer'):
        it=find(n)
        if it is None or not it.property('visible'):continue
        c=it.mapToScene(QPointF(it.width()/2,it.height()/2));ok=ok and z.contains(QPoint(round(rail.x()+c.x()),round(rail.y()+c.y())))
    return ok
def mask_ok(left_probe):
    if bridge.surface_scene is None:
        from surface_geometry import SurfaceScene;bridge.social_win=rail;bridge.surface_scene=SurfaceScene(bridge)
    bridge.surface_scene.refresh();m=rail.mask();ok=not m.isEmpty()
    for n in ('socialChevron','social-discord','social-gmail','social-instagram','social-whatsapp'):
        it=find(n);c=it.mapToScene(QPointF(it.width()/2,it.height()/2));ok=ok and m.contains(QPoint(round(c.x()),round(c.y())))
    return ok and not m.contains(QPoint(round(rail.width()-rail.property('bumpWidth')-left_probe),round(rail.height()/2)))
def run():
    logos=[i.objectName() for i in items(rail.contentItem()) if i.objectName().startswith('social-')]
    checks['logos']=sorted(logos)==['social-discord','social-gmail','social-instagram','social-whatsapp']
    checks['badge3']=find('badgeText-discord').property('text')=='3'
    checks['badgeGmail']=find('badgeText-gmail').property('text')=='7'
    checks['badge99']=find('badgeText-whatsapp').property('text')=='99+'
    checks['mutedIcon']=bool(find('muted-whatsapp').property('visible')) and not find('muted-discord').property('visible')
    from surface_geometry import rail_path
    W,H=rail.property('bumpWidth'),rail.property('bumpHeight');WW,WH=rail.width(),rail.height();ox=WW-W;oy=(WH-H)/2;P=rail_path(W,H,True);b=P.boundingRect();r=26
    checks['windowFixedSize']=abs(WW-(rail.property('railW')+340))<.5 and WH>=H
    checks['railBounds']=abs(b.width()-W-2)<.5 and abs(b.height()-H)<.5
    checks['convexLeft']=(not P.contains(QPointF(1,1))) and (not P.contains(QPointF(1,H-1))) and P.contains(QPointF(1,H/2)) and P.contains(QPointF(r,r+1))
    checks['concaveBase']=P.contains(QPointF(W-3,20)) and P.contains(QPointF(W-3,H-20)) and not P.contains(QPointF(W-10,3)) and not P.contains(QPointF(W-10,H-3)) and P.contains(QPointF(W-1,H/2))
    ch_w=rail.property('chevronW');tiles=[find(n) for n in ('social-discord','social-gmail','social-instagram','social-whatsapp','socialGear')];pad=15
    ok=True
    for t in tiles:
        tl=t.mapToScene(QPointF(-ox,-oy));ok=ok and tl.x()>=pad-.5 and tl.x()+t.width()<=W-ch_w-pad+.5 and all(P.contains(QPointF(tl.x()+dx,tl.y()+dy)) for dx in (0,t.width()) for dy in (0,t.height()))
    for n in ('badge-discord','badge-gmail'):
        bd=find(n);tl=bd.mapToScene(QPointF(-ox,-oy));ok=ok and tl.x()>=0 and P.contains(QPointF(tl.x()+1,tl.y()+1))
    checks['contentInsideBump']=ok
    checks['noBadgeUnconnected']=not find('badge-instagram').property('visible')
    checks['disabledAbsent']=find('social-snapchat') is None and find('social-telegram') is None
    from surface_geometry import shell_path
    SW,SH,T=1000,600,22;band=True
    for off in (0,10,40,W):
        Q=shell_path(SW,SH,T,rail=(SW-T-W+off,(SH-H)/2,W,H,True)).simplified()
        band=band and Q.contains(QPointF(SW-T/2,SH/2)) and Q.contains(QPointF(SW-T/2,SH/2+H/2-40))
    checks['bandNeverVanishes']=band
    ch=find('socialChevron');cb=ch.mapToScene(QPointF(-ox,-oy))
    tile_right=max(t.mapToScene(QPointF(-ox,0)).x()+t.width() for t in tiles)
    checks['railWidthCollapsed']=abs(W-(54+2*15+28))<.5
    checks['chevronRightOfLogosAndBig']=ch.width()>=24 and ch.height()>=56 and cb.x()>=tile_right and cb.x()+ch.width()<=W+.5
    ov=False
    for n in ('badge-discord','badge-gmail','badge-instagram','badge-whatsapp'):
        bd=find(n);bt=bd.mapToScene(QPointF(-ox,-oy))
        if bt.x()<cb.x()+ch.width() and bt.x()+bd.width()>cb.x() and bt.y()<cb.y()+ch.height() and bt.y()+bd.height()>cb.y():ov=True
    checks['chevronNoBadgeOverlap']=not ov
    checks['gear']=find('socialGear') is not None
    checks['maskCollapsed']=mask_ok(10);checks['zoneCollapsed']=zone_ok()
    rail.setProperty('shown',False)
    sent.clear();rail.setProperty('shown',True)
    def click():
        item=find('social-discord');p=item.mapToScene(QPointF(item.width()/2,item.height()/2))
        QTest.mouseClick(rail,Qt.LeftButton,Qt.NoModifier,QPoint(round(p.x()),round(p.y())))
        QTimer.singleShot(150,after)
    def after():
        checks['clickSocialOpen']=any(c['command']=='socialOpen' and c['args']=={'id':'discord'} for c in sent)
        checks['hideAfterClick']=rail.property('shown') is False
        sent.clear();rail.setProperty('shown',True)
        def click2():
            item=find('social-instagram');p=item.mapToScene(QPointF(item.width()/2,item.height()/2))
            QTest.mouseClick(rail,Qt.LeftButton,Qt.NoModifier,QPoint(round(p.x()),round(p.y())))
            QTimer.singleShot(150,after2)
        QTimer.singleShot(900,click2)
    def after2():
        checks['clickSocialLogin']=any(c['command']=='socialLogin' and c['args']=={'id':'instagram'} for c in sent) and not any(c['command']=='socialOpen' for c in sent)
        sent.clear();rail.setProperty('shown',True);rail.setProperty('expanded',False)
        def chevron_click():
            c=find('socialChevron');p=c.mapToScene(QPointF(c.width()/2,c.height()/2))
            QTest.mouseClick(rail,Qt.LeftButton,Qt.NoModifier,QPoint(round(p.x()),round(p.y())))
            QTimer.singleShot(60,lambda:checks.__setitem__('chevronClickExpands',rail.property('expanded') is True))
            QTimer.singleShot(1100,after3)
        QTimer.singleShot(900,chevron_click)
    def after3():
        from surface_geometry import rail_path,shell_path
        W,H=rail.property('bumpWidth'),rail.property('bumpHeight');checks['expandedWidth']=abs(W-(rail.property('railW')+340))<1.5 and H>=rail.property('baseHeight') and abs(rail.width()-(rail.property('railW')+340))<.5
        d=find('socialDrawer');dc=find('socialChevron');lt=[find(n) for n in ('social-discord','social-gmail','social-instagram','social-whatsapp')]
        lr=max(t.mapToScene(QPointF(0,0)).x()+t.width() for t in lt);dx=d.mapToScene(QPointF(0,0)).x();cx=dc.mapToScene(QPointF(0,0)).x()
        checks['orderLogosDrawerChevron']=lr<=dx and dx+d.width()<=cx+.5 and d.width()>150
        checks['drawerOpaqueWhenOpen']=d.property('opacity')>.99
        b=rail_path(W,H,True).boundingRect();checks['expandedPath']=abs(b.width()-W-2)<.5 and abs(b.height()-H)<.5
        SW,SH,T=1000,600,22;band=True
        for off in (0,10,40,W):
            Q=shell_path(SW,SH,T,rail=(SW-T-W+off,(SH-H)/2,W,H,True)).simplified()
            band=band and Q.contains(QPointF(SW-T/2,SH/2)) and Q.contains(QPointF(SW-T/2,SH/2+H/2-40))
        checks['bandNeverVanishesExpanded']=band
        spec=(SW-T-W,(SH-H)/2,W,H,True)
        Q=shell_path(SW,SH,T,rail=spec);checks['silhouetteCoversDrawer']=Q.contains(QPointF(spec[0]+W/2,SH/2)) and Q.simplified().contains(QPointF(spec[0]+W/2,SH/2)) and Q.contains(QPointF(spec[0]+8,SH/2))
        checks['maskExpanded']=mask_ok(10);checks['zoneExpanded']=zone_ok()
        anim={'ok':True,'n':0,'w':set()}
        def sample():
            bw=rail.property('bumpWidth');left=rail.width()-bw+rail.property('offset');anim['w'].add(round(bw/20))
            for n in ('social-discord','social-gmail','social-instagram','social-whatsapp','socialGear','socialDrawer'):
                it=find(n)
                if it is None:continue
                a0=it.mapToScene(QPointF(0,0)).x();b0=a0+it.width()
                if a0<left-1.5 or b0>rail.width()+rail.property('offset')+.5:anim['ok']=False
            d=find('socialDrawer');dl=d.mapToScene(QPointF(0,0)).x()
            if dl<left-1.5:anim['ok']=False
            anim['n']+=1
        rail.setProperty('expanded',False)
        for t in (30,80,140,220,320):QTimer.singleShot(t,sample)
        QTimer.singleShot(400,lambda:(checks.__setitem__('animInsideBump',anim['ok'] and anim['n']==5 and len(anim['w'])>=3),rail.setProperty('expanded',True),QTimer.singleShot(900,after3b)))
    def after3b():
        lst=find('socialHistoryList');checks['historyCount']=lst is not None and lst.property('count')==3
        first=next((i for i in items(rail.contentItem()) if i.objectName()=='history-0'),None)
        if first is None:
            lv=[i for i in items(rail.contentItem()) if i.objectName().startswith('history-')];checks['historyDelegates']=len(lv)
        checks['emptyHiddenWhenFull']=not find('socialHistoryEmpty').property('visible')
        p=first.mapToScene(QPointF(first.width()/2,first.height()/2))
        QTest.mouseClick(rail,Qt.LeftButton,Qt.NoModifier,QPoint(round(p.x()),round(p.y())))
        QTimer.singleShot(150,after4)
    def after4():
        checks['historyClickOpen']=any(c['command']=='socialOpen' and c['args']=={'id':'whatsapp'} for c in sent)
        # same seq, changed title/body (count -> notif) must be updated in place
        h2=[dict(history[0]),{**history[1],'title':'Carol','body':'Nouveau texte','kind':'notif'},dict(history[2])]
        bridge.update_state({'state':{'settings':{'themeMode':'dark','motionScale':0},'social':{**social,'history':h2}}})
        def upd():
            titles=[t.property('text') for t in items(rail.contentItem()) if t.property('text')=='Carol']
            checks['historyUpdateInPlace']=bool(titles) and find('socialHistoryList').property('count')==3 and not any(t.property('text')=='3 nouveaux messages' for t in items(rail.contentItem()))
        QTimer.singleShot(150,upd)
        sent.clear();rail.setProperty('shown',True);rail.setProperty('expanded',True)
        def clear():
            c=find('socialHistoryClear');p=c.mapToScene(QPointF(c.width()/2,c.height()/2))
            QTest.mouseClick(rail,Qt.LeftButton,Qt.NoModifier,QPoint(round(p.x()),round(p.y())))
            QTimer.singleShot(100,empty)
        def empty():
            checks['historyClear']=any(c['command']=='socialHistoryClear' for c in sent)
            bridge.update_state({'state':{'settings':{'themeMode':'dark','motionScale':0},'social':{**social,'history':[]}}})
            QTimer.singleShot(200,lambda:(checks.__setitem__('emptyState',bool(find('socialHistoryEmpty').property('visible')) and find('socialHistoryList').property('count')==0),prefs()))
        QTimer.singleShot(900,clear)
    def prefs():
        # settings tab
        qml='import QtQuick\nimport QtQuick.Window\nWindow{width:900;height:700;visible:true;ShellPreferences{objectName:"prefs";anchors.fill:parent;tab:4}}'
        global prefs_engine
        prefs_engine=QQmlApplicationEngine();prefs_engine.addImportPath(str(ROOT/'qml'));prefs_engine.rootContext().setContextProperty('Bridge',bridge)
        prefs_engine.loadData(qml.encode(),QUrl.fromLocalFile(str(ROOT/'qml/_h.qml')))
        QTimer.singleShot(800,prefs_done)
    QTimer.singleShot(300,click)
def prefs_done():
    w=prefs_engine.rootObjects()[0] if prefs_engine.rootObjects() else None
    names=[i.objectName() for i in items(w.contentItem())] if w else []
    checks['prefsRows']=all(f'socialRow-{x}' in names for x in ('discord','instagram','snapchat','whatsapp','telegram','gmail')) and 'socialGlobalMute' in names and 'socialVolume' in names
    checks['qmlIssues']=not issues
    ok=bool(rail) and all(checks.values())
    print(json.dumps({'checks':checks,'issues':issues[:8],'allPassed':ok}),flush=True)
    bridge.stop();os._exit(0 if ok else 1)
if rail is None:print(issues);os._exit(1)
QTimer.singleShot(400,run);app.exec()
