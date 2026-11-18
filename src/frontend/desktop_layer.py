"""Couche bureau des Winjets (Win32 via ctypes).

Les cartes sont des fenêtres Qt.Tool sans propriétaire (un propriétaire Progman serait détruit au redémarrage d'Explorer) :
WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, placées HWND_BOTTOM sans activation.

- Hors édition, WM_WINDOWPOSCHANGING est filtré (QAbstractNativeEventFilter) pour que le fond de pile tienne.
- « Afficher le bureau » : SetWinEventHook(EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT), donc événementiel, sans sondage.
  Quand WorkerW/Progman passe au premier plan, les cartes montent au-dessus du bureau sans activation ; dès qu'une autre
  fenêtre passe au premier plan elles retournent au fond.
- Mode édition : ordre normal (HWND_TOP) pour pouvoir manipuler les cartes, retour au fond à la sortie.
- Aucun minuteur, aucun hook souris. Les fonctions de décision (classify_foreground, forced_insert_after) sont pures.
Hors plateforme « windows » (tests hors écran), tous les appels natifs sont ignorés.
"""
from __future__ import annotations

import ctypes
import ctypes.wintypes as wt
import sys

from PySide6.QtCore import QAbstractNativeEventFilter, QCoreApplication, QObject, QRectF, Qt, QTimer, Signal, Slot
from PySide6.QtGui import QGuiApplication, QPainterPath

GWL_EXSTYLE = -20
WS_EX_TOOLWINDOW = 0x00000080
WS_EX_APPWINDOW = 0x00040000
WS_EX_NOACTIVATE = 0x08000000
SWP_NOSIZE, SWP_NOMOVE, SWP_NOZORDER, SWP_NOACTIVATE, SWP_FRAMECHANGED = 0x1, 0x2, 0x4, 0x10, 0x20
HWND_TOP, HWND_BOTTOM = 0, 1
WM_WINDOWPOSCHANGING = 0x0046
WM_WTSSESSION_CHANGE = 0x02B1
WTS_SESSION_LOCK, WTS_SESSION_UNLOCK = 7, 8
EVENT_SYSTEM_FOREGROUND = 0x0003
EVENT_SYSTEM_MOVESIZEEND = 0x000B
EVENT_SYSTEM_MINIMIZESTART = 0x0016
EVENT_SYSTEM_MINIMIZEEND = 0x0017
OBJID_WINDOW = 0
GEOMETRY_DEBOUNCE_MS = 150
EDIT_FADE_MS = 220  # fondu de sortie de la grille (180 ms dans WinjetLayer.qml) + marge avant destruction des overlays
WINEVENT_OUTOFCONTEXT = 0x0000

DESKTOP_CLASSES = {'Progman', 'WorkerW'}
# Éléments du shell qu'on active sans quitter le bureau affiché (barre des tâches, débordement, menu Démarrer).
SHELL_CLASSES = {
    'Shell_TrayWnd', 'Shell_SecondaryTrayWnd', 'NotifyIconOverflowWindow', 'TopLevelWindowForOverflowXamlIsland',
    'XamlExplorerHostIslandWindow', 'Windows.UI.Core.CoreWindow',
}


class WindowPos(ctypes.Structure):
    _fields_ = [
        ('hwnd', ctypes.c_void_p), ('hwndInsertAfter', ctypes.c_void_p), ('x', ctypes.c_int), ('y', ctypes.c_int),
        ('cx', ctypes.c_int), ('cy', ctypes.c_int), ('flags', ctypes.c_uint),
    ]


class Msg(ctypes.Structure):
    _fields_ = [
        ('hwnd', ctypes.c_void_p), ('message', ctypes.c_uint), ('wParam', ctypes.c_size_t), ('lParam', ctypes.c_ssize_t),
        ('time', ctypes.c_uint), ('pt', wt.POINT),
    ]


def classify_foreground(class_name):
    """'desktop' (WorkerW/Progman), 'shell' (barre des tâches...) ou 'other'."""
    if class_name in DESKTOP_CLASSES:
        return 'desktop'
    if class_name in SHELL_CLASSES:
        return 'shell'
    return 'other'


def forced_insert_after(mode, insert_after, flags):
    """Nouveau (insertAfter, flags) pour un WM_WINDOWPOSCHANGING, ou None si rien à changer.

    Seul le mode « fond » force HWND_BOTTOM ; un changement qui ne touche pas à l'ordre Z est laissé tel quel.
    """
    if mode != 'bottom' or flags & SWP_NOZORDER:
        return None
    if insert_after == HWND_BOTTOM:
        return None
    return HWND_BOTTOM, flags


