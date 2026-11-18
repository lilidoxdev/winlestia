"""Winjets côté Qt : géométrie (écrans, placement, aimantage), expressions pour le QML et actions du bridge.

Les fonctions de géométrie sont pures (testées sans Qt). `WinjetsBridge` expose au QML, sous `Bridge.winjets`, l'état publié
par le backend (`state.winjets`, voir docs/WINJETS-DESIGN.md §4) et les actions `winjets.*`. Rien n'est évalué depuis un paquet :
les expressions passent par winjet_expr (grammaire fermée).
"""
from __future__ import annotations

import re
from datetime import datetime
from urllib.parse import urlparse

from PySide6.QtCore import Property, QObject, QPointF, QTimer, QUrl, Signal, Slot
from PySide6.QtGui import QCursor, QGuiApplication

import winjet_expr
from winjet_feeds import WinjetFeeds

GRID = 8  # pas d'aimantage
CELL = 168  # côté d'une cellule : 4 × 2 cellules = 720 × 352 px
GUTTER = 16
PITCH = CELL + GUTTER  # pas de la grille d'édition
MARGIN = 16  # marge des bords de la zone de travail
THRESHOLD = 8
MIN_SIDE = 120  # minSize imposé par le moteur
MAX_W, MAX_H = 1600, 1000
HANDLES = ('n', 's', 'e', 'w', 'ne', 'nw', 'se', 'sw')


# --- Écrans et géométrie -------------------------------------------------------------------------------------------


def screen_key(name, width, height):
    """Clé stable d'un écran : nom Windows + taille physique (`\\\\.\\DISPLAY1|2560x1440`)."""
    return f'{name}|{int(width)}x{int(height)}'


def parse_key(key):
    name, _, size = str(key or '').rpartition('|')
    match = re.fullmatch(r'(\d+)x(\d+)', size)
    if not name or not match:
        return str(key or ''), None
    return name, (int(match.group(1)), int(match.group(2)))


def opacity_of(value):
    """Opacité de fond d'une instance : entier 0..100, ou None pour suivre le verre global."""
    if isinstance(value, bool) or not isinstance(value, (int, float)) or value != value or value in (float('inf'), float('-inf')):
        return None
    return int(max(0, min(100, round(value))))


def clamp_size(width, height, minimum=(MIN_SIDE, MIN_SIDE), maximum=(MAX_W, MAX_H)):
    low_w, low_h = max(MIN_SIDE, int(minimum[0])), max(MIN_SIDE, int(minimum[1]))
    high_w, high_h = min(MAX_W, int(maximum[0])), min(MAX_H, int(maximum[1]))
    return (
        int(max(low_w, min(max(low_w, high_w), round(width)))),
        int(max(low_h, min(max(low_h, high_h), round(height)))),
    )


def clamp_rect(x, y, width, height, work_w, work_h):
    """Garde le rectangle dans la zone de travail (coordonnées relatives) ; réduit la taille si elle ne tient pas."""
    width = int(min(width, max(1, work_w)))
    height = int(min(height, max(1, work_h)))
    return int(max(0, min(round(x), work_w - width))), int(max(0, min(round(y), work_h - height))), width, height


def normalize(x, y, work_w, work_h):
    return round(x / max(1, work_w), 6), round(y / max(1, work_h), 6)


def reconcile_geometry(geometry, screens):
    """Place une géométrie stockée sur les écrans actuels.

    `screens` : dicts {key, name, w, h (zone de travail, px logiques), primary}. Renvoie (écran, géométrie relative
    à la zone de travail avec clé, nx, ny et drapeau `recovered`) ou None si l'instance n'a pas encore de place
    (il faut alors `first_free_slot`). Même clé -> simple clamp ; même nom, autre taille -> repositionnement proportionnel
    (nx, ny) puis clamp ; écran absent -> écran principal, `recovered` = vrai.
    """
    if not screens or not isinstance(geometry, dict):
        return None
    width, height = geometry.get('w') or 0, geometry.get('h') or 0
    if not geometry.get('screen') or width <= 0 or height <= 0:
        return None
    name, _ = parse_key(geometry['screen'])
    exact = next((s for s in screens if s['key'] == geometry['screen']), None)
    same_name = next((s for s in screens if s['name'] == name), None)
    recovered = bool(geometry.get('recovered')) and exact is not None
    if exact:
        target, x, y = exact, geometry.get('x', 0), geometry.get('y', 0)
    else:
        target = same_name or next((s for s in screens if s.get('primary')), screens[0])
        recovered = same_name is None
        nx = geometry.get('nx')
        ny = geometry.get('ny')
        if nx is None or ny is None:
            nx, ny = normalize(geometry.get('x', 0), geometry.get('y', 0), target['w'], target['h'])
        x, y = nx * target['w'], ny * target['h']
    x, y, width, height = clamp_rect(x, y, width, height, target['w'], target['h'])
    nx, ny = normalize(x, y, target['w'], target['h'])
    return target, {
        'screen': target['key'], 'x': x, 'y': y, 'w': width, 'h': height, 'nx': nx, 'ny': ny, 'recovered': recovered
    }


