# Python 3.11+ / winjet_render.py / Rendu hors écran des Winjets fournis (vrai WinjetCard + WinjetRenderer, flux simulés).
# usage: py -3.11 tools/winjet_render.py [OUTDIR] [--wallpaper CHEMIN]   (défaut : docs/media/winjets, fond noir et blanc procédural)
# Écrit <id>-<largeur>x<hauteur>.png par taille et all.png (planche contact). Le verre est émulé avec Pillow :
# fond d'écran net, copie floutée à travers le masque alpha de la carte, puis la carte (fond non opaque) par-dessus.
import os, sys, json, math, tempfile, random, time
from pathlib import Path
from types import SimpleNamespace

root = Path(__file__).resolve().parents[1]
front = Path(os.environ.get('WINLESTIA_FRONTEND', root / 'src/frontend'))
sys.path.insert(0, str(front))
os.environ['QT_QPA_PLATFORM'] = 'offscreen'
os.environ.setdefault('QT_QUICK_BACKEND', 'software')
os.environ.setdefault('WINLESTIA_DATA_DIR', tempfile.mkdtemp(prefix='wl-winjets-render-'))
import threading

_dog = threading.Timer(120, lambda: os._exit(3))
_dog.daemon = True
_dog.start()
from PySide6.QtCore import QUrl, QTimer, Qt, qInstallMessageHandler
from PySide6.QtGui import QFontDatabase, QColor
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtQuick import QQuickView
from PIL import Image, ImageDraw, ImageFilter, ImageFont

BUILTIN = root / 'src/winjets/builtin'


def swirl_wallpaper(w=1600, h=1000):
    """Fond d'écran noir et blanc sombre : dégradé diagonal + courbes douces (volutes), sans dépendance."""
    y, x = [a.astype('float32') for a in __import__('numpy').mgrid[0:h, 0:w]]
    np = __import__('numpy')
    u, v = x / w, y / h
    field = 0.10 + 0.12 * (1 - (u * 0.6 + v * 0.4))
    field = field + 0.55 * np.clip(np.sin(7.0 * (u + 0.35 * np.sin(5 * v + 2 * u)) + 4.0 * np.sin(3.2 * v + 1.3)) * 0.5 + 0.5, 0, 1) ** 6
    field = field + 0.22 * np.clip(np.sin(11.0 * (v - 0.3 * np.sin(4 * u)) + 6.0 * u) * 0.5 + 0.5, 0, 1) ** 8
    vig = 1 - 0.55 * ((u - 0.5) ** 2 + (v - 0.5) ** 2) * 2
    g = (np.clip(field * vig, 0, 1) * 255).astype('uint8')
    return Image.fromarray(g, 'L').filter(ImageFilter.GaussianBlur(3)).convert('RGB')


def load_wallpaper(path=None):
    return Image.open(path).convert('RGB') if path else swirl_wallpaper()