def exstyle_for_focus(style, accept):
    """Style étendu d'une carte : NOACTIVATE retiré le temps d'une saisie (`accept`), remis sinon. Pure."""
    return style & ~WS_EX_NOACTIVATE if accept else style | WS_EX_NOACTIVATE


def desktop_shown(windows):
    """Vrai si le bureau est réellement affiché : aucune fenêtre d'application visible, non réduite, non « cloaked ».

    `windows` : dicts {visible, minimized, cloaked, tool, ours, shell} des fenêtres de premier niveau. Les fantômes UWP (cloaked),
    les fenêtres outils, les éléments du shell et nos propres cartes ne comptent pas.
    """
    for w in windows:
        if w.get('ours') or w.get('tool') or w.get('shell') or w.get('cloaked'):
            continue
        if w.get('visible') and not w.get('minimized'):
            return False
    return True


def next_mode(mode, foreground_kind, ours, shown=True):
    """Transitions du mode de pile hors édition : bottom <-> above (bureau réellement affiché). 'edit' ne bouge pas."""
    if mode == 'edit' or ours:
        return mode
    if foreground_kind == 'desktop':
        # Un clic sur le fond d'écran avec des applications ouvertes ne doit pas faire monter les cartes par-dessus.
        return 'above' if shown else 'bottom'
    if foreground_kind == 'shell':
        return mode
    return 'bottom'


class _ZFilter(QAbstractNativeEventFilter):
    def __init__(self, layer):
        super().__init__()
        self.layer = layer

    def nativeEventFilter(self, event_type, message):
        try:
            msg = Msg.from_address(int(message))
            if msg.message == WM_WINDOWPOSCHANGING and msg.hwnd in self.layer.handles:
                self.layer.keep_bottom(msg.lParam)
            elif msg.message == WM_WTSSESSION_CHANGE and msg.hwnd == self.layer.session_hwnd:
                if msg.wParam == WTS_SESSION_LOCK:
                    self.layer.set_locked(True)
                elif msg.wParam == WTS_SESSION_UNLOCK:
                    self.layer.set_locked(False)
        except Exception:
            pass
        return False, 0


def is_covered(rect, windows):
    """Vrai si une fenêtre ordinaire visible (ni réduite, ni masquée, ni outil, ni coquille, ni nous) contient entièrement `rect`."""
    left, top, right, bottom = rect
    for w in windows:
        if w.get('ours') or w.get('shell') or w.get('tool') or w.get('minimized') or w.get('cloaked') or not w.get('visible'):
            continue
        r = w.get('rect')
        if r and r[0] <= left and r[1] <= top and r[2] >= right and r[3] >= bottom:
            return True
    return False