def _overlaps(a, b, gap):
    return a[0] < b[0] + b[2] + gap and b[0] < a[0] + a[2] + gap and a[1] < b[1] + b[3] + gap and b[1] < a[1] + a[3] + gap


def first_free_slot(work_w, work_h, width, height, occupied, margin=MARGIN, gutter=GUTTER, step=GRID):
    """Première place libre (lignes de gauche à droite, de haut en bas), gouttière respectée. Repli : cascade."""
    width, height = min(int(width), work_w), min(int(height), work_h)
    y = margin
    while y + height <= work_h - margin + 0:
        x = margin
        while x + width <= work_w - margin + 0:
            if not any(_overlaps((x, y, width, height), rect, gutter) for rect in occupied):
                return x, y
            x += step
        y += step
    offset = margin + 24 * (len(occupied) % 8)
    x, y, _, _ = clamp_rect(offset, offset, width, height, work_w, work_h)
    return x, y


# --- Aimantage -----------------------------------------------------------------------------------------------------


def _targets(others, work, axis):
    """Lignes d'alignement sur un axe : [(position, début, fin)] ; début/fin = étendue perpendiculaire (None = zone)."""
    out = []
    index, size = (0, 2) if axis == 'x' else (1, 3)
    other_index = 1 - index
    length = work[index]
    out += [(MARGIN, None, None), (length - MARGIN, None, None), (length / 2, None, None)]
    for rect in others:
        lo, hi = rect[other_index], rect[other_index] + rect[other_index + 2]
        out += [(rect[index], lo, hi), (rect[index] + rect[size] / 2, lo, hi), (rect[index] + rect[size], lo, hi)]
    return out


def grid_line(anchor, end=False):
    """Ligne de cellule la plus proche : débuts à MARGIN + k·PITCH, fins une gouttière avant la cellule suivante."""
    base = MARGIN - (GUTTER if end else 0)
    return base + round((anchor - base) / PITCH) * PITCH


def _best(anchors, targets, grid_anchors=(), threshold=THRESHOLD):
    """Meilleur décalage (anchor -> target). Retourne (delta, target) ; la grille (cellules puis pas de 8) ne sert que si
    rien d'autre n'est à portée. grid_anchors = [(position, est_une_fin)] ; le premier est aussi aimanté sur le pas de 8."""
    best = None
    for anchor in anchors:
        for target in targets:
            delta = target[0] - anchor
            if abs(delta) <= threshold and (best is None or abs(delta) < abs(best[0])):
                best = (delta, target)
    if best is not None:
        return best
    cells = [grid_line(a, end) - a for a, end in grid_anchors]
    cells = [d for d in cells if abs(d) <= threshold]
    if cells:
        return min(cells, key=abs), None
    if grid_anchors:
        delta = round(grid_anchors[0][0] / GRID) * GRID - grid_anchors[0][0]
        if abs(delta) <= threshold:
            return delta, None
    return 0, None


def _guide(axis, target, rect):
    """Repère affiché : ligne `axis` à `target[0]`, étendue réunissant la carte et la carte cible."""
    other = (rect[1], rect[1] + rect[3]) if axis == 'x' else (rect[0], rect[0] + rect[2])
    start, end = other
    if target[1] is not None:
        start, end = min(start, target[1]), max(end, target[2])
    return {'axis': axis, 'pos': round(target[0], 2), 'from': round(start, 2), 'to': round(end, 2)}


def snap_move(rect, others, work, threshold=THRESHOLD, snap=True):
    """Déplacement : aimante la carte (bords/centre) sur les autres cartes, les bords de zone (marge 16) puis la grille 8.

    rect = (x, y, w, h) relatif à la zone de travail ; work = (W, H). Renvoie (x, y, repères).
    """
    x, y, w, h = rect
    if not snap:  # Alt : position libre
        return round(x, 2), round(y, 2), []
    guides = []
    delta_x, target_x = _best([x, x + w / 2, x + w], _targets(others, work, 'x'), [(x, False), (x + w, True)], threshold)
    delta_y, target_y = _best([y, y + h / 2, y + h], _targets(others, work, 'y'), [(y, False), (y + h, True)], threshold)
    x, y = x + delta_x, y + delta_y
    if target_x:
        guides.append(_guide('x', target_x, (x, y, w, h)))
    if target_y:
        guides.append(_guide('y', target_y, (x, y, w, h)))
    return round(x, 2), round(y, 2), guides


