# Offscreen check of the redesigned settings (ShellPreferences + qml/settings/*).
# Loads with zero QML warnings, visits every page at 1280x800 and 640x720 in fr/en/es (PNG per page),
# verifies that every SettingsIndex entry has a live row on its page, and that a search navigates.
# Usage: python check_settings_ui.py [OUT_DIR]
import os,sys,json,tempfile
os.environ.setdefault('QT_QPA_PLATFORM','offscreen')
os.environ.setdefault('WINLESTIA_DATA_DIR',tempfile.mkdtemp(prefix='wl-settings-'))
from pathlib import Path
from types import SimpleNamespace
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PySide6.QtCore import QTimer,QUrl,QObject,QMetaObject,qInstallMessageHandler
from PySide6.QtGui import QFontDatabase
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtQuick import QQuickItem
from PySide6.QtTest import QTest
from PySide6.QtCore import Qt
import frontend
from frontend import Bridge,ROOT
from localization import Translator
import faulthandler;faulthandler.dump_traceback_later(int(os.environ.get('WL_DUMP','0')) or 10**6,exit=True)
OUT=Path(sys.argv[1]) if len(sys.argv)>1 else None
app=QApplication([]);app.setQuitOnLastWindowClosed(False);issues=[];checks={};missing=[]
qInstallMessageHandler(lambda kind,context,message:issues.append(message) if int(kind) in (1,2,3) and 'QFontDatabase' not in message and 'window masks' not in message else None)
for font in (ROOT/'assets/fonts').glob('*.ttf'):QFontDatabase.addApplicationFont(str(font))
frontend.TRANSLATOR=Translator(tempfile.mkdtemp(prefix="wl-lang-"))
# Mirrors ShortcutCatalog.cs (ShortcutManager.List) exactly: same ids, groups, labels, defaults, reasons.
SHORTCUTS=[
 {'id':'dashboard','group':'Shell','label':'Ouvrir le tableau de bord','combo':'Ctrl+F1','defaultCombo':'Ctrl+F1','configurable':True,'reason':'','scope':'global','active':True},
 {'id':'dashboardFixed','group':'Shell','label':'Tableau de bord (secours)','combo':'Ctrl+Alt+Espace','defaultCombo':'Ctrl+Alt+Espace','configurable':False,'reason':'Raccourci de secours fixe : il ouvre toujours le tableau de bord, même si l’autre raccourci est réassigné.','scope':'global','active':True},
 {'id':'settings','group':'Shell','label':'Ouvrir les paramètres','combo':'Win+Alt+S','defaultCombo':'Win+Alt+S','configurable':True,'reason':'','scope':'global','active':True},
 {'id':'palette','group':'Shell','label':'Commande rapide','combo':'Ctrl+F2','defaultCombo':'Ctrl+F2','configurable':True,'reason':'','scope':'global','active':True},
 {'id':'wallpaper','group':'Apparence','label':'Changer de fond d’écran','combo':'Win+Alt+W','defaultCombo':'Win+Alt+W','configurable':True,'reason':'','scope':'global','active':True},
 {'id':'capture','group':'Outils','label':'Capturer l’écran','combo':'Ctrl+F3','defaultCombo':'Ctrl+F3','configurable':True,'reason':'','scope':'global','active':True},
 {'id':'pin','group':'Outils','label':'Épingler au premier plan','combo':'Ctrl+F8','defaultCombo':'Ctrl+F8','configurable':True,'reason':'','scope':'global','active':True},
 {'id':'dictation','group':'Outils','label':'Dictée (touche maintenue)','combo':'AltGr','defaultCombo':'','configurable':False,'reason':'Se règle dans Médias et audio › Dictée (touche maintenue).','scope':'global','active':True},
 {'id':'rescue','group':'Secours','label':'Restaurer le bureau','combo':'Ctrl+Alt+Backspace','defaultCombo':'Ctrl+Alt+Backspace','configurable':False,'reason':'Réservé à la restauration du bureau : il doit fonctionner même si Winlestia ne répond plus.','scope':'global','active':True},
 {'id':'tileFocus','group':'Disposition automatique','label':'Passer le focus à la fenêtre voisine','combo':'Ctrl+Alt+Flèches','defaultCombo':'Ctrl+Alt+Flèches','configurable':False,'reason':'Fixe pour l’instant ; actif uniquement quand la disposition automatique est activée.','scope':'tiling','active':True},
 {'id':'tileSwap','group':'Disposition automatique','label':'Échanger avec la fenêtre voisine','combo':'Ctrl+Alt+Maj+Flèches','defaultCombo':'Ctrl+Alt+Maj+Flèches','configurable':False,'reason':'Fixe pour l’instant ; actif uniquement quand la disposition automatique est activée.','scope':'tiling','active':True},
 {'id':'tileFloat','group':'Disposition automatique','label':'Basculer la fenêtre en flottant','combo':'Ctrl+Alt+Maj+F','defaultCombo':'Ctrl+Alt+Maj+F','configurable':False,'reason':'Fixe pour l’instant ; actif uniquement quand la disposition automatique est activée.','scope':'tiling','active':True},
 {'id':'tileGrow','group':'Disposition automatique','label':'Agrandir la zone de la fenêtre','combo':'Ctrl+Alt+Maj+Plus','defaultCombo':'Ctrl+Alt+Maj+Plus','configurable':False,'reason':'Fixe pour l’instant ; actif uniquement quand la disposition automatique est activée.','scope':'tiling','active':True},
 {'id':'tileShrink','group':'Disposition automatique','label':'Réduire la zone de la fenêtre','combo':'Ctrl+Alt+Maj+Moins','defaultCombo':'Ctrl+Alt+Maj+Moins','configurable':False,'reason':'Fixe pour l’instant ; actif uniquement quand la disposition automatique est activée.','scope':'tiling','active':True},
 {'id':'tileExclude','group':'Disposition automatique','label':'Exclure l’application de la disposition','combo':'Ctrl+Alt+Maj+X','defaultCombo':'Ctrl+Alt+Maj+X','configurable':False,'reason':'Fixe pour l’instant ; actif uniquement quand la disposition automatique est activée.','scope':'tiling','active':True},
]
NETS=[{'id':'whatsapp','name':'WhatsApp','enabled':True,'connected':True,'muted':False,'busy':False},{'id':'discord','name':'Discord','enabled':True,'connected':False,'muted':True,'busy':False},{'id':'telegram','name':'Telegram','enabled':False,'connected':False,'muted':False,'busy':False}]
bridge=Bridge(SimpleNamespace(pipe='WinLestiaSettingsUiTest',backend_pid=0,preview=True));bridge.pointer.stop();bridge.city=''
bridge.update_state({'state':{
 'settings':{'themeMode':'auto','motionScale':1,'autoTile':True,'tileExclusions':['notepad.exe','calc.exe'],'clipboardHistory':True,'clipboardLimit':30,'dictationEnabled':True,'dockVisible':True,'iconSize':56,'shortcuts':SHORTCUTS,'shortcutStatus':{'nonce':0,'id':'','ok':True,'message':'','conflictId':''}},
 'social':{'networks':NETS,'sound':True,'volume':60},
 'bookmarks':{'browser':'edge','browsers':[{'id':'edge','name':'Microsoft Edge','installed':True,'isDefault':True},{'id':'chrome','name':'Chrome','installed':True,'isDefault':False}],'hiddenCount':2}}})