class DesktopLayer(QObject):
    # Les Winjets à rendu animé peuvent se mettre en pause : plein écran/présentation/session verrouillée, ou carte recouverte.
    pauseChanged = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.windows = {}  # id(QWindow) -> QWindow
        self.hwnds = {}  # id(QWindow) -> HWND, retenu à l'enregistrement (la fenêtre peut être détruite avant le nettoyage)
        self.handles = set()
        self.backdrops = set()  # HWND de la grille d'édition : toujours sous les cartes
        self.mode = 'bottom'
        self.native = sys.platform == 'win32' and QGuiApplication.platformName() == 'windows'
        self.user = None
        self.filter = None
        self.hook = None
        self.geometry_hooks = []
        self.recompute = None
        self.callback = None
        self.glass = None
        self.locked = False
        self.fullscreen = False
        self.covered = set()
        self.session_hwnd = 0
        if self.native:
            self._setup()

    # Natif ----------------------------------------------------------------------------------------------------------

    def _setup(self):
        self.user = ctypes.windll.user32
        user = self.user
        user.GetWindowLongPtrW.restype = ctypes.c_ssize_t
        user.GetWindowLongPtrW.argtypes = [ctypes.c_void_p, ctypes.c_int]
        user.SetWindowLongPtrW.restype = ctypes.c_ssize_t
        user.SetWindowLongPtrW.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_ssize_t]
        user.SetWindowPos.argtypes = [
            ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_uint
        ]
        user.GetClassNameW.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_int]
        user.GetWindowThreadProcessId.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong)]
        self.filter = _ZFilter(self)
        QCoreApplication.instance().installNativeEventFilter(self.filter)
        self._hook()
        app = QCoreApplication.instance()
        app.aboutToQuit.connect(self.stop)
        from desktop_glass import CardGlass

        self.glass = CardGlass()

    def _hook(self):
        if not self.native or self.hook:
            return
        proc = ctypes.WINFUNCTYPE(
            None, ctypes.c_void_p, ctypes.c_ulong, ctypes.c_void_p, ctypes.c_long, ctypes.c_long, ctypes.c_ulong,
            ctypes.c_ulong,
        )

        def on_event(hook, event, hwnd, object_id, child_id, thread, time):
            try:
                self.on_foreground(hwnd)
            except Exception:
                pass

        self.callback = proc(on_event)  # référence gardée : le ramasse-miettes détruirait le thunk
        self.user.SetWinEventHook.restype = ctypes.c_void_p
        self.user.SetWinEventHook.argtypes = [
            ctypes.c_uint, ctypes.c_uint, ctypes.c_void_p, proc, ctypes.c_ulong, ctypes.c_ulong, ctypes.c_uint
        ]
        self.hook = self.user.SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, None, self.callback, 0, 0, WINEVENT_OUTOFCONTEXT
        )

        # Fenêtre déplacée/redimensionnée/réduite/restaurée sans changement de premier plan : recalcul différé, un seul coup.
        self.recompute = QTimer(self)
        self.recompute.setSingleShot(True)
        self.recompute.setInterval(GEOMETRY_DEBOUNCE_MS)
        self.recompute.timeout.connect(self._refresh_pause)

        def on_geometry(hook, event, hwnd, object_id, child_id, thread, time):
            if hwnd and object_id == OBJID_WINDOW and hwnd not in self.handles:  # filtre bon marché, avant tout appel système
                self.recompute.start()

        self.geometry_callback = proc(on_geometry)
        for first, last in ((EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MOVESIZEEND),
                            (EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND)):
            handle = self.user.SetWinEventHook(first, last, None, self.geometry_callback, 0, 0, WINEVENT_OUTOFCONTEXT)
            if handle:
                self.geometry_hooks.append(handle)

    def _unhook(self):
        if self.hook and self.user:
            self.user.UnhookWinEvent.argtypes = [ctypes.c_void_p]
            self.user.UnhookWinEvent(self.hook)
        self.hook = None
        for handle in self.geometry_hooks:
            if self.user:
                self.user.UnhookWinEvent(handle)
        self.geometry_hooks = []
        if self.recompute is not None:
            self.recompute.stop()

    def stop(self):
        self._unhook()
        self._unregister_session()
        if self.filter is not None and QCoreApplication.instance() is not None:
            QCoreApplication.instance().removeNativeEventFilter(self.filter)
            self.filter = None
        if self.glass:
            self.glass.close()  # détache chaque enregistrement de verre
        self.handles.clear()
        self.hwnds.clear()

    def _unregister_session(self):
        hwnd, self.session_hwnd = self.session_hwnd, 0
        if hwnd:
            try:
                ctypes.windll.wtsapi32.WTSUnRegisterSessionNotification(ctypes.c_void_p(hwnd))
            except Exception:
                pass

    def _register_session(self):
        """Notifications de verrouillage de session sur une carte vivante (au besoin la suivante)."""
        if self.session_hwnd:
            return
        for hwnd in sorted(self.handles):
            try:
                if ctypes.windll.wtsapi32.WTSRegisterSessionNotification(ctypes.c_void_p(hwnd), 0):
                    self.session_hwnd = hwnd
                    return
            except Exception:
                return

    def _class_of(self, hwnd):
        buffer = ctypes.create_unicode_buffer(256)
        self.user.GetClassNameW(hwnd, buffer, 256)
        return buffer.value

    def _is_ours(self, hwnd):
        pid = ctypes.c_ulong()
        self.user.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        return pid.value == ctypes.windll.kernel32.GetCurrentProcessId()

    def set_locked(self, locked):
        if locked != self.locked:
            self.locked = locked
            self.pauseChanged.emit()

    def _refresh_pause(self):
        """Recalcul de l'état de pause (appelé à chaque changement de premier plan, jamais par minuteur)."""
        try:
            state = ctypes.c_int(0)
            ok = ctypes.windll.shell32.SHQueryUserNotificationState(ctypes.byref(state)) == 0
            # QUNS_BUSY 2 (plein écran), QUNS_RUNNING_D3D_FULL_SCREEN 3, QUNS_PRESENTATION_MODE 4
            fullscreen = ok and state.value in (2, 3, 4)
            covered = set()
            if self.mode != 'edit' and self.handles:
                windows = self._top_windows()
                for hwnd in self.handles:
                    rect = wt.RECT()
                    if (self.user.GetWindowRect(ctypes.c_void_p(hwnd), ctypes.byref(rect)) and self.mode == 'bottom'
                        and is_covered((rect.left, rect.top, rect.right, rect.bottom), windows)):
                        covered.add(hwnd)
        except Exception:
            return
        if fullscreen != self.fullscreen or covered != self.covered:
            self.fullscreen, self.covered = fullscreen, covered
            self.pauseChanged.emit()

    @Slot(QObject, result=bool)
    def paused(self, window):
        """Vrai si le rendu animé de cette carte doit être suspendu."""
        if self.locked or self.fullscreen:
            return True
        if window is None:
            return False
        return int(window.property('winjetHandle') or 0) in self.covered

    def on_foreground(self, hwnd):
        if self.native:
            self._refresh_pause()
        if not hwnd or self.mode == 'edit':
            return
        kind = classify_foreground(self._class_of(hwnd))
        mode = next_mode(self.mode, kind, self._is_ours(hwnd), desktop_shown(self._top_windows()) if kind == 'desktop' else True)
        if mode != self.mode:
            self.mode = mode
            self._stack_all()

    def _top_windows(self):
        """Fenêtres de premier niveau visibles, décrites pour `desktop_shown` (EnumWindows, appelée à un changement de premier plan)."""
        user = self.user
        found = []
        cloaked = ctypes.c_int(0)
        dwm = ctypes.windll.dwmapi

        def visit(hwnd, _):
            exstyle = user.GetWindowLongPtrW(hwnd, GWL_EXSTYLE)
            cls = self._class_of(hwnd)
            cloaked.value = 0
            dwm.DwmGetWindowAttribute(ctypes.c_void_p(hwnd), 14, ctypes.byref(cloaked), 4)  # DWMWA_CLOAKED
            found.append({
                'visible': bool(user.IsWindowVisible(hwnd)),
                'minimized': bool(user.IsIconic(hwnd)),
                'cloaked': cloaked.value != 0,
                'tool': bool(exstyle & WS_EX_TOOLWINDOW) and not exstyle & WS_EX_APPWINDOW,
                'ours': hwnd in self.handles or self._is_ours(hwnd),
                'rect': self._rect_of(hwnd),
                'shell': cls in DESKTOP_CLASSES or cls in SHELL_CLASSES,
            })
            return True

        proc = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
        user.EnumWindows.argtypes = [proc, ctypes.c_void_p]
        user.IsWindowVisible.argtypes = [ctypes.c_void_p]
        user.IsIconic.argtypes = [ctypes.c_void_p]
        user.EnumWindows(proc(visit), None)
        return found

    def _rect_of(self, hwnd):
        rect = wt.RECT()
        if self.user.GetWindowRect(ctypes.c_void_p(hwnd), ctypes.byref(rect)):
            return (rect.left, rect.top, rect.right, rect.bottom)
        return None

    def keep_bottom(self, lparam):
        """WM_WINDOWPOSCHANGING : réécrit la demande en place pour garder le fond de pile."""
        pos = WindowPos.from_address(lparam)
        forced = forced_insert_after(self.mode, pos.hwndInsertAfter or 0, pos.flags)
        if forced:
            pos.hwndInsertAfter = forced[0]

    def _style(self, hwnd):
        style = self.user.GetWindowLongPtrW(hwnd, GWL_EXSTYLE)
        wanted = (style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW
        if wanted != style:
            self.user.SetWindowLongPtrW(hwnd, GWL_EXSTYLE, wanted)
            self.user.SetWindowPos(hwnd, None, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED)

    def _stack(self, hwnd):
        # HWND_BOTTOM au repos ; HWND_TOP quand le bureau est affiché ou en édition (toujours sans activation).
        after = HWND_BOTTOM if self.mode == 'bottom' else HWND_TOP
        self.user.SetWindowPos(hwnd, ctypes.c_void_p(after), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)

    def _stack_all(self):
        # Fonds d'abord, cartes ensuite : HWND_TOP place chaque fenêtre au-dessus des précédentes.
        if self.native:
            for hwnd in sorted(self.handles, key=lambda h: h not in self.backdrops):
                self._stack(hwnd)

    # API QML / pont ---------------------------------------------------------------------------------------------------

    @Slot(QObject)
    def register(self, window):
        """À appeler quand la fenêtre est visible : styles, pile, verre."""
        if window is None:
            return
        key = id(window)
        if key not in self.windows:
            self.windows[key] = window
            window.destroyed.connect(lambda *_, k=key: self._forget(k))  # jamais l'objet lui-même : il est déjà détruit
        if not self.native:
            return
        hwnd = int(window.winId())
        self.hwnds[key] = hwnd
        self.handles.add(hwnd)
        window.setProperty('winjetHandle', hwnd)
        self._register_session()
        self._style(hwnd)
        self._stack(hwnd)

    @Slot(QObject, bool, result=bool)
    def acceptFocus(self, window, accept):
        """Saisie au clavier dans une carte (bloc `note`) : l'active le temps de la saisie, puis la rend non activable.

        Pas de minuteur ni de hook : appelé par le QML au clic dans le champ (accept=True) et à la perte du focus ou de
        l'activation (accept=False). La pile reste au fond : keep_bottom réécrit tout WM_WINDOWPOSCHANGING d'une activation
        en HWND_BOTTOM tant que le mode est « bottom » ; à la fin on reposte quand même la carte au fond, par sécurité.
        """
        if window is None:
            return False
        window.setFlag(Qt.WindowDoesNotAcceptFocus, not accept)  # côté Qt (MA_NOACTIVATE, focus fenêtre)
        if not self.native:
            return True
        hwnd = int(window.property('winjetHandle') or 0)
        if not hwnd:
            return True
        style = self.user.GetWindowLongPtrW(hwnd, GWL_EXSTYLE)
        wanted = exstyle_for_focus(style, accept)
        if wanted != style:
            self.user.SetWindowLongPtrW(hwnd, GWL_EXSTYLE, wanted)
            self.user.SetWindowPos(hwnd, None, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED)
        if accept:
            if not self.user.SetForegroundWindow(ctypes.c_void_p(hwnd)):  # autorisé : le processus vient de recevoir un clic
                self.acceptFocus(window, False)  # échec : jamais de carte laissée activable
                return False
            window.requestActivate()
        else:
            self._stack(hwnd)
        return True


    @Slot(QObject)
    def unregister(self, window):
        if window is not None:
            self._forget(id(window))

    def _forget(self, key):
        self.windows.pop(key, None)
        hwnd = self.hwnds.pop(key, 0)
        if hwnd:
            if hwnd == self.session_hwnd:
                self._unregister_session()
            self.handles.discard(hwnd)
            self.backdrops.discard(hwnd)
            if self.native:
                self._register_session()
            if self.glass:
                self.glass.detach(hwnd)

    @Slot(QObject, int, int, int, int)
    def place(self, window, x, y, width, height):
        """Une seule mise en place native par image (x, y, largeur, hauteur) : pas de saut entre quatre SetWindowPos."""
        if window is not None:
            window.setGeometry(x, y, width, height)

    @Slot(QObject)
    def registerBackdrop(self, window):
        """Fond de la grille d'édition : même traitement qu'une carte, mais toujours sous les cartes en mode édition."""
        if window is None:
            return
        self.register(window)
        hwnd = int(window.property('winjetHandle') or 0)
        if hwnd:
            self.backdrops.add(hwnd)
            if self.native:
                self._stack_all()

    @Slot(QObject, float, bool, float)
    def shape(self, window, radius, glass, opacity):
        """Verre natif + coins arrondis de la carte ; `glass` faux -> la carte peint un voile opaque seule."""
        if not self.native or window is None or not self.glass:
            return
        hwnd = int(window.property('winjetHandle') or 0)
        if not hwnd:
            return
        if not glass:
            self.glass.detach(hwnd)
            return
        path = QPainterPath()
        path.addRoundedRect(QRectF(0, 0, window.width(), window.height()), radius, radius)
        self.glass.apply(hwnd, path, window.devicePixelRatio(), opacity)

    def set_editing(self, editing):
        # Sortie différée : les overlays (grille) restent EDIT_FADE_MS pour le fondu, les cartes ne repassent dessous qu'ensuite.
        pending = getattr(self, '_exit_timer', None)
        if editing:
            if pending is not None:
                pending.stop()
            self._apply_mode('edit')
            return
        if self.mode != 'edit':
            return
        if pending is None:
            pending = self._exit_timer = QTimer(self)
            pending.setSingleShot(True)
            pending.timeout.connect(lambda: self._apply_mode('bottom'))
        pending.start(EDIT_FADE_MS)

    def _apply_mode(self, mode):
        if mode != self.mode:
            self.mode = mode
            self._stack_all()

    def reapply(self):
        """Après un redémarrage d'Explorer : styles, pile et hook sont réinstallés."""
        if not self.native:
            return
        self._unhook()
        self._hook()
        for hwnd in list(self.handles):
            self._style(hwnd)
        if self.mode == 'above':
            self.mode = 'bottom'
        self._stack_all()

    def screens_changed(self):
        """Écran ajouté/retiré/redimensionné : les fenêtres se repositionnent côté QML, la pile est reposée."""
        self._stack_all()
