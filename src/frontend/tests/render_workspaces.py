"""Offscreen render of the Dashboard Workspaces tab with a stub Bridge.
Usage: python render_workspaces.py OUT_DIR PREFIX  -> OUT_DIR/PREFIX-{red,teal,red-light}.png
Hard timeout 60 s. Does not touch the running app."""
import os, sys
os.environ.setdefault('QT_QPA_PLATFORM', 'offscreen')
from pathlib import Path
from PySide6.QtCore import QObject, Property, Signal, Slot, QUrl, QTimer
from PySide6.QtGui import QGuiApplication
from PySide6.QtQuick import QQuickView
from PySide6.QtQml import QQmlEngine

ROOT = Path(__file__).resolve().parents[1]
PAL = {
 'red-dark': dict(dark=True, p={'m3background':'#1a1110','m3surface':'#1a1110','m3onSurface':'#f1dedb','m3surfaceContainerLow':'#231918','m3surfaceContainer':'#271d1c','m3surfaceContainerHigh':'#322826','m3surfaceContainerHighest':'#3d3231','m3primary':'#ffb4a8','m3secondary':'#e7bdb6','m3primaryContainer':'#73342b','m3secondaryContainer':'#5d3f3b','m3onPrimary':'#561e16','m3onPrimaryContainer':'#ffdad4','m3onSecondaryContainer':'#ffdad4','m3onSurfaceVariant':'#d8c2be','m3outline':'#a08c89','m3outlineVariant':'#534341','m3tertiary':'#e6c26c','m3error':'#ffb4ab','m3onSecondary':'#43211d','m3onTertiary':'#3f2e00'}),
 'teal-dark': dict(dark=True, p={'m3background':'#0e1514','m3surface':'#0e1514','m3onSurface':'#dde4e2','m3surfaceContainerLow':'#161d1c','m3surfaceContainer':'#1a2120','m3surfaceContainerHigh':'#252b2b','m3surfaceContainerHighest':'#303636','m3primary':'#80d5cf','m3secondary':'#b0ccc9','m3primaryContainer':'#00504c','m3secondaryContainer':'#324b49','m3onPrimary':'#003734','m3onPrimaryContainer':'#9cf2eb','m3onSecondaryContainer':'#cce8e5','m3onSurfaceVariant':'#bec9c7','m3outline':'#889391','m3outlineVariant':'#3f4947','m3tertiary':'#b3c8ec','m3error':'#ffb4ab','m3onSecondary':'#1c3533','m3onTertiary':'#1e3252'}),
 'red-light': dict(dark=False, p={'m3background':'#fff8f6','m3surface':'#fff8f6','m3onSurface':'#231918','m3surfaceContainerLow':'#fff0ee','m3surfaceContainer':'#fceae7','m3surfaceContainerHigh':'#f6e4e1','m3surfaceContainerHighest':'#f1dedb','m3primary':'#904a41','m3secondary':'#77524d','m3primaryContainer':'#ffdad4','m3secondaryContainer':'#ffdad4','m3onPrimary':'#ffffff','m3onPrimaryContainer':'#3a0905','m3onSecondaryContainer':'#2c1512','m3onSurfaceVariant':'#534341','m3outline':'#857370','m3outlineVariant':'#d8c2be','m3tertiary':'#745b0c','m3error':'#ba1a1a','m3onSecondary':'#ffffff','m3onTertiary':'#ffffff'}),
}
APPS = [('discord','Discord','apps'),('claude','Claude','apps'),('chatgpt','ChatGPT','apps'),('steam','Steam','games'),('code','VS Code','projects'),('notes','Notes','documents'),('spotify','Spotify','apps'),('blender','Blender','projects')]
# Synthetic stand-in icons (no id 'notes' -> exercises the letter fallback). Generated once in the temp dir.
ICON_COLORS = {'discord':'#5865F2','claude':'#D97757','chatgpt':'#10A37F','steam':'#1B2838','code':'#0078D4','spotify':'#1DB954','blender':'#EA7600'}
def make_icons(folder):
    from PySide6.QtGui import QImage, QPainter, QColor, QFont
    from PySide6.QtCore import Qt, QRectF
    folder.mkdir(parents=True, exist_ok=True); out = {}
    for key, col in ICON_COLORS.items():
        img = QImage(96, 96, QImage.Format_ARGB32); img.fill(0)
        p = QPainter(img); p.setRenderHint(QPainter.Antialiasing)
        p.setBrush(QColor(col)); p.setPen(Qt.NoPen); p.drawRoundedRect(QRectF(4, 4, 88, 88), 22, 22)
        f = QFont('Rubik'); f.setPixelSize(52); f.setBold(True); p.setFont(f); p.setPen(QColor('white'))
        p.drawText(QRectF(0, 0, 96, 96), Qt.AlignCenter, key[0].upper()); p.end()
        path = folder / (key + '.png'); img.save(str(path)); out[key] = QUrl.fromLocalFile(str(path)).toString()
    return out