PALETTE = {'background': '#101820', 'text': '#e6edf3', 'primary': '#4cc2ff', 'secondary': '#8aa4b8', 'tertiary': '#9ab0e0'}
# Taille par défaut + au moins une autre par paquet (dans les bornes min/max de chaque widget.json).
SIZES = {
    'winlestia.calendrier': [(352, 352), (168, 168)],
    'winlestia.horloge': [(352, 168), (168, 168), (536, 352)],
    'winlestia.lecteur': [(352, 168), (168, 168), (520, 340)],
    'winlestia.systeme': [(352, 352), (168, 168), (536, 536)],
    'winlestia.marches': [(352, 352), (168, 168), (536, 536)],
    'winlestia.stockage': [(352, 168), (168, 168), (536, 536)],
    'winlestia.fichiers': [(352, 352), (168, 168), (536, 536)],
    'winlestia.notes': [(352, 352), (168, 168)],
    'winlestia.agenda': [(352, 352), (168, 168), (536, 536)],
    'winlestia.agenda#vide': [(352, 352)],
    'winlestia.meteo': [(352, 168), (168, 168), (536, 352)],
}
HARNESS = """
import QtQuick
import "%(winjets)s"
Item {
    required property string iid
    required property int w
    required property int h
    width: w
    height: h
    WinjetCard {
        anchors.fill: parent
        inst: Bridge.winjets.instance(parent.iid)
    }
}
"""
DISKS = [
    {'drive': 'C:', 'label': 'Windows', 'totalGb': 953, 'usedGb': 412, 'freeGb': 541, 'percent': 43},
    {'drive': 'D:', 'label': 'Données', 'totalGb': 1862, 'usedGb': 1490, 'freeGb': 372, 'percent': 80},
    {'drive': 'E:', 'label': 'Sauvegarde', 'totalGb': 465, 'usedGb': 120, 'freeGb': 345, 'percent': 26},
]
FORECAST = {
    'current': {'weather_code': 2, 'temperature_2m': 18.4, 'apparent_temperature': 17.2},
    'daily': {
        'time': ['2026-11-18', '2026-11-19', '2026-11-20', '2026-11-21', '2026-11-22', '2026-11-23', '2026-11-24'],
        'weather_code': [2, 61, 3, 0, 1, 95, 71],
        'temperature_2m_max': [21, 17, 16, 19, 22, 15, 6],
        'temperature_2m_min': [11, 10, 9, 8, 12, 9, 0],
    },
}
def _soon(hours):
    """(début ISO, libellé) d'un événement du jour à `hours` h d'ici (borné à 23 h 30) : le temps relatif reste visible."""
    from datetime import datetime, timedelta
    now = datetime.now()
    limit = now.replace(hour=23, minute=30, second=0, microsecond=0)
    at = min(now.replace(second=0, microsecond=0) + timedelta(hours=hours, minutes=5), limit)
    end = at + timedelta(minutes=30)
    return at.strftime('%Y-%m-%dT%H:%M:00'), at.strftime('%H:%M') + '–' + end.strftime('%H:%M')


EVENTS = [
    {'title': 'Réunion d’équipe', 'location': 'Salle B', 'timeLabel': _soon(1)[1], 'dayLabel': 'Aujourd’hui',
        'start': _soon(1)[0], 'allDay': False},
    {'title': 'Déjeuner avec Paul', 'location': 'Le Comptoir', 'timeLabel': _soon(3)[1], 'dayLabel': 'Aujourd’hui',
        'start': _soon(3)[0], 'allDay': False},
    {'title': 'Dentiste', 'location': '', 'timeLabel': '14:00–15:00', 'dayLabel': 'Demain', 'start': '2026-11-19T14:00:00',
        'allDay': False},
    {'title': 'Appel client', 'location': 'Teams', 'timeLabel': '17:00–17:30', 'dayLabel': 'Demain', 'start': '2026-11-19T17:00:00',
        'allDay': False},
    {'title': 'Anniversaire de Marie', 'location': '', 'timeLabel': 'Toute la journée', 'dayLabel': 'vendredi',
        'start': '2026-11-20T00:00:00', 'allDay': True},
    {'title': 'Cinéma', 'location': 'UGC', 'timeLabel': '20:30–22:30', 'dayLabel': 'vendredi', 'start': '2026-11-20T20:30:00',
        'allDay': False},
]
STOCKS = {
    'AAPL': ('Apple Inc.', 231.4, 1.24, 220),
    'MSFT': ('Microsoft Corporation', 418.1, -0.41, 420),
    '^GSPC': ('S&P 500', 5820.3, 0.67, 5700),
}
CPU = [18, 22, 31, 27, 44, 38, 29, 35, 52, 41, 33, 27]


def art_png(path):
    """Pochette simulée : dégradé diagonal saturé (magenta vers orange) et disque clair."""
    img = Image.new('RGB', (256, 256))
    px = img.load()
    for y in range(256):
        for x in range(256):
            t = (x + y) / 510
            px[x, y] = (int(200 + 55 * t), int(40 + 130 * t), int(150 - 100 * t))
    d = ImageDraw.Draw(img)
    d.ellipse((70, 70, 186, 186), outline=(255, 240, 230), width=10)
    img.save(path)


