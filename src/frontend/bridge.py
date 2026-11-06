"""Bridge QML : état de l'interface, actions et slots exposés au QML."""
from __future__ import annotations
import ctypes, json, os, shutil, threading, time, urllib.request, urllib.parse
import ctypes.wintypes
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from PySide6.QtCore import QCoreApplication, QObject, Property, Signal, Slot, QTimer, QUrl, QRect
from PySide6.QtGui import QCursor, QKeySequence, QColor, QRegion
from PySide6.QtWidgets import QApplication, QFileDialog, QInputDialog, QLineEdit
from interaction import EdgePolicy, is_fullscreen
from custom_palette import make_palette, DEFAULT, valid_color
from screen_picker import ClickPicker
from search import palette_search, emoji_choices, wallpaper_choices, wallpaper_refresh, wallpaper_known, wallpaper_import, wallpaper_remove, wallpaper_hide, wallpaper_hidden, wallpaper_restore_hidden, user_wallpaper_dir, settings_uri_known
from live_model import LiveListModel
from capture_native import CaptureController
from wallpaper_theme import WallpaperTheme
from wallpaper_thumbs import thumb_for
from win_native import user32, kernel32, MonitorInfo
from app_state import ROOT, CONFIG
import app_state
from bridge_client import PipeClient
from windows_player import WindowsPlayer


