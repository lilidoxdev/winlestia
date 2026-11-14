"""Vue d'ensemble : liste des fenêtres du backend et miniatures DWM en direct.

Une miniature DWM doit être enregistrée par le processus qui possède la fenêtre de destination et se dessine AU-DESSUS de son
contenu : on l'enregistre donc ici, sur la fenêtre QML de la vue, en pixels physiques. Tout échec DWM est avalé (la carte montre
alors l'icône de l'application)."""
from __future__ import annotations
import ctypes
import time
from PySide6.QtCore import QCoreApplication, QObject, Property, Signal, Slot
from win_native import ThumbnailProperties, dwmapi, user32

RECT_DESTINATION, OPACITY, VISIBLE, SOURCE_CLIENT_ONLY = 0x1, 0x4, 0x8, 0x10
RETRY_DELAY = 1.0  # secondes entre deux tentatives d'enregistrement d'une même fenêtre


class OverviewController(QObject):
    windowsChanged = Signal()

    def __init__(self, bridge):
        super().__init__(bridge)
        self.bridge = bridge
        self._windows = []
        self._pending = 0
        self._thumbs = {}  # fenêtre source -> [miniature, fenêtre de destination, propriétés, dernier état]
        self._failed = {}  # fenêtre source -> instant du dernier échec d'enregistrement
        app = QCoreApplication.instance()
        if app:
            app.aboutToQuit.connect(self.clear)

    windows = Property('QVariantList', lambda s: s._windows, notify=windowsChanged)

    @Slot()
    def request(self):
        self._pending += 1
        self.bridge.action('overviewWindows', {})

    @Slot()
    def stop(self):
        self._pending = 0
        if self._windows:
            self._windows = []
            self.windowsChanged.emit()

    def receive(self, items):
        """Réponse `windows` du backend. Vrai si elle appartient à la vue (même quand elle arrive trop tard et est ignorée)."""
        if items and not (isinstance(items[0], dict) and 'iconId' in items[0]):
            return False
        if self._pending <= 0:
            return bool(items)
        self._pending -= 1
        # Poignées en texte : un rôle entier de ListModel tronquerait au-delà de 2^31.
        self._windows = [{**item, 'hwnd': str(int(item['hwnd']))} for item in items if isinstance(item, dict) and 'hwnd' in item]
        self.windowsChanged.emit()
        return True

    def unregister(self, source):
        entry = self._thumbs.pop(source, None)
        if entry:
            try:
                dwmapi.DwmUnregisterThumbnail(entry[0])
            except Exception:
                pass

    @Slot(str, str)
    def act(self, hwnd, action):
        """Active ou ferme une fenêtre de la liste. Le backend prend le premier plan : on l'y autorise d'abord."""
        try:
            pid = int(getattr(getattr(self.bridge, 'args', None), 'backend_pid', 0) or 0)
            if pid:
                user32.AllowSetForegroundWindow(pid)
        except Exception:
            pass
        self.bridge.action('overviewAct', {'hwnd': str(hwnd), 'action': action})

    @Slot(str)
    def remove(self, source):
        try:
            self.unregister(int(source))
        except Exception:
            pass

    @Slot()
    def clear(self):
        for source in list(self._thumbs):
            self.unregister(source)
        self._failed.clear()

    def _entry(self, source, dest):
        """Entrée enregistrée de `source` pour la fenêtre `dest` (None si DWM refuse ; nouvel essai au plus une fois par seconde)."""
        entry = self._thumbs.get(source)
        if entry is not None and entry[1] != dest:
            self.unregister(source)
            entry = None
        if entry is not None:
            return entry
        now = time.monotonic()
        if now - self._failed.get(source, -RETRY_DELAY) < RETRY_DELAY:
            return None
        thumb = ctypes.c_void_p()
        if dwmapi.DwmRegisterThumbnail(dest, source, ctypes.byref(thumb)) != 0 or not thumb.value:
            self._failed[source] = now
            return None
        size = (ctypes.c_long * 2)()
        if dwmapi.DwmQueryThumbnailSourceSize(thumb, ctypes.byref(size)) != 0 or size[0] <= 1 or size[1] <= 1:
            dwmapi.DwmUnregisterThumbnail(thumb)
            self._failed[source] = now
            return None
        self._failed.pop(source, None)
        props = ThumbnailProperties()
        props.flags = RECT_DESTINATION | OPACITY | VISIBLE | SOURCE_CLIENT_ONLY
        props.visible = 1
        props.client_only = 0
        # [poignée, fenêtre de destination, structure réutilisée, dernier (rectangle, opacité) appliqué]
        entry = self._thumbs[source] = [thumb.value, dest, props, None]
        return entry

    def _apply(self, dest, ratio, source, x, y, w, h, opacity):
        entry = self._entry(source, dest)
        if entry is None:
            return False
        left, top = round(x * ratio), round(y * ratio)
        state = (left, top, round((x + w) * ratio), round((y + h) * ratio), max(0, min(255, round(opacity * 255))))
        if entry[3] == state:
            return True
        props = entry[2]
        props.destination[:] = state[:4]
        props.opacity = state[4]
        if dwmapi.DwmUpdateThumbnailProperties(entry[0], ctypes.byref(props)) != 0:
            entry[3] = None
            return False
        entry[3] = state
        return True

    @Slot(QObject, str, float, float, float, float, float, result=bool)
    def place(self, window, source, x, y, w, h, opacity):
        """Place la miniature de `source` dans le rectangle (x, y, w, h) logique de `window`. Faux si DWM refuse."""
        try:
            return self._apply(int(window.winId()), float(window.devicePixelRatio()), int(source), x, y, w, h, opacity)
        except Exception:
            return False

    @Slot(QObject, 'QVariantList', result='QVariantList')
    def placeMany(self, window, items):
        """Un appel par image : `items` = [[source, x, y, w, h, opacité], ...]. Retourne un booléen par entrée (faux si DWM refuse)."""
        out = [False] * len(items)
        try:
            dest = int(window.winId())
            ratio = float(window.devicePixelRatio())
        except Exception:
            return out
        for i, item in enumerate(items):
            try:
                out[i] = self._apply(dest, ratio, int(item[0]), item[1], item[2], item[3], item[4], item[5])
            except Exception:
                pass
        return out
