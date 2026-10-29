"""Interaction + frame-activity checks for the 'Tout ton espace' panel (stub Bridge, offscreen).
Usage: python check_espace.py OUT_DIR     (ESPACE_ICONS=dir for generated icons; ESPACE_REDUCED=1 for reduced effects)
Prints PASS/FAIL lines; saves a few PNGs. Never touches the running app."""
import os, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import render_espace as R
from PySide6.QtCore import Qt, QObject, QPoint, QTimer, QMetaObject, Q_ARG
from PySide6.QtGui import QGuiApplication
from PySide6.QtTest import QTest

OUT = Path(sys.argv[1]); RES = []
def check(name, ok, extra=''):
    RES.append(ok); print(('PASS ' if ok else 'FAIL ') + name + (' ' + str(extra) if extra else ''))

def main():
    app = QGuiApplication(sys.argv)
    for f in (R.ROOT / 'assets/fonts').glob('*.ttf'): R.QFontDatabase.addApplicationFont(str(f))
    R.ICONS = R.make_icons()
    R.qInstallMessageHandler(lambda t, c, m: R.MSGS.append(m))
    QTimer.singleShot(100000, lambda: os._exit(2))
    tag = 'reduced' if os.environ.get('ESPACE_REDUCED') == '1' else 'fx'
    view, stub, eng = R.make_view(app, 'red-dark', (1366, 768)); stub.h = 768; stub.changed.emit()
    root = view.rootObject(); grid = root.findChild(QObject, 'workspaceGrid'); query = root.findChild(QObject, 'workspaceQuery')
    frames = [0]; view.frameSwapped.connect(lambda: frames.__setitem__(0, frames[0] + 1))
    # Hidden: no rendering at all.
    R.pump(app, 300); f0 = frames[0]; R.pump(app, 1500)
    check('hidden: zero frames rendered in 1.5 s', frames[0] == f0, frames[0] - f0)
    check('model warm while hidden (372 apps, apps scope = 275)', root.findChild(QObject, 'workspaceBrowser').property('tileCount') == 275, root.findChild(QObject, 'workspaceBrowser').property('tileCount'))
    # Open, capture mid-animation frames.
    view.requestActivate(); R.pump(app, 100); fa = frames[0]
    root.setProperty('open', True); R.pump(app, 110); view.grabWindow().save(str(OUT / f'espace-anim110-{tag}.png'))
    R.pump(app, 1500); check('open animation renders frames', frames[0] - fa > 5, frames[0] - fa)
    f1 = frames[0]; R.pump(app, 1500)
    check('open+settled: zero frames in 1.5 s idle (no running animation/timer)', frames[0] == f1, frames[0] - f1)
    view.grabWindow().save(str(OUT / f'espace-open-{tag}.png'))
    # Hover a tile.
    QTest.mouseMove(view, QPoint(396, 272)); R.pump(app, 400); view.grabWindow().save(str(OUT / f'espace-hover-{tag}.png'))
    R.pump(app, 900); view.grabWindow().save(str(OUT / f'espace-tooltip-{tag}.png'))
    QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(396, 272)); R.pump(app, 100)
    check('click tile -> launch action', ('action', 'launch') in stub.log)
    QTest.mouseClick(view, Qt.RightButton, Qt.NoModifier, QPoint(565, 490)); R.pump(app, 500); view.grabWindow().save(str(OUT / f'espace-menu-{tag}.png'))
    check('right-click opens organise menu', bool(root.findChild(QObject, 'workspaceContext').property('opened')))
    menu = root.findChild(QObject, 'workspaceMenu'); texts = list(menu.property('itemTexts').toVariant())
    want = ['Ouvrir', 'Ouvrir l’emplacement', 'Exécuter en administrateur']
    check('menu items (Ouvrir, favoris, emplacement, admin)', all(w in texts for w in want) and any('favoris' in t for t in texts), texts)
    check('menu is visible with non-zero height', menu.property('height') > 100 and menu.property('opacity') > 0.9, menu.property('height'))
    mx, my = menu.property('x'), menu.property('y')
    stub.log.clear(); QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(int(mx + 60), int(my + 22))); R.pump(app, 300)
    check('menu Ouvrir launches and closes the menu', ('action', 'launch') in stub.log and not root.findChild(QObject, 'workspaceContext').property('opened'), stub.log)
    QTest.keyClick(view, Qt.Key_Escape); R.pump(app, 300)
    QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(700, 745)); R.pump(app, 300)  # dismiss popup if Esc did not
    # Regression: context menu must reopen every time (real mouse/wheel events only).
    ctx = root.findChild(QObject, 'workspaceContext'); mn = root.findChild(QObject, 'workspaceMenu')
    shown = lambda: bool(ctx.property('visible')) and bool(ctx.property('opened')) and mn.property('height') > 100 and mn.property('opacity') > 0.9
    rc = lambda x, y: (QTest.mouseClick(view, Qt.RightButton, Qt.NoModifier, QPoint(x, y)), R.pump(app, 350))
    A, Bp = (565, 490), (396, 272)
    rc(*A); check('menu repeat: right-click tile A -> visible', shown(), mn.property('height'))
    QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(1100, 300)); R.pump(app, 350)
    check('menu repeat: click outside -> closed', not ctx.property('opened') and not ctx.property('visible'), (ctx.property('opened'), ctx.property('visible'), mn.property('x'), mn.property('y'), mn.property('width')))
    rc(*Bp); check('menu repeat: right-click tile B -> visible', shown())
    x0 = mn.property('x'); mv = False
    for ty in (300, 330, 360, 400, 450, 490, 540):
        rc(903, ty)
        if shown() and mn.property('x') > x0 + 300: mv = True; break
    check('menu repeat: right-click another tile while open moves menu', mv, (x0, mn.property('x'), ty))
    rc(*A); check("menu repeat: right-click back on A", shown())
    stub.log.clear(); QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(int(mn.property('x') + 60), int(mn.property('y') + 22))); R.pump(app, 350)
    check('menu repeat: choose Ouvrir -> launch + closed', ('action', 'launch') in stub.log and not ctx.property('opened'), stub.log)
    rc(*A); check('menu repeat: right-click after action -> visible', shown(), mn.property('height'))
    gridv = root.findChild(QObject, 'workspaceGrid'); y0 = gridv.property('contentY')
    from PySide6.QtGui import QWheelEvent
    from PySide6.QtCore import QPointF, QPoint as QP
    def wheel(dy):
        pt = QPointF(1100, 400); ev = QWheelEvent(pt, view.mapToGlobal(pt), QP(0, 0), QP(0, dy), Qt.NoButton, Qt.NoModifier, Qt.NoScrollPhase, False)
        QGuiApplication.sendEvent(view, ev); R.pump(app, 400)
    wheel(-600)
    check('menu repeat: list scrolled by wheel', gridv.property('contentY') > y0, (y0, gridv.property('contentY')))
    check('menu repeat: scrolling closed the menu', not ctx.property('opened'))
    ok = False
    for ty in (300, 330, 360, 400, 450, 490, 540):
        rc(903, ty)
        if shown(): ok = True; break
    check('menu repeat: right-click recycled tile after scroll -> visible', ok, ty)
    QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(1100, 300)); R.pump(app, 300)
    wheel(600); rc(*Bp); QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(1100, 300)); R.pump(app, 300)
    root.setProperty('open', False); R.pump(app, 600)
    check('menu repeat: hiding panel closes menu', not ctx.property('opened'))
    root.setProperty('open', True); R.pump(app, 1500); rc(*A)
    check('menu repeat: right-click after hide+reopen -> visible', shown())
    QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(1100, 300)); R.pump(app, 300)
    # Keyboard
    QTest.keyClick(view, Qt.Key_F, Qt.ControlModifier); R.pump(app, 200)
    check('Ctrl+F focuses search', bool(query.property('activeFocus')))
    [QTest.keyClick(view, ch) for ch in 'blen']; R.pump(app, 400)
    check('typing filters (blen -> only Blender*)', 0 < root.findChild(QObject, 'workspaceBrowser').property('tileCount') < 30, root.findChild(QObject, 'workspaceBrowser').property('tileCount'))
    stub.log.clear(); QTest.keyClick(view, Qt.Key_Down); R.pump(app, 200); QTest.keyClick(view, Qt.Key_Return); R.pump(app, 100)
    check('Down then Enter launches current tile', ('action', 'launch') in stub.log)
    query.setProperty('text', ''); R.pump(app, 600)
    check('clearing search restores 275', root.findChild(QObject, 'workspaceBrowser').property('tileCount') == 275, root.findChild(QObject, 'workspaceBrowser').property('tileCount'))
    # Category chip + collapse toggle.
    QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(493, 186)); R.pump(app, 600)
    n = root.findChild(QObject, 'workspaceBrowser').property('tileCount'); check('chip "3D et conception" filters', 0 < n < 275, n)
    QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(1312, 184)); R.pump(app, 600); view.grabWindow().save(str(OUT / f'espace-chips-open-{tag}.png'))
    QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(1312, 184)); R.pump(app, 400)
    QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(355, 186)); R.pump(app, 600)  # Tout
    check('chip "Tout" restores 275', root.findChild(QObject, 'workspaceBrowser').property('tileCount') == 275, root.findChild(QObject, 'workspaceBrowser').property('tileCount'))
    # Scope switch via sidebar, mid-animation pill frame.
    QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(160, 231)); R.pump(app, 110); view.grabWindow().save(str(OUT / f'espace-pill-mid-{tag}.png')); R.pump(app, 900)
    check('sidebar Projets -> 41 items', root.findChild(QObject, 'workspaceBrowser').property('tileCount') == 41, root.findChild(QObject, 'workspaceBrowser').property('tileCount'))
    # List mode
    QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(1312, 130)); R.pump(app, 700); view.grabWindow().save(str(OUT / f'espace-list-{tag}.png'))
    QTest.mouseClick(view, Qt.LeftButton, Qt.NoModifier, QPoint(1312, 130)); R.pump(app, 500)
    # Esc: with text it must be consumed by the search field (clear), otherwise Main's shortcut closes.
    query.forceActiveFocus(); query.setProperty('text', 'bl'); R.pump(app, 300)
    QTest.keyClick(view, Qt.Key_Escape); R.pump(app, 300)
    check('Esc with text clears search (not close)', query.property('text') == '' and ('close',) not in stub.log, query.property('text'))
    # Empty state
    query.setProperty('text', 'zzzz'); R.pump(app, 700); check('no result -> 0 items', root.findChild(QObject, 'workspaceBrowser').property('tileCount') == 0); view.grabWindow().save(str(OUT / f'espace-empty-{tag}.png'))
    query.setProperty('text', ''); R.pump(app, 500)
    # Sections, dedupe, collapse, flat A-Z (scope "Tout afficher").
    b = root.findChild(QObject, 'workspaceBrowser')
    QMetaObject.invokeMethod(b, 'selectBoard', Q_ARG('QVariant', 'all')); R.pump(app, 700)
    order = list(b.property('secOrder').toVariant()); info = b.property('secInfo').toVariant()
    check('dedupe: 377 raw -> 372 unique entries', b.property('tileCount') == 372, b.property('tileCount'))
    cats = [k for k in order if k.startswith('c:')]
    rank = lambda k: 0 if k == 'fav' else 1 if k == 'rec' else 2 if k.startswith('c:') else 3 if k == 'folders' else 4
    check('sections ordered Favoris, Récents, categories, Dossiers, Outils Windows', [rank(k) for k in order] == sorted(rank(k) for k in order) and order[:2] == ['fav', 'rec'] and order[-1] == 'tools' and 'folders' in order, order[:3] + order[-2:])
    import unicodedata; fk = lambda k: unicodedata.normalize('NFD', k).encode('ascii', 'ignore').decode().lower()
    check('categories sorted A-Z', [fk(c) for c in cats] == sorted(fk(c) for c in cats), len(cats))
    flat = [e['name'] for e in b.property('flat').toVariant()]; pos = 0; ok = True
    for k in order:
        n = info[k]['count']
        if info[k]['closed']: continue
        part = flat[pos:pos + n]; pos += n
        if k not in ('rec',) and [p.lower() for p in part] != sorted(p.lower() for p in part): ok = False
    check('items A-Z inside each section', ok)
    check('Outils Windows collapsed by default', info['tools']['closed'] is True and info['tools']['count'] == 6, info['tools'])
    nflat = len(flat); QMetaObject.invokeMethod(b, 'toggleSection', Q_ARG('QVariant', 'tools')); R.pump(app, 300)
    check('expanding Outils Windows adds its tiles', len(b.property('flat').toVariant()) == nflat + 6 and b.property('secInfo').toVariant()['tools']['closed'] is False)
    QMetaObject.invokeMethod(b, 'toggleSection', Q_ARG('QVariant', 'fav')); R.pump(app, 300)
    check('collapsing Favoris removes its tile, state remembered', len(b.property('flat').toVariant()) == nflat + 6 - 1 and b.property('secInfo').toVariant()['fav']['closed'] is True)
    view.grabWindow().save(str(OUT / f'espace-sections-{tag}.png'))
    b.setProperty('flatMode', True); R.pump(app, 600); lo = list(b.property('secOrder').toVariant())
    check('flat A-Z: letter headers only', all(k.startswith('l:') for k in lo) and lo == sorted(lo), lo[:4]); view.grabWindow().save(str(OUT / f'espace-flat-{tag}.png'))
    b.setProperty('flatMode', False); R.pump(app, 400)
    # Catalog change while open: incremental row update, no crash.
    stub._apps = [dict(a, favorite=True) if a['id'] == 'e0' else a for a in stub._apps]; stub.catalogChanged.emit(); R.pump(app, 400)
    check('catalogChanged applied', root.findChild(QObject, 'workspaceBrowser').property('tileCount') > 0)
    # Close / reopen: state survives, no frames while hidden again.
    root.setProperty('open', False); R.pump(app, 600); f2 = frames[0]; R.pump(app, 1200)
    check('closed again: zero frames', frames[0] == f2, frames[0] - f2)
    bad = [m for m in R.MSGS if ('qml' in m.lower() or 'Error' in m) and 'of null' not in m]
    print('UNEXPECTED MESSAGES:', len(bad)); [print(' ', m[:240]) for m in bad[:10]]
    print('RESULT', sum(RES), '/', len(RES)); sys.stdout.flush(); os._exit(0)

main()