def snap_resize(rect, handle, others, work, minimum=(MIN_SIDE, MIN_SIDE), maximum=(MAX_W, MAX_H), threshold=THRESHOLD, snap=True):
    """Redimensionnement par une poignée (n s e w ne nw se sw) : seuls les bords déplacés s'aimantent.

    Renvoie (x, y, w, h, repères) en respectant min/max et la zone de travail.
    """
    x, y, w, h = rect
    left, top, right, bottom = x, y, x + w, y + h
    min_w, min_h = max(MIN_SIDE, minimum[0]), max(MIN_SIDE, minimum[1])
    max_w, max_h = min(MAX_W, maximum[0]), min(MAX_H, maximum[1])
    guides = []
    if not snap:  # Alt : seules les bornes min/max et la zone de travail s'appliquent
        threshold = -1
    if 'e' in handle:
        delta, target = _best([right], _targets(others, work, 'x'), [(right, True)], threshold)
        right = min(left + max_w, max(left + min_w, min(right + delta, work[0])))
        if target and right == target[0]:
            guides.append(_guide('x', target, (left, top, right - left, bottom - top)))
    if 'w' in handle:
        delta, target = _best([left], _targets(others, work, 'x'), [(left, False)], threshold)
        left = max(right - max_w, min(right - min_w, max(left + delta, 0)))
        if target and left == target[0]:
            guides.append(_guide('x', target, (left, top, right - left, bottom - top)))
    if 's' in handle:
        delta, target = _best([bottom], _targets(others, work, 'y'), [(bottom, True)], threshold)
        bottom = min(top + max_h, max(top + min_h, min(bottom + delta, work[1])))
        if target and bottom == target[0]:
            guides.append(_guide('y', target, (left, top, right - left, bottom - top)))
    if 'n' in handle:
        delta, target = _best([top], _targets(others, work, 'y'), [(top, False)], threshold)
        top = max(bottom - max_h, min(bottom - min_h, max(top + delta, 0)))
        if target and top == target[0]:
            guides.append(_guide('y', target, (left, top, right - left, bottom - top)))
    return round(left, 2), round(top, 2), round(right - left, 2), round(bottom - top, 2), guides


# --- Mises en page et contexte -------------------------------------------------------------------------------------


def select_layout(layouts, width, height):
    """Dernière mise en page dont minWidth/minHeight sont satisfaits par la taille réelle ; 0 par défaut."""
    chosen = 0
    for index, layout in enumerate(layouts or []):
        if not isinstance(layout, dict):
            continue
        if width >= (layout.get('minWidth') or 0) and height >= (layout.get('minHeight') or 0):
            chosen = index
    return chosen


def flatten_data(entries):
    """`data.<source>.*` : le contenu de `value` (fusionné avec updated/error/stale quand c'est un objet)."""
    out = {}
    for source, entry in (entries or {}).items():
        if not isinstance(entry, dict):
            continue
        value = entry.get('value')
        meta = {k: entry.get(k) for k in ('updated', 'error', 'stale')}
        out[source] = {**meta, **value} if isinstance(value, dict) else value if value is not None else meta
    return out


def default_settings(definition, stored):
    """Réglages déclarés, valeurs par défaut comblées par les valeurs stockées."""
    values = {}
    for item in (definition or {}).get('settings') or []:
        if isinstance(item, dict) and item.get('id'):
            values[item['id']] = item.get('default')
    values.update(stored or {})
    return values


RETIRED_PREFIX = 'io.github.walidmpcwidgets.original.'  # coquilles de l'ancien hôte PC Widgets (retiré), jamais supprimées sur disque


def drop_retired(raw):
    """Retire de l'état publié les paquets et instances de l'hôte PC Widgets retiré (données utilisateur intactes)."""
    if not isinstance(raw, dict):
        return raw
    packages = raw.get('packages') or []
    instances = raw.get('instances') or []
    gone = {p.get('id') for p in packages if str(p.get('id') or '').startswith(RETIRED_PREFIX)}
    gone |= {i.get('packageId') for i in instances if str(i.get('packageId') or '').startswith(RETIRED_PREFIX)}
    if not gone:
        return raw
    dead = {i.get('id') for i in instances if i.get('packageId') in gone}
    out = {**raw, 'packages': [p for p in packages if p.get('id') not in gone],
           'instances': [i for i in instances if i.get('packageId') not in gone]}
    if isinstance(raw.get('data'), dict):
        out['data'] = {k: v for k, v in raw['data'].items() if k not in dead}
    return out


def diff_snapshots(old, new):
    """Identifiants dont l'empreinte (instance, paquet, données, géométrie) est ajoutée, retirée ou modifiée."""
    return sorted(i for i in set(old) | set(new) if old.get(i) != new.get(i))


def host_allowed(url, network, homepage):
    """openUrl : HTTPS seulement, hôte déclaré dans permissions.network ou égal à celui de homepage."""
    parsed = urlparse(str(url or ''))
    if parsed.scheme != 'https' or not parsed.hostname:
        return False
    allowed = {h.lower() for h in network or []}
    home = urlparse(str(homepage or '')).hostname
    if home:
        allowed.add(home.lower())
    return parsed.hostname.lower() in allowed


# --- Pont QML ------------------------------------------------------------------------------------------------------