ICONS = {}

class Stub(QObject):
    changed = Signal()
    def __init__(self, pal, favs, recents):
        super().__init__(); self._pal = pal; self._favs = favs; self._rec = recents; self.log = []
    def _a(self): return [{'id':i,'name':n,'board':b} for i,n,b in APPS]*3
    dark = Property(bool, lambda s: s._pal['dark'], notify=changed)
    adaptivePalette = Property('QVariantMap', lambda s: s._pal['p'], notify=changed)
    apps = Property('QVariantList', _a, notify=changed)
    favorites = Property('QVariantList', lambda s: [{'id':i,'name':n,'board':b} for i,n,b in APPS][:s._favs], notify=changed)
    recent = Property('QVariantList', lambda s: [{'id':i,'name':n,'board':b} for i,n,b in APPS][:s._rec], notify=changed)
    iconRevision = Property(int, lambda s: 0, notify=changed)
    reducedEffects = Property(bool, lambda s: False, notify=changed)
    glassEnabled = Property(bool, lambda s: False, notify=changed)
    glassOpacity = Property(float, lambda s: 1.0, notify=changed)
    glassTintOpacity = Property(float, lambda s: 1.0, notify=changed)
    frameEnabled = Property(bool, lambda s: False, notify=changed)
    frameThickness = Property(int, lambda s: 0, notify=changed)
    themeMode = Property(str, lambda s: 'dark', notify=changed)
    assetRoot = Property(str, lambda s: ROOT.joinpath('assets').as_uri(), notify=changed)
    @Slot(str, 'QVariant', result=str)
    def iconUrl(self, i, r=0): return ICONS.get(i, '')
    @Slot(str, 'QVariantMap')
    def action(self, n, p=None): self.log.append(('action', n))
    @Slot(str)
    def navigate(self, t): self.log.append(('navigate', t))
    @Slot(str, result=str)
    def localUrl(self, p): return ''

def main():
    out = Path(sys.argv[1]); prefix = sys.argv[2]
    favs = int(os.environ.get('WS_FAVS', 5)); rec = int(os.environ.get('WS_REC', 4))
    app = QGuiApplication(sys.argv)
    from PySide6.QtGui import QFontDatabase
    for f in (ROOT/'assets/fonts').glob('*.ttf'): QFontDatabase.addApplicationFont(str(f))
    ICONS.update(make_icons(Path(os.environ.get('TEMP', '.')) / 'ws2-icons'))
    QTimer.singleShot(90000, lambda: os._exit(2))
    msgs = []
    from PySide6.QtCore import qInstallMessageHandler
    qInstallMessageHandler(lambda t, c, m: msgs.append(m))
    settle = int(os.environ.get('WS_SETTLE', 1600))
    for name, key, wait in (('red','red-dark',settle),('teal','teal-dark',settle),('red-light','red-light',settle),('anim','red-dark',int(os.environ.get('WS_ANIM_MS', 230)))):
        stub = Stub(PAL[key], favs, rec)
        eng = QQmlEngine(); eng.addImportPath(str(ROOT/'qml')); eng.rootContext().setContextProperty('Bridge', stub)
        view = QQuickView(eng, None)
        view.setSource(QUrl.fromLocalFile(str(Path(__file__).parent/'WorkspacesHarness.qml')))
        if view.status() != QQuickView.Ready:
            print('QML errors:', [e.toString() for e in view.errors()]); print(msgs); sys.exit(1)
        view.setResizeMode(QQuickView.SizeRootObjectToView)
        view.resize(980, 520); view.show()
        for _ in range(8): app.processEvents()
        QTimer.singleShot(wait, app.quit); app.exec()
        img = view.grabWindow(); img.save(str(out/f'{prefix}-{name}.png'))
        print(name, img.width(), img.height()); view.hide()
    bad = [m for m in msgs if 'qml' in m.lower() or 'warn' in m.lower() or 'Error' in m]
    print('MESSAGES:', len(msgs)); [print("  ", m[:300]) for m in msgs if "Colours.qml" not in m and "Config.qml" not in m and "Appearance" not in m]

if __name__ == "__main__": main()