def stock_data():
    symbols = {}
    for sym, (name, price, change, base) in STOCKS.items():
        rng = random.Random(sym)
        hist, v = [], float(base)
        for i in range(24):
            v += (price - v) / max(1, 24 - i) + rng.gauss(0, base * 0.006)
            hist.append(v)
        hist[-1] = price
        symbols[sym] = {'name': name, 'price': price, 'change24h': change, 'history': hist}
    return {'market': {'value': {'live': True, 'symbols': symbols}, 'updated': '2026-11-18T09:00:00Z'}}


def agenda_data():
    return {'agenda': {'value': {'status': 'ok', 'count': len(EVENTS), 'events': EVENTS}, 'updated': '2026-11-18T09:00:00Z'}}


def main():
    argv = sys.argv[1:]
    wall_path = None
    if '--wallpaper' in argv:
        i = argv.index('--wallpaper')
        wall_path = argv[i + 1]
        del argv[i:i + 2]
    out = Path(argv[0]) if argv else root / 'docs/media/winjets'
    out.mkdir(parents=True, exist_ok=True)
    app = QApplication([])
    import app_state
    from localization import Translator
    from frontend import Bridge, ROOT

    app_state.TRANSLATOR = Translator(tempfile.mkdtemp(prefix='wl-lang-'))
    from winjet_feeds import WinjetFeeds, weather_payload

    for font in (ROOT / 'assets/fonts').glob('*.ttf'):
        QFontDatabase.addApplicationFont(str(font))
    issues = []
    qInstallMessageHandler(lambda kind, context, message: issues.append(message))

    # Flux simulés : aucun disque, dossier, réseau ni géolocalisation réels.
    def fake_disk(self):
        self._disk = [dict(d) for d in DISKS]
        self._bump()

    def fake_files(self, force=False):
        now = time.time()
        self._recent = [{'name': n, 'path': 'C:\\Users\\demo\\' + n, 'kind': 'file', 'icon': ic, 'modified': now - age}
                        for n, ic, age in (('Rapport T3.docx', 'description', 600), ('Budget 2027.xlsx', 'table_chart', 7200),
                            ('Photo vacances.jpg', 'image', 86400),
                                           ('Présentation.pptx', 'description', 172800), ('Notes réunion.txt', 'description', 259200),
                                           ('Facture.pdf', 'picture_as_pdf', 604800), ('Plan.png', 'image', 700000), ('Archive.zip',
                                               'folder_zip', 900000))]
        self._folders = [{'name': n, 'path': 'C:\\Users\\demo\\' + n, 'icon': ic}
                         for n, ic in (('Bureau', 'desktop_windows'), ('Téléchargements', 'download'), ('Documents', 'description'),
                             ('Images', 'image'))]
        self._allowed = {e['path'] for e in self._recent} | {f['path'] for f in self._folders}
        self._bump()

    def fake_weather(self, key, force=False):
        self._set_weather(key, weather_payload(FORECAST, key or 'Paris', 'city', 'fr'))

    WinjetFeeds._poll_disk = fake_disk
    WinjetFeeds._refresh_files = fake_files
    WinjetFeeds._start_weather = fake_weather

    bridge = Bridge(SimpleNamespace(pipe='PanneauxWinjetRender', backend_pid=0, preview=False))
    bridge.pointer.stop()
    bridge.update_state({'state': {
        'settings': {'themeMode': 'custom', 'customPalette': PALETTE, 'reducedEffects': False, 'customGlass': True,
            'customGlassOpacity': 0.12},
        'dark': True,
        'performance': {'cpu': CPU[-1], 'memPercent': 0.58, 'batteryPerc': 80, 'batteryCharging': False},
        'sysInfo': {'uptime': '3 j 4 h', 'userName': 'demo', 'osName': 'Windows 11'},
    }})
    art = Path(tempfile.mkdtemp()) / 'art.png'
    art_png(art)
    PLAYER = {
        'available': True, 'playing': True, 'title': 'Midnight City', 'artist': 'M83', 'album': 'Hurry Up, We’re Dreaming',
        'art': art.as_uri(), 'position': 84, 'duration': 243, 'canNext': True, 'canPrevious': True, 'canToggle': True,
        'canSeek': True, 'app': 'Spotify',
    }

    def apply_player():
        # update_state() vide le lecteur : on réinjecte la piste simulée après chaque mise à jour.
        bridge._player.data = dict(PLAYER)
        bridge._player.stamp = time.monotonic()
    apply_player()
    bridge._player.changed.emit()

    ids = list(SIZES)
    packages, instances, data = [], [], {}
    for key in ids:
        pid = key.split('#')[0]
        folder = BUILTIN / pid
        manifest = json.loads((folder / 'manifest.json').read_text('utf-8'))
        packages.append({
            'id': pid, 'version': manifest.get('version', ''), 'name': manifest['name'], 'author': manifest.get('author', ''),
            'description': manifest.get('description', ''), 'builtin': True, 'dir': str(folder), 'preview': str(folder / 'preview.png'),
            'network': (manifest.get('permissions') or {}).get('network') or [],
            'definition': json.loads((folder / 'widget.json').read_text('utf-8')), 'manifest': manifest,
        })
        state = {'text': 'Courses du week-end\n- pain, lait, café\n- appeler Marie\n- réserver le train'} if pid.endswith('notes') else {}
        instances.append({'id': 'i-' + key, 'packageId': pid, 'hidden': False, 'settings': {}, 'state': state})
    data['i-winlestia.marches'] = stock_data()
    data['i-winlestia.agenda'] = agenda_data()
    data['i-winlestia.agenda#vide'] = {'agenda': {'value': {'status': 'unconfigured', 'count': 0, 'events': []}}}
    niveaux = {'niveaux': {'value': json.loads((BUILTIN / 'winlestia.systeme/assets/niveaux.json').read_text('utf-8'))}}
    data['i-winlestia.systeme'] = niveaux
    data['i-winlestia.stockage'] = niveaux
    bridge._state['winjets'] = {'editMode': False, 'packages': packages, 'instances': instances, 'data': data, 'lastImport': {}}
    bridge.winjets.refresh_state()

    engine = QQmlApplicationEngine()
    engine.addImportPath(str(ROOT / 'qml'))
    engine.rootContext().setContextProperty('Bridge', bridge)
    jobs = []
    tmpdir = Path(tempfile.mkdtemp()).resolve()
    qml = tmpdir / 'Harness.qml'
    qml.write_text(HARNESS % {'winjets': (ROOT / 'qml/winjets').as_uri()}, encoding='utf-8')
    for pid in ids:
        for w, h in SIZES[pid]:
            view = QQuickView(engine, None)
            view.setColor(QColor(0, 0, 0, 0))
            view.setFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint)
            view.setResizeMode(QQuickView.SizeViewToRootObject)
            view.setInitialProperties({'iid': 'i-' + pid, 'w': w, 'h': h})
            view.setSource(QUrl.fromLocalFile(str(qml)))
            view.show()
            jobs.append((pid, w, h, view))

    wall = load_wallpaper(wall_path)

    def glass(card, key):
        """Fond d'écran net, copie floutée sous la carte (masque = alpha de la carte), carte par-dessus."""
        w, h = card.size
        pad = 24
        scale = max((w + 2 * pad) / wall.width, (h + 2 * pad) / wall.height)
        bg = wall.resize((int(wall.width * scale) + 1, int(wall.height * scale) + 1), Image.LANCZOS)
        ox = (hash(key) % 997) % max(1, bg.width - w - 2 * pad)
        oy = (hash(key) % 463) % max(1, bg.height - h - 2 * pad)
        bg = bg.crop((ox, oy, ox + w + 2 * pad, oy + h + 2 * pad)).convert('RGBA')
        blurred = bg.filter(ImageFilter.GaussianBlur(40))  # DWM : flou fort ; la teinte fumée vient du fond de la carte (alpha ~0.58)
        mask = Image.new('L', bg.size, 0)
        mask.paste(card.getchannel('A').point(lambda a: 255 if a > 0 else 0), (pad, pad))
        base = Image.composite(blurred, bg, mask)
        layer = Image.new('RGBA', bg.size, (0, 0, 0, 0))
        layer.paste(card, (pad, pad))
        return Image.alpha_composite(base, layer)

    def finish():
        if os.environ.get('WINJET_DEBUG'):
            print('DATA', bridge.winjets.instance('i-winlestia.agenda').get('data'), file=sys.stderr)
            def walk(it, depth=0):
                if it.property('hasBar') is not None and it.property('limit') is not None:
                    print('LIST', it.property('shown'), it.property('height'),
                        (lambda v: len(v.toVariant()) if hasattr(v, 'toVariant') else v)(it.property('items')), file=sys.stderr)
                for c in it.childItems():
                    walk(c, depth + 1)
            for pid, w, h, view in jobs:
                print(pid, w, h, file=sys.stderr)
                walk(view.rootObject())
        shots = []
        for pid, w, h, view in jobs:
            img = view.grabWindow()
            tmp = tmpdir / 'g.png'
            img.save(str(tmp))
            card = Image.open(tmp).convert('RGBA')
            shot = glass(card, pid + str(w))
            path = out / ('%s-%dx%d.png' % (pid.replace('#', '-'), w, h))
            shot.convert('RGB').save(path)
            shots.append((pid, w, h, shot, path))
        # Planche contact : une ligne par paquet, tailles côte à côte.
        try:
            label = ImageFont.truetype('segoeui.ttf', 16)
        except OSError:
            label = ImageFont.load_default()
        gap, rows = 20, []
        for pid in ids:
            items = [s[3] for s in shots if s[0] == pid]
            rows.append((pid, items, max(i.height for i in items), sum(i.width for i in items) + gap * (len(items) - 1)))
        width = max(r[3] for r in rows) + 2 * gap
        height = sum(r[2] + 26 + gap for r in rows) + gap
        sheet = Image.new('RGB', (width, height), (16, 16, 16))
        d = ImageDraw.Draw(sheet)
        y = gap
        for pid, items, rh, _ in rows:
            d.text((gap, y), pid, font=label, fill=(200, 210, 220))
            x = gap
            for it in items:
                sheet.paste(it.convert('RGB'), (x, y + 26))
                x += it.width + gap
            y += rh + 26 + gap
        sheet.save(out / 'all.png')
        errors = [x for x in issues if 'plugin does not support' not in x and 'QFontDatabase' not in x and 'OpenThemeData' not in x]
        print(json.dumps({'saved': [str(s[4]) for s in shots] + [str(out / 'all.png')], 'qmlIssues': errors}, ensure_ascii=False))
        bridge.stop()
        app.quit()

    # Historique CPU : échantillons injectés l'un après l'autre pour alimenter system.cpuHistory.
    samples = iter(CPU)

    def feed():
        try:
            value = next(samples)
        except StopIteration:
            return
        bridge.update_state({'state': {'performance': {'cpu': value, 'memPercent': 0.58},
            'sysInfo': {'uptime': '3 j 4 h', 'userName': 'demo', 'osName': 'Windows 11'}}})
        QTimer.singleShot(60, feed)

    QTimer.singleShot(2800, lambda: (apply_player(), bridge._player.changed.emit()))
    QTimer.singleShot(1500, feed)
    QTimer.singleShot(4800, lambda: (apply_player(), bridge._player.changed.emit()))
    QTimer.singleShot(5400, finish)
    app.exec()


main()
sys.stdout.flush()
os._exit(0)