class WinjetsBridge(QObject):
    changed = Signal()  # tout changement d'état (la révision augmente)
    instanceChanged = Signal(str)  # seule cette carte doit se redessiner (instance, paquet ou données)
    idsChanged = Signal()  # l'ensemble des fenêtres à créer a changé
    instancesChanged = Signal()
    packagesChanged = Signal()
    editModeChanged = Signal()
    importChanged = Signal()
    guidesChanged = Signal()
    gestureChanged = Signal()  # rectangle en cours de geste (cellules éclairées de la grille)
    settingsRequested = Signal(str)
    screensChanged = Signal()

    def __init__(self, bridge, layer=None):
        super().__init__(bridge)
        self.bridge = bridge
        self._feeds = WinjetFeeds(bridge)
        self._winjets = {}
        self._screens = []
        self._ids = []
        self._revision = 0
        self._fps = {}  # id -> empreinte de la dernière publication
        self._screen_gen = 0
        self._last_packages = None
        self._last_edit = None
        self._guides = []
        self._gesture = None  # {'id', 'info', 'screen', 'others'} figé au début d'un geste
        self._live = {}  # rectangle courant du geste, relatif à la zone de travail
        self._pending = ''
        self._local = {}  # géométries validées localement, en attendant l'écho du backend
        self._sent = {}
        self._import = {}
        self._edit = False
        self._edit_asked = None
        self._timer = QTimer(self)
        self._timer.setSingleShot(True)
        self._timer.setInterval(60)
        self._timer.timeout.connect(self.refresh_screens)
        self.layer = layer
        if layer is None:
            from desktop_layer import DesktopLayer

            self.layer = DesktopLayer(self)
        self._epoch = None
        bridge.changed.connect(self.refresh_state)
        if hasattr(bridge, 'shellEvent'):
            bridge.shellEvent.connect(self.shell_event)
        app = QGuiApplication.instance()
        if app is not None:
            app.screenAdded.connect(self._watch_screen)
            app.screenRemoved.connect(self.schedule_screens)
            for screen in QGuiApplication.screens():
                self._watch_screen(screen, refresh=False)
        self.refresh_screens()
        self.refresh_state()

    # Écrans ---------------------------------------------------------------------------------------------------------

    def _watch_screen(self, screen, refresh=True):
        for signal in (screen.geometryChanged, screen.availableGeometryChanged, screen.logicalDotsPerInchChanged):
            signal.connect(self.schedule_screens)
        if refresh:
            self.schedule_screens()

    def schedule_screens(self, *args):
        """Regroupe les rafales (branchement d'un écran = plusieurs signaux) en un seul recalcul."""
        self._timer.start()

    def describe_screens(self):
        primary = QGuiApplication.primaryScreen()
        out = []
        for screen in QGuiApplication.screens():
            work = screen.availableGeometry()
            ratio = screen.devicePixelRatio()
            full = screen.geometry()
            out.append({
                'key': screen_key(screen.name(), round(full.width() * ratio), round(full.height() * ratio)),
                'name': screen.name(),
                'x': work.x(), 'y': work.y(), 'w': work.width(), 'h': work.height(),
                'primary': screen == primary,
            })
        return out

    def refresh_screens(self):
        screens = self.describe_screens()
        names_changed = [s['name'] for s in screens] != [s['name'] for s in self._screens]
        self._screens = screens
        self._screen_gen += 1
        self.layer.screens_changed()
        if names_changed:
            self.screensChanged.emit()
        self._publish(force=True)

    # État -----------------------------------------------------------------------------------------------------------

    def _raw(self):
        raw = (getattr(self.bridge, '_state', None) or {}).get('winjets')
        return drop_retired(raw) if isinstance(raw, dict) else {}

    def shell_event(self, event):
        # Explorer redémarré : le backend relaie TaskbarCreated (événement `desktop.explorerRestarted`).
        if event == 'desktop.explorerRestarted':
            self.layer.reapply()

    def refresh_state(self, *args):
        # Tolère aussi un compteur d'époque dans l'état (`desktop.explorerEpoch`) au cas où l'événement serait manqué.
        epoch = ((getattr(self.bridge, '_state', None) or {}).get('desktop') or {}).get('explorerEpoch')
        if epoch is not None and epoch != self._epoch:
            if self._epoch is not None:
                self.layer.reapply()
            self._epoch = epoch
        raw = self._raw()
        if raw == self._winjets and self._screens:
            return
        self._winjets = raw
        self._publish()

    def _package(self, package_id):
        return next((p for p in self._winjets.get('packages') or [] if p.get('id') == package_id), None)

    def _occupied(self, screen_key_, skip=''):
        out = []
        for inst in self._winjets.get('instances') or []:
            if inst.get('id') == skip or inst.get('hidden'):
                continue
            geometry = self._geometry_of(inst)
            if geometry and geometry[0]['key'] == screen_key_:
                g = geometry[1]
                out.append((g['x'], g['y'], g['w'], g['h']))
        return out

    def _geometry_of(self, inst):
        stored = self._local.get(inst.get('id')) or inst.get('geometry') or {}
        return reconcile_geometry(stored, self._screens)

    def _place_new(self, inst):
        """Instance sans place : première place libre de l'écran principal."""
        package = self._package(inst.get('packageId')) or {}
        size = ((package.get('definition') or {}).get('size') or {}).get('default') or [CELL * 2 + GUTTER, CELL]
        primary = next((s for s in self._screens if s.get('primary')), self._screens[0] if self._screens else None)
        if primary is None:
            return None
        w, h = clamp_size(size[0], size[1])
        x, y = first_free_slot(primary['w'], primary['h'], w, h, self._occupied(primary['key'], inst.get('id')))
        nx, ny = normalize(x, y, primary['w'], primary['h'])
        return primary, {'screen': primary['key'], 'x': x, 'y': y, 'w': w, 'h': h, 'nx': nx, 'ny': ny, 'recovered': False}

    def _publish(self, force=False):
        """Recalcule ids visibles et géométries ; persiste (une fois) ce qui a été rescalé, récupéré ou placé."""
        raw = self._winjets
        self._edit = bool(raw.get('editMode')) if self._edit_asked is None else self._edit_asked
        if self._edit_asked is not None and bool(raw.get('editMode')) == self._edit_asked:
            self._edit_asked = None
        self.layer.set_editing(self._edit)
        for inst in raw.get('instances') or []:
            ident = inst.get('id')
            if ident in self._local and self._local[ident] == inst.get('geometry'):
                self._local.pop(ident, None)
            if not self._screens:
                continue
            resolved = self._geometry_of(inst) or self._place_new(inst)
            if resolved is None:
                continue
            geometry = resolved[1]
            stored = inst.get('geometry') or {}
            if ident not in self._local and self._differs(geometry, stored) and self._sent.get(ident) != geometry:
                self._sent[ident] = geometry
                self._local[ident] = geometry
                self.bridge.action('winjets.setGeometry', {'instanceId': ident, 'geometry': geometry})
        ids = [i['id'] for i in raw.get('instances') or [] if i.get('id') and not i.get('hidden')]
        data = raw.get('data') or {}
        fps = {}
        for inst in raw.get('instances') or []:
            ident = inst.get('id')
            if ident:
                fps[ident] = (inst, self._package(inst.get('packageId')), data.get(ident), self._local.get(ident), self._screen_gen)
        if self._gesture and self._gesture['id'] not in fps:  # instance retirée en plein geste : plus de repère ni d'étiquette
            self.endGesture()
        touched = diff_snapshots(self._fps, fps)
        self._fps = fps
        packages = raw.get('packages') or []
        packages_changed = packages != self._last_packages
        edit_changed = self._edit != self._last_edit
        self._last_packages, self._last_edit = packages, self._edit
        ids_changed = ids != self._ids
        import_changed = (raw.get('lastImport') or {}) != self._import
        if not (touched or packages_changed or edit_changed or ids_changed or import_changed):
            return
        self._revision += 1
        if ids_changed:
            self._ids = ids
            self.idsChanged.emit()
        if import_changed:
            self._import = raw.get('lastImport') or {}
            self.importChanged.emit()
        if packages_changed:
            self.packagesChanged.emit()
        if edit_changed:
            self.editModeChanged.emit()
        for ident in touched:
            self.instanceChanged.emit(ident)
        if touched:
            self.instancesChanged.emit()
        self.changed.emit()

    @staticmethod
    def _differs(a, b):
        keys = ('screen', 'x', 'y', 'w', 'h')
        if any(a.get(k) != b.get(k) for k in keys):
            return True
        return bool(a.get('recovered')) != bool(b.get('recovered'))

    # Propriétés -----------------------------------------------------------------------------------------------------

    revision = Property(int, lambda s: s._revision, notify=changed)
    editMode = Property(bool, lambda s: s._edit, notify=editModeChanged)
    instanceIds = Property('QVariantList', lambda s: list(s._ids), notify=idsChanged)
    packages = Property('QVariantList', lambda s: list(s._winjets.get('packages') or []), notify=packagesChanged)
    lastImport = Property('QVariantMap', lambda s: dict(s._import), notify=importChanged)
    guides = Property('QVariantList', lambda s: list(s._guides), notify=guidesChanged)
    gestureRect = Property('QVariantMap', lambda s: dict(s._live), notify=gestureChanged)
    pendingSettings = Property(str, lambda s: s._pending, notify=settingsRequested)

    @Property('QVariantList', notify=instancesChanged)
    def instances(self):
        """Toutes les instances (masquées comprises) pour la page Réglages."""
        return [self.instance(i['id']) for i in self._winjets.get('instances') or [] if i.get('id')]

    screenNames = Property('QVariantList', lambda s: [x['name'] for x in s._screens], notify=screensChanged)
    cellSize = Property(int, lambda s: CELL, constant=True)
    cellGap = Property(int, lambda s: GUTTER, constant=True)
    gridMargin = Property(int, lambda s: MARGIN, constant=True)
    desktopLayer = Property(QObject, lambda s: s.layer, constant=True)
    feeds = Property(QObject, lambda s: s._feeds, constant=True)  # system.disk, weather.*, files.* (voir winjet_feeds)

    @Slot(str, result='QVariantMap')
    def workArea(self, screen_name):
        """Zone de travail d'un écran en px logiques globaux (x, y, w, h)."""
        screen = next((s for s in self._screens if s['name'] == screen_name), None)
        return {k: screen[k] for k in ('x', 'y', 'w', 'h')} if screen else {}

    @Slot(str, str, float, result='QVariantMap')
    def clockInfo(self, timezone, pattern, now_ms):
        """Heure d'un fuseau IANA (repli : heure locale) : composantes pour l'analogique + texte formaté."""
        moment = datetime.fromtimestamp(now_ms / 1000).astimezone() if now_ms else datetime.now().astimezone()
        if timezone:
            try:
                from zoneinfo import ZoneInfo

                moment = moment.astimezone(ZoneInfo(timezone))
            except Exception:
                pass  # fuseau inconnu ou tzdata absent : heure locale
        return {
            'hour': moment.hour, 'minute': moment.minute, 'second': moment.second,
            'text': winjet_expr.format_date(moment, pattern or 'HH:mm'),
        }

    @Slot(str, result='QVariantMap')
    def package(self, package_id):
        return dict(self._package(package_id) or {})

    @Slot(str, result='QVariantMap')
    def instance(self, instance_id):
        """Instance prête à afficher : géométrie absolue (px logiques globaux), réglages et état complétés."""
        inst = next((i for i in self._winjets.get('instances') or [] if i.get('id') == instance_id), None)
        if inst is None:
            return {}
        package = self._package(inst.get('packageId')) or {}
        definition = package.get('definition') or {}
        resolved = self._geometry_of(inst) or self._place_new(inst)
        size = definition.get('size') or {}
        out = {
            'id': instance_id,
            'packageId': inst.get('packageId', ''),
            'packageName': package.get('name', inst.get('packageId', '')),
            'packageOk': bool(package),
            'hidden': bool(inst.get('hidden')),
            'opacity': opacity_of(inst.get('opacity')),
            'definition': definition,
            'layouts': definition.get('layouts') or [],
            'settingsSpec': definition.get('settings') or [],
            'settings': default_settings(definition, inst.get('settings')),
            'state': {**(definition.get('state') or {}), **(inst.get('state') or {})},
            'data': flatten_data((self._winjets.get('data') or {}).get(instance_id)),
            'minW': max(MIN_SIDE, (size.get('min') or [MIN_SIDE, MIN_SIDE])[0]),
            'minH': max(MIN_SIDE, (size.get('min') or [MIN_SIDE, MIN_SIDE])[1]),
            'maxW': min(MAX_W, (size.get('max') or [MAX_W, MAX_H])[0]),
            'maxH': min(MAX_H, (size.get('max') or [MAX_W, MAX_H])[1]),
            'network': package.get('network') or [],
            'valid': resolved is not None,
        }
        if resolved is not None:
            screen, geometry = resolved
            out.update({
                'screenName': screen['name'], 'screenKey': screen['key'],
                'x': screen['x'] + geometry['x'], 'y': screen['y'] + geometry['y'],
                'w': geometry['w'], 'h': geometry['h'], 'recovered': bool(geometry.get('recovered')),
            })
        return out

    # Expressions ----------------------------------------------------------------------------------------------------

    @Slot(str, 'QVariant', result='QVariant')
    def render(self, text, ctx):
        return winjet_expr.render(text, self._ctx(ctx))

    @Slot(str, 'QVariant', result=str)
    def renderText(self, text, ctx):
        return winjet_expr.render_text(text, self._ctx(ctx))

    @Slot('QVariant', 'QVariant', result='QVariant')
    def resolve(self, value, ctx):
        """Valeur d'un champ de bloc : chaînes en gabarit, autres types tels quels."""
        value = value.toVariant() if hasattr(value, 'toVariant') else value
        return winjet_expr.render(value, self._ctx(ctx)) if isinstance(value, str) else value

    @Slot('QVariant', str, result=str)
    def fmt(self, value, kind):
        """Format d'affichage (currency, percent, number), le même que `fmt(x, 'kind')` des gabarits."""
        value = value.toVariant() if hasattr(value, 'toVariant') else value
        return winjet_expr.fmt(value, kind) or ''

    @Slot('QVariant', 'QVariant', result=bool)
    def condition(self, expression, ctx):
        return winjet_expr.condition(expression, self._ctx(ctx))

    @staticmethod
    def _ctx(ctx):
        ctx = ctx.toVariant() if hasattr(ctx, 'toVariant') else ctx
        if not isinstance(ctx, dict):
            return {}
        if not isinstance(ctx.get('now'), datetime):
            moment = winjet_expr._as_datetime(ctx.get('now'))
            ctx = {**ctx, 'now': moment or datetime.now().astimezone()}
        return ctx

    @Slot('QVariant', float, float, result=int)
    def layoutIndex(self, layouts, width, height):
        layouts = layouts.toVariant() if hasattr(layouts, 'toVariant') else layouts
        return select_layout(layouts, width, height)

    @Slot(str, str, result=str)
    def assetUrl(self, package_id, src):
        """URL locale d'une image du paquet ; refuse tout ce qui sort de `assets/`."""
        package = self._package(package_id)
        src = str(src or '').replace('\\', '/')
        if not package or not package.get('dir') or '..' in src.split('/') or not src.startswith('assets/'):
            return ''
        return QUrl.fromLocalFile(f"{package['dir']}/{src}").toString()

    @Slot(str, result=str)
    def fileUrl(self, path):
        return QUrl.fromLocalFile(path).toString() if path else ''

    @Slot(str, str, result=bool)
    def openUrl(self, package_id, url):
        package = self._package(package_id) or {}
        homepage = (package.get('manifest') or {}).get('homepage', '')
        if not host_allowed(url, package.get('network'), homepage):
            return False
        return QDesktopServicesProxy.open(url)

    # Aimantage et géométrie depuis le QML ---------------------------------------------------------------------------

    @Slot(result=QPointF)
    def cursorPos(self):
        """Position globale du curseur (px logiques) : référence d'un geste, indépendante de la fenêtre qui se déplace."""
        return QCursor.pos()

    @Slot(str)
    def beginGesture(self, instance_id):
        """Début d'un geste : écran et cartes voisines figés, pas de relecture de toutes les instances à chaque mouvement."""
        self._gesture = None
        info, screen, others = self._frame(instance_id)
        self._gesture = {'id': instance_id, 'info': info, 'screen': screen, 'others': others}

    @Slot()
    def endGesture(self):
        self._gesture = None
        self._live = {}
        self.gestureChanged.emit()
        self.clearGuides()

    def _frame(self, instance_id):
        if self._gesture and self._gesture['id'] == instance_id:
            return self._gesture['info'], self._gesture['screen'], self._gesture['others']
        info = self.instance(instance_id)
        screen = next((s for s in self._screens if s['name'] == info.get('screenName')), None)
        others = [
            (o['x'] - screen['x'], o['y'] - screen['y'], o['w'], o['h'])
            for oid in self._ids
            if oid != instance_id
            for o in [self.instance(oid)]
            if o.get('valid') and o.get('screenName') == screen['name']
        ] if screen else []
        return info, screen, others

    def _publish_live(self, screen, x, y, w, h):
        live = {'screen': screen['name'], 'x': x, 'y': y, 'w': w, 'h': h}
        if live != self._live:
            self._live = live
            self.gestureChanged.emit()

    @Slot(str, float, float, float, float, bool, result='QVariantMap')
    def snapMove(self, instance_id, x, y, w, h, snap):
        """Coordonnées globales logiques en entrée et en sortie ; publie les repères. snap faux (Alt) : position libre."""
        info, screen, others = self._frame(instance_id)
        if screen is None:
            return {'x': x, 'y': y, 'guides': []}
        nx, ny, guides = snap_move((x - screen['x'], y - screen['y'], w, h), others, (screen['w'], screen['h']), snap=snap)
        nx, ny, w, h = clamp_rect(nx, ny, w, h, screen['w'], screen['h'])
        self.setGuides(screen['name'], guides)
        self._publish_live(screen, nx, ny, w, h)
        return {'x': screen['x'] + nx, 'y': screen['y'] + ny, 'guides': guides}

    @Slot(str, str, float, float, float, float, bool, result='QVariantMap')
    def snapResize(self, instance_id, handle, x, y, w, h, snap):
        info, screen, others = self._frame(instance_id)
        if screen is None or handle not in HANDLES:
            return {'x': x, 'y': y, 'w': w, 'h': h, 'guides': []}
        rx, ry, rw, rh, guides = snap_resize(
            (x - screen['x'], y - screen['y'], w, h), handle, others, (screen['w'], screen['h']),
            (info['minW'], info['minH']), (info['maxW'], info['maxH']), snap=snap,
        )
        self.setGuides(screen['name'], guides)
        self._publish_live(screen, rx, ry, rw, rh)
        return {'x': screen['x'] + rx, 'y': screen['y'] + ry, 'w': rw, 'h': rh, 'guides': guides}

    def setGuides(self, screen_name, guides):
        value = [{**g, 'screen': screen_name} for g in guides]
        if value != self._guides:
            self._guides = value
            self.guidesChanged.emit()

    @Slot()
    def clearGuides(self):
        self.setGuides('', [])

    @Slot(str, float, float, float, float)
    def commitGeometry(self, instance_id, x, y, w, h):
        """Fin d'un geste : écran contenant le centre de la carte, géométrie relative, une seule persistance."""
        self.endGesture()
        cx, cy = x + w / 2, y + h / 2
        screen = next(
            (s for s in self._screens if s['x'] <= cx < s['x'] + s['w'] and s['y'] <= cy < s['y'] + s['h']), None
        ) or next((s for s in self._screens if s.get('primary')), None)
        info = self.instance(instance_id)
        if screen is None or not info:
            return
        w, h = clamp_size(w, h, (info['minW'], info['minH']), (info['maxW'], info['maxH']))
        rx, ry, w, h = clamp_rect(x - screen['x'], y - screen['y'], w, h, screen['w'], screen['h'])
        nx, ny = normalize(rx, ry, screen['w'], screen['h'])
        geometry = {'screen': screen['key'], 'x': rx, 'y': ry, 'w': w, 'h': h, 'nx': nx, 'ny': ny, 'recovered': False}
        self._local[instance_id] = geometry
        self._sent[instance_id] = geometry
        self.bridge.action('winjets.setGeometry', {'instanceId': instance_id, 'geometry': geometry})
        self._revision += 1
        self.instanceChanged.emit(instance_id)
        self.changed.emit()

    @Slot(str, float, float, result='QVariantMap')
    def freeSlot(self, screen_name, w, h):
        """Première place libre pour une carte w×h (coordonnées relatives à la zone de travail)."""
        screen = next((s for s in self._screens if s['name'] == screen_name), None) or next(
            (s for s in self._screens if s.get('primary')), None
        )
        if screen is None:
            return {}
        x, y = first_free_slot(screen['w'], screen['h'], w, h, self._occupied(screen['key']))
        return {'screen': screen['name'], 'x': x, 'y': y}

    # Actions --------------------------------------------------------------------------------------------------------

    @Slot(str)
    def add(self, package_id):
        package = self._package(package_id) or {}
        size = ((package.get('definition') or {}).get('size') or {}).get('default') or [CELL * 2 + GUTTER, CELL]
        w, h = clamp_size(size[0], size[1])
        primary = next((s for s in self._screens if s.get('primary')), None)
        args = {'packageId': package_id}
        if primary:
            x, y = first_free_slot(primary['w'], primary['h'], w, h, self._occupied(primary['key']))
            args.update({'screen': primary['key'], 'x': x, 'y': y})
        self.bridge.action('winjets.add', args)

    @Slot(str)
    def remove(self, instance_id):
        self.bridge.action('winjets.remove', {'instanceId': instance_id})

    @Slot(str, bool)
    def setHidden(self, instance_id, hidden):
        self.bridge.action('winjets.setHidden', {'instanceId': instance_id, 'hidden': bool(hidden)})

    @Slot(str, 'QVariant')
    def setOpacity(self, instance_id, opacity):
        """Opacité du fond 0..100 ; null = suivre le verre global. Un seul envoi, au relâchement du curseur."""
        opacity = opacity.toVariant() if hasattr(opacity, 'toVariant') else opacity
        self.bridge.action('winjets.setOpacity', {'instanceId': instance_id, 'opacity': opacity_of(opacity)})

    @Slot(str, 'QVariant')
    def setSettings(self, instance_id, settings):
        settings = settings.toVariant() if hasattr(settings, 'toVariant') else settings
        self.bridge.action('winjets.setSettings', {'instanceId': instance_id, 'settings': settings})

    @Slot(str, str, 'QVariant')
    def setState(self, instance_id, key, value):
        self.bridge.action('winjets.setState', {'instanceId': instance_id, 'key': key, 'value': value})

    @Slot(str)
    def refresh(self, instance_id):
        self.bridge.action('winjets.refresh', {'instanceId': instance_id})

    @Slot(bool)
    def setEditMode(self, enabled):
        self._edit_asked = bool(enabled)
        self._edit = bool(enabled)
        self.layer.set_editing(self._edit)
        if not enabled:
            self.endGesture()
        self.bridge.action('winjets.setEditMode', {'enabled': bool(enabled)})
        self.editModeChanged.emit()

    @Slot()
    def recoverAll(self):
        self.bridge.action('winjets.recoverAll', {})

    @Slot(str)
    def inspect(self, path):
        self.bridge.action('winjets.inspect', {'path': _local_path(path)})

    @Slot(result=str)
    def pickPackage(self):
        """Sélecteur de fichier du bouton « Choisir un fichier » ; le ZIP choisi est inspecté, pas installé."""
        from PySide6.QtWidgets import QFileDialog

        path, _ = QFileDialog.getOpenFileName(None, 'Winjet', '', 'Winjet (*.zip *.winjet)')
        if path:
            self.inspect(path)
        return path

    @Slot(str)
    def importPackage(self, path):
        self.bridge.action('winjets.import', {'path': _local_path(path)})

    @Slot(str)
    def uninstall(self, package_id):
        self.bridge.action('winjets.uninstall', {'packageId': package_id})

    @Slot(str)
    def requestSettings(self, instance_id):
        """Le bouton ⚙ d'une carte (fenêtre sans focus) ouvre le dialogue dans Réglages > Winjets."""
        self._pending = instance_id
        self.settingsRequested.emit(instance_id)
        self.bridge.action('settings', {'section': 'winjets'})

    @Slot()
    def clearPending(self):
        self._pending = ''


def _local_path(path):
    url = QUrl(str(path))
    return url.toLocalFile() if url.isLocalFile() else str(path)


class QDesktopServicesProxy:
    """Isolé pour que les tests puissent remplacer l'ouverture réelle d'un lien."""

    @staticmethod
    def open(url):
        from PySide6.QtGui import QDesktopServices

        return QDesktopServices.openUrl(QUrl(url))