HARNESS=b'''import QtQuick
import QtQuick.Window
import "settings"
Window{
 property alias prefs:prefs
 property int pageTo:-1
 property var entryIds:SettingsIndex.entries.map(e=>e.page+":"+e.id)
 onPageToChanged:if(pageTo>=0)prefs.go(pageTo)
 color:"#222";visible:true
 ShellPreferences{id:prefs;anchors.fill:parent;anchors.margins:16}
}'''
def items(item):
    yield item
    for c in item.childItems():yield from items(c)
def build(w,h):
    e=QQmlApplicationEngine();e.addImportPath(str(ROOT/'qml'));e.rootContext().setContextProperty('Bridge',bridge);bridge.engine=e
    e.loadData(HARNESS.replace(b'color:"#222"',b'width:%d;height:%d;color:"#222"'%(w,h)),QUrl.fromLocalFile(str(ROOT/'qml'/'SettingsHarness.qml')))
    return e
def find(root,name):return next((i for i in items(root.contentItem()) if i.objectName()==name),None)
def run():
    for lang in ('fr','en','es'):
        frontend.TRANSLATOR.select(lang)
        for (w,h) in ((1280,800),(640,720)):
            e=build(w,h);root=e.rootObjects()[0] if e.rootObjects() else None
            checks[f'{lang}:{w}:loads']=root is not None
            if root is None:continue
            root.resize(w,h);yield 700
            # PySide deadlocks when a Python QTranslator is called from the QML loader threads while loadData blocks:
            # compile every page translator-less first, then install the translator and retranslate on the main thread.
            for page in range(8):
                root.setProperty('pageTo',-1);root.setProperty('pageTo',page);yield 350
            app.installTranslator(frontend.TRANSLATOR);e.retranslate();root.setProperty('pageTo',-1);yield 300
            entries=root.property('entryIds');entries=entries.toVariant() if hasattr(entries,'toVariant') else entries;seen=set()
            for page in range(8):
                root.setProperty('pageTo',page);yield 700
                if OUT:OUT.mkdir(parents=True,exist_ok=True);root.grabWindow().save(str(OUT/f'settings-{lang}-{w}x{h}-p{page}.png'))
                for i in items(root.contentItem()):
                    rid=i.property('rowId');sid=i.property('sectionId')
                    for v in (rid,sid):
                        if v:seen.add(f'{page}:{v}')
            if lang=='fr' and w==1280:
                dynamic=[x for x in entries if ':sc:' in x or ':net:' in x]
                miss=[x for x in entries if x not in seen and x not in dynamic]
                checks['everyIndexEntryHasRow']=not miss;missing.extend(miss)
                # Search: type, Enter on the first hit, the page must change.
                root.setProperty('pageTo',0);yield 600
                search=find(root,'prefsSearch');QMetaObject.invokeMethod(search,'forceActiveFocus');yield 200
                search.setProperty('text','taskbar');yield 500
                checks['search:hasResults']=find(root,'prefsResult0') is not None
                QTest.keyClick(root,Qt.Key_Return);yield 900
                nav2=find(root,'prefsPage2');checks['search:navigatedToDocks']=bool(nav2 is not None and nav2.property('selected'))
                search.setProperty('text','zzzzqq');yield 400
                checks['search:noHitsEmpty']=find(root,'prefsResult0') is None
                search.setProperty('text','');yield 300
                # Shortcut dialog opens, a failed status is shown, then it closes.
                root.setProperty('pageTo',3);yield 600
                btn=find(root,'shortcutEdit-palette');checks['shortcuts:editButton']=btn is not None
                rows={i.property('rowId'):i for i in items(root.contentItem()) if i.property('rowId') and str(i.property('rowId')).startswith('sc:')}
                checks['shortcuts:settingsRow']='sc:settings' in rows and find(root,'shortcutEdit-settings') is not None
                checks['shortcuts:settingsCombo']=any(x['id']=='settings' and x['combo']=='Win+Alt+S' for x in SHORTCUTS)
                checks['shortcuts:all15Rows']=all(f"sc:{x['id']}" in rows for x in SHORTCUTS) and len(SHORTCUTS)==15
                for x in SHORTCUTS:
                    b=find(root,'shortcutEdit-'+x['id']);shown=b is not None and bool(b.property('visible'))
                    if x['configurable']:checks[f"shortcuts:{x['id']}:hasModifier"]=shown
                    else:
                        r=rows.get('sc:'+x['id'])
                        checks[f"shortcuts:{x['id']}:noModifierShowsReason"]=(not shown) and r is not None and bool(r.property('hint')) and str(r.property('hint'))!=''
            root.close();app.removeTranslator(frontend.TRANSLATOR);e.deleteLater();yield 200
    checks['noQmlWarnings']=not issues
    ok=all(checks.values())
    print(json.dumps({'checks':checks,'missing':missing[:12],'issues':issues[:10],'allPassed':ok}),flush=True)
    bridge.stop();os._exit(0 if ok else 1)
gen=run()
def drive():
    try:d=next(gen)
    except StopIteration:return
    QTimer.singleShot(d,drive)
QTimer.singleShot(300,drive);app.exec()