class Bridge(QObject):
    colorPicked = Signal(str)
    surfaceChanged = Signal()
    changed = Signal()
    perfChanged = Signal()
    frameChanged = Signal()
    screensChanged = Signal()
    spectrumChanged = Signal()
    audioBarsChanged = Signal()
    catalogChanged = Signal()
    dockChanged = Signal()
    clipboardChanged = Signal()
    noticeChanged = Signal()
    sessionRequested = Signal(str)
    searchChanged = Signal()
    navigationRequested = Signal(str)
    weatherResult = Signal(dict)
    windowChanged = Signal()
    geometryChanged = Signal()
    shellEvent = Signal(str)
    tabRequested = Signal(int)
    dictationChanged = Signal()
    dictationKeyCaptured = Signal(int)
    wallpapersReady = Signal()
    wallpaperPickerChanged = Signal()
    wallpaperThumbReady = Signal(str, str)
    # Palette sources: open windows (asked on opening, dropped on closing) and the file index (debounced, newest request wins by generation number).
    fileChanged = Signal()
    _windows = []
    _file_gen = 0
    _file_term = ''
    _file_items = []
    _file_status = 'idle'
    _file_sent = False
    _file_timer = None

    def __init__(self, args):
        super().__init__()
        self.surface_scene = None
        self.args = args
        self._window = None
        self._state = {}
        self._pending_controls = {}
        self._pending_displays = {}
        self._pending_audio = {}
        self._icon_requests = {}
        self._dark = False
        self._player = WindowsPlayer(self)
        self.top = None
        self.dock = None
        self.social_win = None
        self.away_social = 0
        self.bookmarks_win = None
        self.away_bookmarks = 0
        self.last_reply = 0
        self.peek_until = 0
        self.away_top = 0
        self.away_dock = 0
        self.force_dock = 0
        self.top_seen = False
        self._screen = QApplication.primaryScreen()
        self.client = PipeClient(args.pipe)
        self.client.received.connect(self.update_state)
        self.client.start()
        self._dict = {'state': 'idle', 'level': 0.0, 'levels': [0.0] * 32, 'lang': '', 'text': '', 'error': ''}
        self._dict_stopped = False
        self._dict_until = 0
        self._dict_area = {'x': 0, 'y': 0, 'width': 0, 'height': 0}
        self.dictation_timer = QTimer(self)
        self.dictation_timer.setInterval(33)
        self.dictation_timer.timeout.connect(self.dictation_tick)
        self.pointer = QTimer(self)
        self.pointer.setInterval(90)
        self.pointer.timeout.connect(self.poll_pointer)
        self.pointer.start()
        self._weather = {}
        self.weather_busy = False
        self.weather_at = 0
        self.modal = False
        self.weatherResult.connect(self.weather_ready)
        self.edge_policy = EdgePolicy()
        self._results = []
        self.query = ''
        self.opening_until = 0
        self.mode = 'dashboard'
        self.workspace_board = 'apps'
        self.health_at = 0
        self._notice = ''
        self.navigation_epoch = 0
        self.recording_kind = None
        self.walltheme = WallpaperTheme(self)
        self.walltheme.changed.connect(self.theme_ready)
        self.fullscreen_names = ()
        self.quiet_on = False
        self.quiet_forced = 'auto'
        self._bars_want = False
        self._theme_revision = 0
        QApplication.instance().screenAdded.connect(self.screen_list_changed)
        QApplication.instance().screenRemoved.connect(self.screen_list_changed)
        for display in QApplication.screens():
            display.geometryChanged.connect(lambda rect: self.screensChanged.emit())
        try:
            self.city = json.loads((CONFIG / 'weather.json').read_text(encoding='utf-8')).get('city', '')
        except (OSError, ValueError):
            self.city = ''

    languageMode = Property(str, lambda s: app_state.TRANSLATOR.mode, notify=changed)
    language = Property(str, lambda s: app_state.TRANSLATOR.language, notify=changed)

    @Slot(str)
    def setLanguage(self, mode):
        app_state.TRANSLATOR.select(mode)
        self.changed.emit()
        self.engine.retranslate()

    def current_theme(self):
        return self.walltheme.for_screen(self._screen.name())

    def theme_ready(self):
        self._theme_revision += 1
        self.publish_palette()
        self.changed.emit()
        self.frameChanged.emit()

    # Winlestia Fichiers (WPF) cannot see a live-wallpaper override, so the palette actually shown here is handed over in %LOCALAPPDATA%\Panneaux\palette-shell.json (auto mode only, written only when it changed; the file is event-watched there, never polled).
    def publish_palette(self):
        # Computed here, written off the UI thread (ordered single-worker executor). Leaving auto mode writes {"mode":"custom"} so Palette.cs drops the stale auto palette.
        try:
            if self.themeMode == 'auto':
                palette = self.current_theme().get('palette', {})
                if not palette:
                    return
                text = json.dumps({'mode': 'auto', 'palette': palette}, sort_keys=True, default=str)
            else:
                text = '{"mode":"custom"}'
            if text == getattr(self, '_published_palette', None):
                return
            self._published_palette = text
            if not hasattr(self, '_publisher'):
                self._publisher = ThreadPoolExecutor(max_workers=1, thread_name_prefix='Panneaux Publish')

            def write(text=text):
                try:
                    data = Path(
                        os.environ.get('WINLESTIA_DATA_DIR')
                        or Path(os.environ.get('LOCALAPPDATA') or str(Path.home() / 'AppData' / 'Local')) / 'Panneaux'
                    )
                    data.mkdir(parents=True, exist_ok=True)
                    target = data / 'palette-shell.json'
                    tmp = target.with_suffix('.tmp')
                    tmp.write_text(text, encoding='utf-8')
                    os.replace(tmp, target)
                except Exception:
                    if getattr(self, '_published_palette', None) == text:
                        self._published_palette = None

            self._publisher.submit(write)
        except Exception:
            # Publication de palette facultative : la prochaine mise à jour la rejouera.
            pass

    def theme_inputs_changed(self):
        # Event-driven replacement of the explorer 4 s poll: runs only when a theme-relevant setting, the folder list or the wallpaper palette actually changed.
        s = self.settings
        key = (
            s.get('themeMode'),
            json.dumps(s.get('customPalette'), sort_keys=True, default=str),
            s.get('explorerTheme'),
            s.get('accentBorders'),
            str(s.get('explorerFolders')),
            self._theme_revision,
        )
        if key == getattr(self, '_theme_inputs', None):
            return
        self._theme_inputs = key
        self.publish_palette()
        self.sync_explorer_theme()

    def screen_list_changed(self, screen):
        self.screensChanged.emit()

    @Property(bool, constant=True)
    def hibernateAvailable(self):
        try:
            caps = ctypes.create_string_buffer(
                76
            )  # SYSTEM_POWER_CAPABILITIES; byte 6 = SystemS4, byte 8 = HiberFilePresent
            return bool(ctypes.windll.powrprof.GetPwrCapabilities(caps)) and caps.raw[8] != 0 and caps.raw[6] != 0
        except Exception:
            return False

    themeRevision = Property(int, lambda s: s._theme_revision, notify=frameChanged)
    fullscreenScreens = Property('QVariantList', lambda s: list(s.fullscreen_names), notify=frameChanged)

    @Slot(str, int, result='QVariantMap')
    def paletteForScreen(self, name, revision):
        return self.walltheme.for_screen(name).get('palette', {})

    _theme_pending = None

    def theme_pending_expire(self):
        if self._theme_pending:
            self._theme_pending = None
            self.changed.emit()

    themeMode = Property(
        str,
        lambda s: s._theme_pending or s._state.get('settings', {}).get('themeMode', 'dark' if s._dark else 'light'),
        notify=changed,
    )
    adaptivePalette = Property(
        'QVariantMap',
        lambda s: (
            make_palette(s.settings.get('customPalette'))['palette']
            if s.themeMode == 'custom'
            else s.current_theme().get('palette', {})
            if s.themeMode == 'auto'
            else {}
        ),
        notify=changed,
    )
    wallpaperInfo = Property('QVariantMap', lambda s: s.current_theme(), notify=changed)
    _wpicker = {}
    wallpaperPicker = Property('QVariantMap', lambda s: s._wpicker, notify=wallpaperPickerChanged)
    glassOpacity = Property(
        float,
        lambda s: max(
            0.02,
            min(
                0.95,
                float(
                    s.settings.get('customGlassOpacity', 0.12)
                    if s.themeMode == 'custom'
                    else s.settings.get('glassOpacity', 0.95)
                ),
            ),
        ),
        notify=changed,
    )
    # Auto theme, dark scheme on a bright wallpaper: pale text needs a denser tint to stay legible. Wallpapers at L* 40 or below render exactly as before.
    glassTintOpacity = Property(
        float,
        lambda s: min(
            0.95,
            s.glassOpacity * 0.75 / (1 - s.glassOpacity * 0.25)
            + (
                max(0.0, float(s.current_theme().get('luminance', 0)) - 0.4) * 3
                if s.themeMode == 'auto' and s.dark
                else 0.0
            ),
        ),
        notify=changed,
    )
    surfaceRevision = Property(int, lambda s: s.surface_scene.revision if s.surface_scene else 0, notify=surfaceChanged)

    @Slot(str, int, result='QVariantMap')
    def surfaceForScreen(self, name, revision):
        return self.surface_scene.data.get(name, {}) if self.surface_scene else {}

    fileDragChanged = Signal()

    @Property(bool, notify=fileDragChanged)
    def fileDragActive(self):
        return getattr(self, '_file_drag_active', False)

    @Property(bool, notify=fileDragChanged)
    def fileDragNearTop(self):
        return getattr(self, '_file_drag_near', False)

    def poll_file_drag(self):
        down = bool(user32.GetAsyncKeyState(1) & 0x8000)
        point = QCursor.pos()
        now = time.monotonic()
        if down and not getattr(self, '_file_drag_down', False):
            self._file_drag_origin = point
            self._file_drag_start = now
            self._file_drag_foreign = False
            try:
                cursor = ctypes.wintypes.POINT()
                if user32.GetCursorPos(ctypes.byref(cursor)):
                    owner = ctypes.c_ulong(0)
                    user32.GetWindowThreadProcessId(user32.WindowFromPoint(cursor), ctypes.byref(owner))
                    self._file_drag_foreign = owner.value != os.getpid()
            except Exception:
                # Position du curseur illisible pendant un glisser : on garde l'état précédent.
                pass
        active = (
            down
            and getattr(self, '_file_drag_foreign', False)
            and now - getattr(self, '_file_drag_start', now) > 0.25
            and (point - getattr(self, '_file_drag_origin', point)).manhattanLength() > 12
        )
        self._file_drag_down = down
        screen = QApplication.screenAt(point)
        near = bool(active and screen and point.y() <= screen.geometry().top() + 150)
        changed = active != getattr(self, '_file_drag_active', False) or near != getattr(self, '_file_drag_near', False)
        self._file_drag_active = active
        self._file_drag_near = near
        if changed:
            self.fileDragChanged.emit()

    def sync_explorer_theme(self):
        if not self.settings.get('explorerTheme', False) and not self.settings.get('accentBorders', True):
            self._explorer_signature = None
            return
        palette = self.adaptivePalette
        args = {
            'accent': palette.get('m3primary', '#afd4f0' if self.dark else '#895143'),
            'background': palette.get('m3surface', '#101518' if self.dark else '#fff8f6'),
            'text': palette.get('m3onSurface', '#e5ebef' if self.dark else '#261916'),
        }
        signature = json.dumps(args, sort_keys=True) + str(self._state.get('settings', {}).get('explorerFolders', []))
        if signature != getattr(self, '_explorer_signature', None):
            self.action('explorerPalette', args)
            self._explorer_signature = signature

    @Slot()
    def updateSurfaces(self):
        if self.surface_scene:
            self.surface_scene.request()

    frameThickness = Property(int, lambda s: max(4, min(40, int(s.settings.get('frameThickness', 16)))), notify=changed)
    frameEnabled = Property(bool, lambda s: bool(s._state.get('settings', {}).get('screenFrame', True)), notify=changed)

    @Property('QVariantList', notify=screensChanged)
    def frameScreens(self):
        result = []
        for screen in QApplication.screens():
            rect = screen.geometry()
            result.append(
                {'name': screen.name(), 'x': rect.x(), 'y': rect.y(), 'width': rect.width(), 'height': rect.height()}
            )
        return result

    @Property(QObject, constant=True)
    def capture(self):
        if not hasattr(self, '_capture'):
            self._capture = CaptureController(self)
            self.config_path = CONFIG
        return self._capture

    @Property(QObject, constant=True)
    def audioApplications(self):
        if not hasattr(self, '_audio_apps'):
            self._audio_apps = LiveListModel(self)
        return self._audio_apps

    @Property(QObject, constant=True)
    def audioOutputs(self):
        if not hasattr(self, '_audio_outputs'):
            self._audio_outputs = LiveListModel(self)
        return self._audio_outputs

    @Property(QObject, constant=True)
    def audioInputs(self):
        if not hasattr(self, '_audio_inputs'):
            self._audio_inputs = LiveListModel(self)
        return self._audio_inputs

    player = Property(QObject, lambda s: s._player, constant=True)
    dark = Property(
        bool,
        lambda s: (
            make_palette(s.settings.get("customPalette"))["dark"]
            if s.themeMode == "custom"
            else bool(s.current_theme().get("dark", True))
            if s.themeMode == "auto"
            else s._dark
        ),
        notify=changed,
    )
    performance = Property('QVariantMap', lambda s: s._state.get('performance', {}), notify=perfChanged)
    sysInfo = Property('QVariantMap', lambda s: s._state.get('sysInfo', {}), notify=perfChanged)
    weather = Property('QVariantMap', lambda s: s._weather, notify=changed)
    audioReady = Property(bool, lambda s: bool(s._state.get('audioMixer', {}).get('ready', True)), notify=changed)
    controls = Property('QVariantMap', lambda s: s._state.get('controls', {}), notify=changed)
    apps = Property('QVariantList', lambda s: s._state.get('apps', []), notify=catalogChanged)
    favorites = Property('QVariantList', lambda s: s._state.get('favorites', []), notify=catalogChanged)
    recent = Property('QVariantList', lambda s: s._state.get('recent', []), notify=catalogChanged)
    websites = Property('QVariantList', lambda s: s._state.get('websites', []), notify=catalogChanged)
    profiles = Property('QVariantList', lambda s: s._state.get('profiles', []), notify=catalogChanged)
    runningApps = Property('QVariantList', lambda s: s._state.get('runningApps', []), notify=dockChanged)
    taskbarApps = Property('QVariantList', lambda s: s._state.get('taskbarApps', []), notify=dockChanged)
    clipboardItems = Property('QVariantList', lambda s: s._state.get('clipboard', []), notify=clipboardChanged)
    settings = Property('QVariantMap', lambda s: s._state.get('settings', {}), notify=changed)
    paletteResults = Property('QVariantList', lambda s: s._results, notify=searchChanged)
    fileResults = Property('QVariantList', lambda s: s._file_items, notify=fileChanged)
    fileStatus = Property(str, lambda s: s._file_status, notify=fileChanged)
    workspaceBoard = Property(str, lambda s: s.workspace_board, notify=searchChanged)
    notice = Property(str, lambda s: s._notice, notify=noticeChanged)
    networkKind = Property(
        str,
        lambda s: (
            s._state.get('networkKind') if s._state.get('networkKind') in ('ethernet', 'wifi', 'none') else 'wifi'
        ),
        notify=changed,
    )
    virtualDesktops = Property(
        'QVariantMap',
        lambda s: s._state.get('virtualDesktops') if isinstance(s._state.get('virtualDesktops'), dict) else {},
        notify=changed,
    )
    taskbarEnabled = Property(bool, lambda s: bool(s._state.get('taskbarEnabled', s.args.preview)), notify=changed)
    # Surface visibility preferences: a hidden dock / rail is not polled, not shown and keeps no input mask.
    dockVisible = Property(bool, lambda s: bool(s._state.get('settings', {}).get('dockVisible', True)), notify=changed)
    bookmarksRailVisible = Property(
        bool, lambda s: bool(s._state.get('settings', {}).get('bookmarksRailVisible', True)), notify=changed
    )
    socialRailVisible = Property(
        bool, lambda s: bool(s._state.get('settings', {}).get('socialRailVisible', True)), notify=changed
    )
    dictationState = Property(str, lambda s: s._dict['state'], notify=dictationChanged)
    dictationLevel = Property(float, lambda s: s._dict['level'], notify=dictationChanged)
    dictationLevels = Property('QVariantList', lambda s: s._dict['levels'], notify=dictationChanged)
    dictationLang = Property(str, lambda s: s._dict['lang'], notify=dictationChanged)
    dictationText = Property(str, lambda s: s._dict['text'], notify=dictationChanged)
    dictationError = Property(str, lambda s: s._dict['error'], notify=dictationChanged)
    dictationArea = Property('QVariantMap', lambda s: s._dict_area, notify=dictationChanged)
    audioBars = Property('QVariantList', lambda s: getattr(s, '_audio_bars', []), notify=audioBarsChanged)
    spectrum = Property('QVariantList', lambda s: getattr(s, '_spectrum', [0.0] * 48), notify=spectrumChanged)
    iconRevision = Property(int, lambda s: int(s._state.get('iconRevision', 0)), notify=changed)
    assetRoot = Property(str, lambda s: (ROOT / 'assets').as_posix(), constant=True)
    configRoot = Property(str, lambda s: CONFIG.as_posix(), constant=True)
    window = Property(QObject, lambda s: s._window, notify=windowChanged)
    screenX = Property(int, lambda s: s._screen.geometry().x(), notify=geometryChanged)
    screenY = Property(int, lambda s: s._screen.geometry().y(), notify=geometryChanged)
    screenWidth = Property(int, lambda s: s._screen.geometry().width(), notify=geometryChanged)
    screenHeight = Property(int, lambda s: s._screen.geometry().height(), notify=geometryChanged)
    glassEnabled = Property(
        bool,
        lambda s: s.themeMode == 'auto' or (s.themeMode == 'custom' and bool(s.settings.get('customGlass', False))),
        notify=changed,
    )
    picking = Property(bool, lambda s: getattr(s, '_picking', False), notify=changed)
    pickColor = Property(str, lambda s: getattr(s, '_pick_color', '#ffffff'), notify=changed)
    pickX = Property(int, lambda s: QCursor.pos().x() + 20, notify=changed)
    pickY = Property(int, lambda s: QCursor.pos().y() + 22, notify=changed)

    @Slot(str, result=bool)
    def validHex(self, value):
        return valid_color(value)

    @Slot(str, result='QVariantList')
    def colorHSV(self, value):
        h, s, v, a = QColor(value).getHsvF()
        return [max(0, h), s, v]

    @Slot(float, float, float, result=str)
    def hsvColor(self, h, s, v):
        return QColor.fromHsvF(h % 1, max(0, min(1, s)), max(0, min(1, v))).name()

    @Slot(bool)
    def editTheme(self, enabled):
        self.modal = enabled
        if enabled:
            self.top.hide()
            self.shellEvent.emit('dockHide')
        elif self.top:
            self.top.show()
            self.navigate('settings')

    @Slot()
    def startColorPick(self):
        if getattr(self, '_picking', False):
            return
        self._click_picker = ClickPicker()
        self._picking = True
        self._pick_after = time.monotonic() + 0.3
        self.changed.emit()
        self._pick_timer = QTimer(self)
        self._pick_timer.setInterval(35)
        self._pick_timer.timeout.connect(self.sampleColor)
        self._pick_timer.start()

    def sampleColor(self):
        point = (ctypes.c_long * 2)()
        user32.GetCursorPos(ctypes.byref(point))
        if self._click_picker.choice:
            point[:] = self._click_picker.choice
        dc = user32.GetDC(None)
        try:
            pixel = ctypes.windll.gdi32.GetPixel(dc, point[0], point[1])
        finally:
            user32.ReleaseDC(None, dc)
        if pixel != 0xFFFFFFFF:
            self._pick_color = f'#{pixel & 255:02x}{pixel >> 8 & 255:02x}{pixel >> 16 & 255:02x}'
        self.changed.emit()
        if time.monotonic() < self._pick_after:
            return
        cancel = bool(user32.GetAsyncKeyState(27) & 0x8000 or self._click_picker.cancel or self._click_picker.failed)
        if cancel or self._click_picker.choice:
            self._click_picker.stop()
            self._pick_timer.stop()
            self._pick_timer.deleteLater()
            self._picking = False
            self.changed.emit()
            self.colorPicked.emit('' if cancel else self._pick_color)

    bookmarks = Property(
        'QVariantMap',
        lambda s: {
            'browser': '',
            'browsers': [],
            'items': [],
            'hiddenCount': 0,
            'canDeleteInBrowser': False,
            'busy': False,
            'error': '',
            **(s._state.get('bookmarks') or {}),
            'items': getattr(s, '_bm_items', []),
        },
        notify=changed,
    )
    social = Property(
        'QVariantMap',
        lambda s: {
            'muted': False,
            'sound': True,
            'volume': 70,
            'networks': [],
            'history': [],
            **(s._state.get('social') or {}),
        },
        notify=changed,
    )
    reducedEffects = Property(
        bool, lambda s: bool(s._state.get('settings', {}).get('reducedEffects', False)), notify=changed
    )
    preview = Property(bool, lambda s: s.args.preview, constant=True)

    @Slot(str, 'QVariant')
    def action(self, name, args):
        if hasattr(args, 'toVariant'):
            args = args.toVariant()
        args = args or {}
        if name in (
            'palette',
            'settings',
            'controls',
            'clipboard',
            'workspace',
            'tools',
            'audio',
            'microphone',
            'capture',
            'mixer',
        ):
            self.navigate(
                'workspace:' + args.get('id', 'apps')
                if name == 'workspace'
                else 'settings:' + str(args['section'])
                if name == 'settings' and args.get('section')
                else name
            )
            return
        if name == 'customTheme':
            self._state.setdefault('settings', {}).update(
                themeMode='custom',
                customPalette=args.get('colors', DEFAULT),
                customGlass=bool(args.get('glass')),
                customGlassOpacity=args.get('opacity', 0.12),
            )
            self.changed.emit()
            self.frameChanged.emit()
        if name == 'theme':
            self._state.setdefault('settings', {})['themeMode'] = self._theme_pending = (
                args.get('mode') if args.get('mode') in ('auto', 'custom') else 'dark' if args.get('dark') else 'light'
            )
            QTimer.singleShot(2500, self.theme_pending_expire)
            if args.get('mode') != 'auto':
                self._dark = bool(args.get('dark'))
            self.changed.emit()
            self.frameChanged.emit()
        if name == 'preference' and args.get('key') == 'dictationKey':
            self._state.setdefault('settings', {})['dictationKey'] = int(args.get('value', 0xA5))
            self.changed.emit()
        if name == 'preference' and args.get('key') in ('glassOpacity', 'frameThickness'):
            key = args['key']
            value = (
                max(4, min(40, round(float(args.get('value', 16)))))
                if key == 'frameThickness'
                else max(0.02, min(0.95, float(args.get('value', 0.95))))
            )
            self._state.setdefault('settings', {})[key] = value
            self.changed.emit()
            self.frameChanged.emit()
        if name in ('sessionVolume', 'deviceVolume'):
            self._pending_audio[(name, args.get('id', ''))] = (float(args.get('value', 0)), time.monotonic() + 1.5)
        if name == 'mediaVolume':
            self._pending_controls['mediaVolume'] = (float(args.get('value', 0)), time.monotonic() + 1.5)
        if name == 'brightness' and 'id' in args:
            self._pending_displays[args['id']] = (float(args.get('value', 0)), time.monotonic() + 1.5)
            for row in self._state.get('controls', {}).get('displays', []):
                if row.get('id') == args['id']:
                    row['value'] = float(args.get('value', 0))
            self.changed.emit()
        elif name in ('volume', 'brightness'):
            self._pending_controls[name] = (float(args.get('value', 0)), time.monotonic() + 1.5)
            self._state.setdefault('controls', {})[name] = float(args.get('value', 0))
            self.changed.emit()
        if self.args.backend_pid:
            user32.AllowSetForegroundWindow(self.args.backend_pid)
        request = {'command': name, 'args': args}
        if name in ('icon', 'volume', 'brightness', 'preference', 'sessionVolume', 'deviceVolume', 'mediaVolume'):
            with self.client.update_lock:
                self.client.updates[name + ':' + str(args.get('key', args.get('id', '')))] = request
        else:
            self.client.commands.put(request)
        kernel32.SetEvent(self.client.wakeup)

    @Slot(str)
    def navigate(self, mode):
        if mode == 'close' and self.recording_kind:
            self.recordShortcut(False, self.recording_kind)
        self.navigation_epoch += 1
        self.client.subscription['epoch'] = self.navigation_epoch
        self.top_seen = False
        self.away_top = 0
        self.opening_until = time.monotonic() + 0.8
        if mode.startswith('workspace:'):
            self.workspace_board = mode.split(':', 1)[1]
            self.searchChanged.emit()
        was = self.mode
        self.mode = mode.split(':', 1)[0]
        self.navigationRequested.emit(mode)
        self.palette_lifecycle(was)

    @Slot(str)
    def searchPalette(self, text):
        self.query = text
        self._results = palette_search(text, self._state, app_state.TRANSLATOR.language, self._windows)
        self.searchChanged.emit()

    def palette_lifecycle(self, was):
        if self.mode == 'palette' and was != 'palette':
            self.action('windowList', {})
        elif was == 'palette' and self.mode != 'palette':
            self._windows = []
            self.fileQuery('')

    @Slot(str)
    def fileQuery(self, term):
        # Under 2 characters: no request. Otherwise a 200 ms single-shot timer sends the latest term; older answers are dropped by generation.
        self._file_gen += 1
        self._file_term = term.strip()
        self._file_items = []
        if self._file_timer is None:
            self._file_timer = QTimer(self)
            self._file_timer.setSingleShot(True)
            self._file_timer.setInterval(200)
            self._file_timer.timeout.connect(self.file_send)
        self._file_timer.stop()
        if len(self._file_term) < 2:
            if self._file_sent:
                self._file_sent = False
                self.action('fileCancel', {})
            self._file_status = 'idle'
        else:
            self._file_status = 'searching'
            self._file_timer.start()
        self.fileChanged.emit()

    def file_send(self):
        self._file_sent = True
        self.action('fileSearch', {'term': self._file_term, 'gen': self._file_gen})

    def files_arrived(self, data):
        if not isinstance(data, dict) or data.get('gen') != self._file_gen or self.mode != 'palette':
            return
        gen = self._file_gen
        self._file_status = str(data.get('status') or 'ok')
        self._file_items = [
            {
                'id': 'file:%d' % int(f.get('index', 0)),
                'index': int(f.get('index', 0)),
                'gen': gen,
                'name': str(f.get('name') or ''),
                'comment': str(f.get('path') or ''),
                'icon': 'draft',
                'kind': 'file',
            }
            for f in (data.get('items') or [])
            if isinstance(f, dict)
        ]
        self.fileChanged.emit()

    @Slot(str, result='QVariantList')
    def emojiChoices(self, query):
        return emoji_choices(query)

    @Slot(str, result='QVariantList')
    def wallpaperChoices(self, query):
        return wallpaper_choices(query)

    @Slot()
    def refreshWallpapers(self):
        wallpaper_refresh(self.wallpapersReady.emit)

    @Slot()
    def refreshWallpapersNow(self):
        wallpaper_refresh(self.wallpapersReady.emit, True)

    @Slot(result='QVariantList')
    def addWallpaperImages(self):
        # Native multi-select dialog (the switcher is hidden by the caller meanwhile), then copy into the user folder; returns the new paths.
        files, _ = QFileDialog.getOpenFileNames(
            None,
            QCoreApplication.translate('', 'Ajouter des images'),
            str(Path.home() / 'Pictures'),
            'Images (*.jpg *.jpeg *.png *.bmp *.webp)',
        )
        return wallpaper_import(files) if files else []

    @Slot()
    def openWallpaperFolder(self):
        folder = user_wallpaper_dir()
        try:
            folder.mkdir(parents=True, exist_ok=True)
            os.startfile(str(folder))
        except OSError:
            # Ouverture du dossier facultative : le bouton reste sans effet.
            pass

    @Slot(str, result=bool)
    def removeWallpaper(self, path):
        return wallpaper_remove(path)

    @Slot(str, result=bool)
    def hideWallpaper(self, path):
        return wallpaper_hide(path)

    @Slot(result=int)
    def hiddenWallpaperCount(self):
        return len(wallpaper_hidden())

    @Slot(result=bool)
    def restoreHiddenWallpapers(self):
        return wallpaper_restore_hidden()

    @Slot('QVariantList')
    def requestWallpaperThumbs(self, paths):
        # Switcher thumbnails: one background thread per request (a newer request stops the older one), each file emitted as soon as its cached JPEG exists.
        self._thumb_job = getattr(self, '_thumb_job', 0) + 1
        job = self._thumb_job
        paths = [str(p) for p in paths]

        def run():
            for path in paths:
                if job != self._thumb_job:
                    return
                made = thumb_for(path)
                if made:
                    self.wallpaperThumbReady.emit(path, Path(made).as_uri())

        threading.Thread(target=run, daemon=True).start()

    @Slot()
    def requestWallpaperPicker(self):
        # Displays and running animated-wallpaper software, asked once when the switcher opens (the backend reads process names only; no timer).
        self._wpicker = {}
        self.wallpaperPickerChanged.emit()  # nothing from the previous session (warning, unplugged displays) while the answer is pending
        if self.client is not None:
            self.client.commands.put({'command': 'wallpaperPicker', 'args': {}})
            kernel32.SetEvent(self.client.wakeup)

    @Slot(str)
    def setWallpaper(self, path):
        self.applyWallpaper(path, 0)

    @Slot(str, int)
    def applyWallpaper(self, path, display):
        # Only files from the last scan. The backend applies it (IDesktopWallpaper, display 0 = all displays), nudges its own revision and answers with the animated-software list for the switcher line.
        if not wallpaper_known(path):
            return
        if self.client is None or self.client.handle is None:
            return self.applyWallpaperLocally(path, int(display))
        self.client.commands.put({'command': 'wallpaperApply', 'args': {'path': path, 'display': int(display)}})
        kernel32.SetEvent(self.client.wakeup)

    def applyWallpaperLocally(self, path, display):
        # Backend absent or pipe not connected: former direct SPI call (all displays only). The queued wallpaperChanged reaches the backend on reconnection and re-derives the palette and console colours.
        def apply():
            ok = display <= 0 and bool(ctypes.windll.user32.SystemParametersInfoW(0x14, 0, path, 3))
            if ok:
                if self.client is not None:
                    self.client.commands.put({'command': 'wallpaperChanged', 'args': {}})
                    kernel32.SetEvent(self.client.wakeup)
            else:
                self._notice = (
                    'Le fond d’écran n’a pas pu être appliqué.'
                    if display <= 0
                    else 'Fond d’écran par écran indisponible pour le moment.'
                )
                self.noticeChanged.emit()

        threading.Thread(target=apply, daemon=True).start()

    @Slot('QVariant')
    def executeResult(self, value):
        if hasattr(value, 'toVariant'):
            value = value.toVariant()
        if not value:
            return
        kind = value.get('kind', 'app')
        key = value.get('id', '')
        if kind == 'navigate':
            self.navigate(key)
        elif kind == 'calc':
            self.closePanel()
            self.action('copyText', {'text': value['name'][2:]})
        elif kind == 'autocomplete':
            self.prefillPalette(key)
        elif kind == 'session':
            # Every session action, lock included, waits for the confirmation dialog in Main.qml, which sends confirmed:true itself.
            if key in ('shutdown', 'restart', 'logout', 'lock', 'sleep', 'hibernate'):
                self.sessionRequested.emit(key)
        elif kind == 'wallpaper':
            self.closePanel()
            self.setWallpaper(key)
        elif kind == 'emoji':
            self.closePanel()
            self.action('copyText', {'text': key})
        elif kind == 'window':
            hwnd = value.get('hwnd')
            if isinstance(hwnd, int) and any(w.get('hwnd') == hwnd for w in self._windows):
                self.closePanel()
                self.action('activate', {'hwnd': hwnd})
        elif kind == 'setting':
            if settings_uri_known(key):
                self.closePanel()
                try:
                    os.startfile(key)
                except OSError:
                    # Fichier introuvable ou sans application associée : rien à ouvrir.
                    pass
        elif kind == 'file':
            # The backend resolves the path from its own last result; only a generation and an index travel back (queued before the panel closes, which cancels the search).
            if value.get('gen') == self._file_gen and self.mode == 'palette':
                self.action('fileOpen', {'gen': value['gen'], 'index': value.get('index', -1)})
                self.closePanel()
        else:
            self.closePanel()
            self.action('paletteRun', {'kind': kind, 'id': key, 'query': self.query})

    @Slot()
    def focusPalette(self):
        def visit(item):
            if item.objectName() == 'paletteQuery':
                item.setProperty('text', '')
                item.forceActiveFocus()
                return True
            return any(visit(child) for child in item.childItems())

        if self.top:
            visit(self.top.contentItem())

    @Slot(str)
    def prefillPalette(self, text):
        def visit(item):
            if item.objectName() == 'paletteQuery':
                item.setProperty('text', text)
                return True
            return any(visit(child) for child in item.childItems())

        if self.top:
            visit(self.top.contentItem())
        self.searchPalette(text)

    @Slot(int, int, result=str)
    @Slot(int, result=str)
    def vkName(self, vk):
        names = {
            0xA0: 'Maj gauche',
            0xA1: 'Maj droite',
            0xA2: 'Ctrl gauche',
            0xA3: 'Ctrl droite',
            0xA4: 'Alt gauche',
            0xA5: 'Alt droit',
            0x5B: 'Windows gauche',
            0x5C: 'Windows droite',
            0x14: 'Verr. Maj',
            0x13: 'Pause',
            0x91: 'Arrêt défil.',
            0x5D: 'Menu contextuel',
            0x20: 'Espace',
            0x09: 'Tab',
            0x08: 'Retour arrière',
            0x0D: 'Entrée',
            0x2D: 'Inser',
            0x2E: 'Suppr',
            0x24: 'Début',
            0x23: 'Fin',
            0x21: 'Page préc.',
            0x22: 'Page suiv.',
            0x70 + 0: 'F1',
        }
        if vk in names:
            return names[vk]
        if 0x70 <= vk <= 0x87:
            return 'F%d' % (vk - 0x6F)
        if 0x30 <= vk <= 0x39 or 0x41 <= vk <= 0x5A:
            return chr(vk)
        if vk <= 0:
            return ''
        buffer = ctypes.create_unicode_buffer(64)
        scan = user32.MapVirtualKeyW(vk, 0)
        if scan and user32.GetKeyNameTextW(scan << 16, buffer, 64):
            return buffer.value
        return 'Touche 0x%02X' % vk

    @Slot(int, int, result=str)
    def shortcutText(self, key, modifiers):
        return QKeySequence(key | modifiers).toString(QKeySequence.NativeText)

    @Slot(str, int, int)
    def captureShortcut(self, kind, key, modifiers):
        virtual = 0x70 + key - 0x01000030 if 0x01000030 <= key <= 0x01000052 else key
        mods = (
            (2 if modifiers & 0x04000000 else 0)
            | (4 if modifiers & 0x02000000 else 0)
            | (1 if modifiers & 0x08000000 else 0)
            | (8 if modifiers & 0x10000000 else 0)
        )
        self.action('shortcut', {'kind': kind, 'key': virtual, 'modifiers': mods})

    @Slot(bool, str)
    def recordShortcut(self, enabled, kind):
        self.recording_kind = kind if enabled else None
        self.modal = enabled
        if enabled:
            self._notice = ''
            self.noticeChanged.emit()
        self.action('captureShortcutMode', {'enabled': enabled})

    @Slot(str)
    def chooseEntryIcon(self, key):
        self.modal = True
        try:
            path, _ = QFileDialog.getOpenFileName(
                None,
                QCoreApplication.translate('', 'Icône de cet accès'),
                '',
                'Images (*.png *.ico *.jpg *.jpeg *.webp)',
            )
            if path:
                self.action('entryIcon', {'id': key, 'path': path})
        finally:
            self.modal = False

    @Slot(bool)
    def setAudioBars(self, wanted):
        self._bars_want = bool(wanted)
        wanted = self._bars_want and not self.reducedEffects and not self.quiet_on
        if wanted == bool(self.client.subscription.get('bars')):
            return
        self.client.subscription = {**self.client.subscription, 'bars': wanted}
        self.client.bars_seq = 0
        self.client.bars_idle = True
        if not wanted and getattr(self, '_audio_bars', None):
            self._audio_bars = []
            self.audioBarsChanged.emit()
        kernel32.SetEvent(self.client.wakeup)

    @Slot(bool, int)
    def subscribe(self, visible, page):
        visible = visible or bool(self.recording_kind)
        updated = {
            **self.client.subscription,
            'visible': visible,
            'page': page,
            'controls': bool(self.top and self.top.property('controlsOpen')),
            'mixer': bool(self.top and self.top.property('audioSampling')),
            'wifi': bool(self.top and self.top.property('wifiSampling')),
        }
        if updated != self.client.subscription:
            self.client.subscription = updated
            kernel32.SetEvent(self.client.wakeup)

    @Slot(int)
    def openTab(self, index):
        self.navigation_epoch += 1
        self.client.subscription['epoch'] = self.navigation_epoch
        was = self.mode
        self.mode = 'dashboard'
        self.top_seen = False
        self.opening_until = time.monotonic() + 0.8
        self.tabRequested.emit(index)
        self.palette_lifecycle(was)

    @Slot(str, result=str)
    def localUrl(self, path):
        if not path:
            return ''
        if path.endswith('/.face') and not Path(path).exists():
            return ''
        if path.startswith(('file:', 'image:', 'http:', 'https:', 'qrc:')):
            return path
        return QUrl.fromLocalFile(path).toString()

    @Slot(str, result=str)
    def iconPath(self, name):
        needle = name.lower().replace('.exe', '')
        entry = next((e for e in self._state.get('apps', []) if needle in e.get('name', '').lower()), None)
        return self.iconUrl(entry['id'], self.iconRevision) if entry else ''

    @Slot(str, int, result=str)
    def iconUrl(self, key, revision=0):
        path = self._state.get('icons', {}).get(key, '')
        if not path and time.monotonic() - self._icon_requests.get(key, -10) > 5:
            self._icon_requests[key] = time.monotonic()
            self.action('icon', {'id': key})
        return self.localUrl(path) if path else ''

    @Slot(result=str)
    def pickAvatar(self):
        path, _ = QFileDialog.getOpenFileName(
            None, QCoreApplication.translate('', 'Photo de profil'), '', 'Images (*.png *.jpg *.jpeg *.webp)'
        )
        if path:
            shutil.copyfile(path, CONFIG / '.face')
            self.changed.emit()
        return self.localUrl(str(CONFIG / '.face')) if path else ''

    @Slot(str, str, result=bool)
    def copyFile(self, source, target):
        try:
            source = QUrl(source).toLocalFile() or source
            target = QUrl(target).toLocalFile() or target
            if Path(source).resolve() != Path(target).resolve():
                shutil.copyfile(source, target)
            return True
        except OSError:
            return False

    @Slot()
    def refreshWeather(self):
        if self.weather_busy or time.monotonic() - self.weather_at < 900:
            return
        self.weather_busy = True

        def fetch():
            try:

                def read(url):
                    with urllib.request.urlopen(url, timeout=8) as response:
                        return json.load(response)

                city = self.city
                if not city:
                    geo = read('https://wttr.in/?format=j1')['nearest_area'][0]
                    city = geo['areaName'][0]['value']
                results = read(
                    'https://geocoding-api.open-meteo.com/v1/search?'
                    + urllib.parse.urlencode({'name': city, 'count': 1, 'language': 'en'})
                ).get('results', [])
                if not results:
                    raise ValueError('Ville introuvable')
                location = results[0]
                data = read(
                    'https://api.open-meteo.com/v1/forecast?'
                    + urllib.parse.urlencode(
                        {
                            'latitude': location['latitude'],
                            'longitude': location['longitude'],
                            'current': 'temperature_2m,weather_code',
                            'timezone': 'auto',
                        }
                    )
                )['current']
                code = data['weather_code']
                icon = (
                    'sunny'
                    if code == 0
                    else 'partly_cloudy_day'
                    if code < 3
                    else 'cloud'
                    if code == 3
                    else 'foggy'
                    if code < 50
                    else 'rainy'
                    if code < 70
                    else 'weather_snowy'
                    if code < 80
                    else 'thunderstorm'
                    if code >= 95
                    else 'rainy'
                )
                self.weatherResult.emit(
                    {'temperature': f"{round(data['temperature_2m'])}°C", 'description': location['name'], 'icon': icon}
                )
            except Exception:
                self.weatherResult.emit({'description': 'Météo indisponible · réessayer'})

        threading.Thread(target=fetch, daemon=True).start()

    @Slot(dict)
    def weather_ready(self, value):
        self.weather_busy = False
        self.weather_at = time.monotonic()
        self._weather = value
        self.changed.emit()

    @Slot()
    def chooseCity(self):
        self.modal = True
        try:
            city, ok = QInputDialog.getText(
                None,
                QCoreApplication.translate('', 'Météo · Open-Meteo'),
                QCoreApplication.translate('', 'Ville, pays'),
                QLineEdit.Normal,
                self.city,
            )
            if ok and city.strip():
                self.city = city.strip()[:120]
                (CONFIG / 'weather.json').write_text(json.dumps({'city': self.city}), encoding='utf-8')
                self.weather_at = 0
                self.refreshWeather()
        finally:
            self.modal = False

    @Slot()
    def activate(self):
        if self.top:
            self.top.requestActivate()
            QTimer.singleShot(
                80,
                lambda: (
                    self.top.requestActivate() if self.mode != 'close' and self.top and self.top.isVisible() else None
                ),
            )

    @Slot()
    def revealDock(self):
        self.force_dock = time.monotonic() + 3
        self.shellEvent.emit('dock')

    @Slot()
    def closePanel(self):
        if self.recording_kind:
            self.recordShortcut(False, self.recording_kind)
        self.navigate('close')

    @Slot(dict)
    def dictation_tick(self):
        # Only runs between dictation:start and a final state; coalesced so the pipe never queues stale requests.
        # Garde-fou : arrêt si le backend ne répond plus depuis 15 s (échéance prolongée à chaque réponse non finale).
        if time.monotonic() > self._dict_until:
            self.dictation_timer.stop()
            return
        with self.client.update_lock:
            self.client.updates['dictation'] = {'command': 'dictation', 'args': {}}
        kernel32.SetEvent(self.client.wakeup)

    def dictation_event(self, event):
        if event.startswith('dictation:key:'):
            try:
                vk = int(event.rsplit(':', 1)[1])
            except ValueError:
                return
            self._state.setdefault('settings', {})['dictationKey'] = vk
            self.changed.emit()
            self.dictationKeyCaptured.emit(vk)
            return
        if event == 'dictation:keycancel':
            self.dictationKeyCaptured.emit(0)
            return
        if event == 'dictation:start':
            area = (QApplication.primaryScreen() or self._screen).geometry()
            self._dict_area = {'x': area.x(), 'y': area.y(), 'width': area.width(), 'height': area.height()}
            self._dict = {'state': 'listening', 'level': 0.0, 'levels': [0.0] * 32, 'lang': '', 'text': '', 'error': ''}
            self._dict_stopped = False
            self._dict_until = time.monotonic() + 15
            self.dictationChanged.emit()
            self.dictation_timer.start()
            self.dictation_tick()
        elif event == 'dictation:stop':
            self._dict_stopped = True

    def dictation_update(self, data):
        levels = [max(0.0, min(1.0, float(v))) for v in (data.get('levels') or [])][:32]
        levels += [0.0] * (32 - len(levels))
        state = str(data.get('state') or 'idle')
        self._dict = {
            'state': state,
            'level': max(0.0, min(1.0, float(data.get('level') or 0))),
            'levels': levels,
            'lang': str(data.get('lang') or ''),
            'text': str(data.get('text') or ''),
            'error': str(data.get('error') or ''),
        }
        self.dictationChanged.emit()
        if state in ('done', 'error') or (state == 'idle' and self._dict_stopped):
            self.dictation_timer.stop()
        elif state in ('loading', 'listening', 'transcribing', 'typing'):
            self._dict_until = time.monotonic() + 15

    def screen_names(self, devices):
        # The backend names monitors by Win32 device (\.\DISPLAY1) but QScreen.name() is the model name on Qt 6: translate through the monitor under each screen.
        devices = set(str(n) for n in devices)
        found = set()
        for screen in QApplication.screens():
            geometry = screen.geometry()
            info = MonitorInfo()
            info.size = ctypes.sizeof(info)
            if (
                user32.GetMonitorInfoW(
                    user32.MonitorFromPoint(ctypes.wintypes.POINT(geometry.x() + 1, geometry.y() + 1), 2),
                    ctypes.byref(info),
                )
                and info.name in devices
            ):
                found.add(screen.name())
        return found

    def update_state(self, message):
        if 'dictation' in message and isinstance(message['dictation'], dict):
            self.dictation_update(message['dictation'])
        if 'state' in message:
            self.walltheme.update(
                message['state'].get('wallpapers', []), message['state'].get('wallpaperRevision'), self.client
            )
        if 'state' in message and isinstance(message['state'].get('fullscreenMonitors'), list):
            self._backend_fullscreen = True
            names = tuple(sorted(self.screen_names(message['state']['fullscreenMonitors'])))
            if names != self.fullscreen_names:
                self.fullscreen_names = names
                self.frameChanged.emit()
        if 'state' in message:
            quiet = bool(message['state'].get('quiet', False))
            forced = str(message['state'].get('quietForced', 'auto'))
            if quiet != self.quiet_on or forced != self.quiet_forced:
                self.quiet_on = quiet
                self.quiet_forced = forced
                self.frameChanged.emit()
            self.quiet_apply()
        if 'wallpaperCapture' in message:
            self.walltheme.captured(message['wallpaperCapture'])
        if isinstance(message.get('wallpaperPicker'), dict):
            self._wpicker = dict(self._wpicker, **message['wallpaperPicker'])
            self.wallpaperPickerChanged.emit()
        if isinstance(message.get('windows'), list) and self.mode == 'palette':
            self._windows = message['windows']
            self.searchPalette(self.query)
        if message.get('fileResults'):
            self.files_arrived(message['fileResults'])
        if message.get('notice'):
            self._notice = message['notice']
            self.noticeChanged.emit()
        if isinstance(message.get('audioBars'), dict):
            frame = message['audioBars']
            self.client.bars_seq = frame.get('seq', 0)
            self.client.bars_idle = bool(frame.get('idle', True))
            if isinstance(frame.get('bars'), list) and frame['bars'] != getattr(self, '_audio_bars', None):
                self._audio_bars = frame['bars']
                self.audioBarsChanged.emit()
        if 'spectrum' in message and message['spectrum'] != getattr(self, '_spectrum', None):
            self._spectrum = message['spectrum']
            self.spectrumChanged.emit()
        if 'state' in message:
            self.last_reply = time.monotonic()
        if self._theme_pending and message.get('state', {}).get('settings', {}).get('themeMode') == self._theme_pending:
            self._theme_pending = None
        if 'state' in message and any(
            value != self._state.get(key) for key, value in message['state'].items() if key != 'diagnostics'
        ):
            catalog_changed = any(
                self._state.get(key) != message['state'].get(key)
                for key in ('apps', 'favorites', 'recent', 'websites', 'profiles', 'quickTabs')
            )
            for ident, (value, until) in list(self._pending_displays.items()):
                row = next(
                    (r for r in message['state'].get('controls', {}).get('displays', []) if r.get('id') == ident), None
                )
                if row is None or abs(row.get('value', 0) - value) < 1 or time.monotonic() > until:
                    self._pending_displays.pop(ident, None)
                else:
                    row['value'] = value
            for key, (value, until) in list(self._pending_controls.items()):
                target = message['state'].setdefault('media' if key == 'mediaVolume' else 'controls', {})
                field = 'volume' if key == 'mediaVolume' else key
                remote = target.get(field, 0)
                if abs(remote - value) < 1 or time.monotonic() > until:
                    self._pending_controls.pop(key, None)
                else:
                    target[field] = value
            for (kind, ident), (value, until) in list(self._pending_audio.items()):
                rows = (
                    message['state']
                    .get('audioMixer', {})
                    .get('applications' if kind == 'sessionVolume' else 'devices', [])
                )
                row = next((r for r in rows if r.get('id') == ident), None)
                if time.monotonic() > until or (row and abs(row.get('volume', 0) - value) < 1):
                    self._pending_audio.pop((kind, ident), None)
                elif row:
                    row['volume'] = value
            audio = message['state'].get('audioMixer', {})
            self.audioApplications.sync(audio.get('applications', []))
            self.audioOutputs.sync([d for d in audio.get('devices', []) if d['flow'] == 0])
            self.audioInputs.sync([d for d in audio.get('devices', []) if d['flow'] == 1])
            dock_changed = any(
                self._state.get(key) != message['state'].get(key) for key in ('taskbarApps', 'runningApps')
            )
            clipboard_changed = self._state.get('clipboard') != message['state'].get('clipboard')
            media_changed = self._state.get('media') != message['state'].get('media')
            # Perf counters change on almost every poll; they have their own signal so the Bridge.changed storm (every binding re-evaluating) only fires for real changes.
            quiet = ('diagnostics', 'performance', 'sysInfo')
            core_changed = any(
                value != self._state.get(key) for key, value in message['state'].items() if key not in quiet
            )
            perf_changed = any(self._state.get(key) != message['state'].get(key) for key in ('performance', 'sysInfo'))
            self._state = message['state']
            self._dark = bool(self._state.get('dark'))
            self._player.data = self._state.get('media', {})
            if core_changed:
                self.changed.emit()
            if perf_changed:
                self.perfChanged.emit()
            if media_changed:
                self._player.changed.emit()
            if catalog_changed:
                self.catalogChanged.emit()
            if dock_changed:
                self.dockChanged.emit()
            if clipboard_changed:
                self.clipboardChanged.emit()
            if catalog_changed and self.mode == 'palette':
                self.searchPalette(self.query)
        elif 'state' in message:
            self._state = message['state']
        if message.get('bookmarksItems'):
            self._bm_items = message['bookmarksItems'].get('items') or []
            self._bm_items_version = message['bookmarksItems'].get('version')
            self.changed.emit()
        bm_version = (self._state.get('bookmarks') or {}).get('version')
        if (
            bm_version is not None
            and bm_version != getattr(self, '_bm_items_version', None)
            and bm_version != getattr(self, '_bm_requested', None)
        ):
            self._bm_requested = bm_version
            self.action('bookmarksItems', {})
        for event in message.get('events', []):
            if self.quiet_on and event.startswith('osd'):
                continue  # quiet mode: no OSD, so nothing takes focus or animates over the game
            if event.startswith('shortcut-key:'):
                if self.recording_kind:
                    _, modifiers, key = event.split(':')
                    self.action('shortcut', {'kind': self.recording_kind, 'key': int(key), 'modifiers': int(modifiers)})
                continue
            if event in ('capture-select', 'capture-ocr'):
                self.capture.start('text' if event == 'capture-ocr' else 'region')
                continue
            if event == 'files':
                self.action('fileResults', {})
                continue
            if event.startswith('dictation:'):
                self.dictation_event(event)
                continue
            if event == 'dock':
                self.force_dock = time.monotonic() + 3
            if event in (
                'toggle',
                'access',
                'media',
                'performance',
                'controls',
                'palette',
                'settings',
                'clipboard',
                'tools',
                'audio',
                'microphone',
                'capture',
                'mixer',
                'close',
                'settings:social',
                'settings:bookmarks',
            ) or event.startswith('workspace:'):
                self.navigate(event)
            else:
                self.shellEvent.emit(event)

    @Slot(float, float, float, float, result=bool)
    def menuOutsideClick(self, x, y, w, h):
        down = bool(user32.GetAsyncKeyState(1) & 0x8000 or user32.GetAsyncKeyState(2) & 0x8000)
        was = getattr(self, '_menu_down', False)
        self._menu_down = down
        p = QCursor.pos()
        return down and not was and not (x <= p.x() < x + w and y <= p.y() < y + h)

    screenFullscreen = Property(bool, lambda s: s._screen.name() in s.fullscreen_names, notify=frameChanged)
    quiet = Property(bool, lambda s: s.quiet_on, notify=frameChanged)
    dockQuiet = Property(bool, lambda s: s.dock_quiet(s._screen), notify=frameChanged)

    def dock_quiet(self, screen):
        # Dock and edge chrome are cut on a fullscreen screen only (per screen), or everywhere when quiet is forced on.
        return self.quiet_on and (self.quiet_forced == 'on' or screen.name() in self.fullscreen_names)

    def quiet_slow(self):
        # Timers drop to their slowest safe pace only when every screen is quiet; the shell shortcut and exit detection are event-driven in the backend.
        return self.quiet_on and (
            self.quiet_forced == 'on' or all(s.name() in self.fullscreen_names for s in QApplication.screens())
        )

    def quiet_apply(self):
        slow = self.quiet_slow()
        if slow != self.client.quiet:
            self.client.quiet = slow
            self.pointer_pace()
        self.setAudioBars(self._bars_want)

    def fullscreen(self, screen=None):
        screen = screen or self._screen
        hwnd = user32.GetForegroundWindow()
        pid = ctypes.c_ulong()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        remembered = getattr(self, '_full_candidates', {})
        if pid.value not in (os.getpid(), self.args.backend_pid) and hwnd:
            info = MonitorInfo()
            info.size = ctypes.sizeof(info)
            if user32.GetMonitorInfoW(user32.MonitorFromWindow(hwnd, 2), ctypes.byref(info)):
                remembered[info.name] = hwnd
                self._full_candidates = remembered
        hwnd = remembered.get(screen.name(), 0)
        if not hwnd or not user32.IsWindow(hwnd) or user32.IsIconic(hwnd) or not user32.IsWindowVisible(hwnd):
            return False
        info = MonitorInfo()
        info.size = ctypes.sizeof(info)
        if (
            not user32.GetMonitorInfoW(user32.MonitorFromWindow(hwnd, 2), ctypes.byref(info))
            or info.name != screen.name()
        ):
            return False
        name = ctypes.create_unicode_buffer(128)
        user32.GetClassNameW(hwnd, name, 128)
        if name.value in ('Progman', 'WorkerW', 'Shell_TrayWnd'):
            return False
        rect = (ctypes.c_long * 4)()
        origin = (ctypes.c_long * 2)(0, 0)
        if not user32.GetClientRect(hwnd, ctypes.byref(rect)) or not user32.ClientToScreen(hwnd, ctypes.byref(origin)):
            return False
        return is_fullscreen(
            user32.GetWindowLongPtrW(hwnd, -16),
            bool(user32.IsZoomed(hwnd)),
            (origin[0], origin[1], origin[0] + rect[2], origin[1] + rect[3]),
            tuple(info.monitor),
        )

    def edge_zone(self, rail, edge, side='right'):
        # Hover zone = displayed bump (content + drawer + chevron, aligned to its screen edge in the fixed-size window), +-16 px vertically, up to the screen edge.
        rg = rail.geometry()
        bw = float(rail.property('bumpWidth') or rg.width())
        bh = float(rail.property('bumpHeight') or rg.height())
        offset = float(rail.property('offset') or 0)
        by = rg.y() + (rg.height() - bh) / 2
        if side == 'left':
            bx = rg.x() - offset + bw
            return QRect(int(edge), int(by) - 16, int(bx - edge) + 17, int(bh) + 32)
        bx = rg.x() + rg.width() - bw + offset
        return QRect(int(bx) - 16, int(by) - 16, int(edge - bx) + 17, int(bh) + 32)

    def social_zone(self, screen_right):
        return self.edge_zone(self.social_win, screen_right, 'right')

    def edge_rail(self, rail, side, point, geometry, full, now):
        # Shared trigger/hide logic of the hidden edge rails: peek at the screen edge, hide 0.5 s after the pointer leaves the displayed bump.
        key = 'social' if side == 'right' else 'bookmarks'
        attr = 'away_' + key
        event = (key + 'Peek', key + 'Hide')
        margin = int(
            self._state.get("settings", {}).get(
                "rightActivationMargin" if side == 'right' else "leftActivationMargin", 2
            )
        )
        rail_h = float(rail.property('bumpHeight') or rail.height() or 300)
        at_edge = point.x() >= geometry.right() - margin if side == 'right' else point.x() <= geometry.left() + margin
        # A neighbouring screen on that side makes the shared edge a pass-through, not a trigger.
        edge_x = geometry.right() if side == 'right' else geometry.left()
        adjacent = any(
            o.geometry() != geometry
            and (o.geometry().left() <= edge_x + 1 if side == 'right' else o.geometry().right() >= edge_x - 1)
            and (o.geometry().left() >= edge_x - 1 if side == 'right' else o.geometry().right() <= edge_x + 1)
            and o.geometry().top() < geometry.bottom()
            and o.geometry().bottom() > geometry.top()
            for o in QApplication.screens()
        )
        trigger = at_edge and not adjacent and abs(point.y() - geometry.center().y()) < rail_h / 2 + 60
        inside = rail.isVisible() and self.edge_zone(
            rail, geometry.right() if side == 'right' else geometry.left(), side
        ).contains(point)
        if trigger and not full:
            self.shellEvent.emit(event[0])
            setattr(self, attr, 0)
        elif inside or not rail.property('shown'):
            setattr(self, attr, 0)
        else:
            if not getattr(self, attr):
                setattr(self, attr, now)
            if now - getattr(self, attr) > 0.5 and not self.args.preview:
                self.shellEvent.emit(event[1])
                setattr(self, attr, 0)

    def poll_pointer(self):
        try:
            self._poll_pointer()
        finally:
            self.pointer_pace()

    def _poll_pointer(self):
        self.poll_file_drag()
        if not self.top:
            return
        if self.modal:
            return
        if self.args.backend_pid and time.monotonic() > self.health_at:
            self.health_at = time.monotonic() + 2
            handle = ctypes.windll.kernel32.OpenProcess(0x1000, False, self.args.backend_pid)
            if not handle:
                QApplication.quit()
                return
            ctypes.windll.kernel32.CloseHandle(handle)
        point = QCursor.pos()
        screen = QApplication.screenAt(point) or QApplication.primaryScreen()
        if screen != self._screen:
            self._screen = screen
            self.geometryChanged.emit()
            self.frameChanged.emit()
            self.changed.emit()
        now = time.monotonic()
        geometry = screen.geometry()
        if self.quiet_slow() and not (
            self.top and self.top.isVisible()
        ):  # quiet everywhere and no panel open: no edge tracking, no peek, no rail
            self.edge_policy.away_since = 0
            self.away_top = self.away_social = self.away_bookmarks = 0
            return
        backend_full = getattr(self, '_backend_fullscreen', False)
        full = screen.name() in self.fullscreen_names if backend_full else self.fullscreen()
        fullscreen_names = (
            self.fullscreen_names
            if backend_full
            else tuple(s.name() for s in QApplication.screens() if self.fullscreen(s))
        )
        if fullscreen_names != self.fullscreen_names:
            self.fullscreen_names = fullscreen_names
            self.frameChanged.emit()
        dock_full = (
            self.dock_quiet(screen) if backend_full else full
        )  # preference off: the dock keeps its normal edge behaviour in fullscreen
        full = full or (self.quiet_on and self.quiet_forced == 'on')
        panel_width = float(self.top.property('panelWidth') or self.top.width())
        panel_height = float(self.top.property('panelHeight') or self.top.height())
        panel_rect = QRect(
            round(self.top.x() + (self.top.width() - panel_width) / 2),
            self.top.y(),
            round(panel_width),
            round(panel_height),
        )
        top_inside = self.top.isVisible() and panel_rect.adjusted(-12, -2, 12, 16).contains(point)
        top_edge = (
            geometry.top()
            <= point.y()
            <= geometry.top() + int(self._state.get("settings", {}).get("topActivationMargin", 2))
            and abs(point.x() - geometry.center().x()) < 460
        )
        if self.fileDragNearTop:
            if self.top.isVisible():
                self.navigate('close')
            self.away_top = 0
        elif top_edge and not full:
            self.shellEvent.emit('peek')
            self.away_top = 0
        elif top_inside:
            self.away_top = 0
            self.top_seen = True
        elif self.top.isVisible() and panel_height < 95 and now > self.opening_until:
            if not self.away_top:
                self.away_top = now
            if now - self.away_top > 0.55 and not self.args.preview:
                self.navigate('close')
        if self.social_win and self.socialRailVisible:
            self.edge_rail(self.social_win, 'right', point, geometry, full, now)
        else:
            self.away_social = 0
        if self.bookmarks_win and self.bookmarksRailVisible:
            self.edge_rail(self.bookmarks_win, 'left', point, geometry, full, now)
        else:
            self.away_bookmarks = 0
        if self.dock and not self.dockVisible:
            self.edge_policy.away_since = 0
            self.client.subscription['dock'] = False  # dock off: no bottom-edge tracking, nothing subscribed
        elif self.dock and self.dockVisible and dock_full:
            self.edge_policy.away_since = (
                0  # fullscreen: chrome stays hidden, no reveal and no timers; idle state resumes on exit
            )
        elif self.dock and self.dockVisible:
            self.client.subscription['dock'] = bool(self.dock.property('shown'))
            dock_inside = self.dock.isVisible() and self.dock.geometry().adjusted(-8, -10, 8, 4).contains(point)
            menu_open = bool(self.dock.property('menuOpen'))
            if menu_open:
                dock_inside = True
            bottom = point.y() >= geometry.bottom() - 7
            decision = self.edge_policy.update(now, bottom, dock_inside, dock_full, now < self.force_dock)
            if decision is True:
                self.shellEvent.emit('dock')
            elif decision is False and not self.args.preview:
                self.shellEvent.emit('dockHide')

    def apply_surface_visibility(self, *args):
        # A surface switched off in the settings is hidden for real (not just transparent) and its cached input mask is dropped, so re-enabling re-applies it.
        if not self._state.get('settings'):
            return  # no snapshot yet: never hide a surface on defaults
        flags = (self.dockVisible, self.socialRailVisible, self.bookmarksRailVisible)
        if flags == getattr(self, '_surface_flags', None):
            return
        self._surface_flags = flags
        for win, on in zip((self.dock, self.social_win, self.bookmarks_win), flags):
            if win is None or on:
                continue
            if win.isVisible():
                win.setVisible(False)
            win.setMask(QRegion())
            if self.surface_scene:
                self.surface_scene.input_masks.pop(int(win.winId()), None)

    def pointer_pace(self):
        if self.quiet_slow() and not (self.top and self.top.isVisible()):
            if self.pointer.interval() != 500:
                self.pointer.setInterval(500)
            return
        point = QCursor.pos()
        geometry = (QApplication.screenAt(point) or QApplication.primaryScreen()).geometry()
        now = time.monotonic()
        # Adaptive pace: 90 ms while a reveal is active, the button is down (file drag) or the pointer is within 48 px of a screen edge; 250 ms otherwise.
        # Only edges that host a visible surface count: the top panel trigger always does, the bottom dock and the side rails only while enabled.
        edges = [point.y() - geometry.top()]
        if self.dockVisible:
            edges.append(geometry.bottom() - point.y())
        if self.bookmarksRailVisible:
            edges.append(point.x() - geometry.left())
        if self.socialRailVisible:
            edges.append(geometry.right() - point.x())
        near = min(edges) <= 48
        active = (
            near
            or self.away_top
            or self.away_social
            or self.away_bookmarks
            or now < self.force_dock
            or getattr(self, '_file_drag_down', False)
            or bool(user32.GetAsyncKeyState(1) & 0x8000)
        )
        if self.top and self.top.isVisible() and float(self.top.property('panelHeight') or 0) >= 95:
            active = True  # an open panel; the collapsed bar is covered by away_top / edge proximity
        for win, on in (
            (self.dock, self.dockVisible),
            (self.social_win, self.socialRailVisible),
            (self.bookmarks_win, self.bookmarksRailVisible),
        ):
            if win and on and (win.property('shown') or win.property('menuOpen')):
                active = True
        interval = 90 if active else 250
        if self.pointer.interval() != interval:
            self.pointer.setInterval(interval)

    def stop(self):
        if getattr(self, '_click_picker', None):
            self._click_picker.stop()
        if getattr(self, 'glass', None):
            self.glass.stop()
        if self.surface_scene:
            self.surface_scene.stop()
        self.walltheme.stop()
        self.pointer.stop()
        self.client.stop()
        self.client.wait(1500)
