"""Bridge QML : état de l'interface, actions et slots exposés au QML.

Les signaux et toutes les Property à notification restent ici : PySide ne relie un signal de
notification que s'il est défini dans la même classe. Les slots et la logique par domaine vivent
dans les mixins bridge_*.py.
"""
from __future__ import annotations
import ctypes
import json
from PySide6.QtCore import Property, QObject, QTimer, Signal, Slot
from PySide6.QtGui import QCursor
from PySide6.QtWidgets import QApplication
import app_state
from app_state import CONFIG, ROOT
from bridge_actions import ActionMixin
from bridge_assets import AssetMixin
from bridge_client import PipeClient
from bridge_dictation import DictationMixin
from bridge_palette import PaletteMixin
from bridge_pointer import PointerMixin
from bridge_shortcuts import ShortcutMixin
from bridge_state import StateMixin
from bridge_theme import ThemeMixin
from bridge_wallpaper import WallpaperMixin
from bridge_weather import WeatherMixin
from capture_native import CaptureController
from custom_palette import make_palette
from interaction import EdgePolicy
from live_model import LiveListModel
from wallpaper_theme import WallpaperTheme
from win_native import user32
from windows_player import WindowsPlayer


class Bridge(
    QObject,
    ThemeMixin,
    PaletteMixin,
    WallpaperMixin,
    ShortcutMixin,
    ActionMixin,
    AssetMixin,
    WeatherMixin,
    DictationMixin,
    StateMixin,
    PointerMixin,
):
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

    # Sources de la palette : fenêtres ouvertes (demandées à l'ouverture, oubliées à la fermeture) et index de
    # fichiers (temporisé, la requête la plus récente gagne grâce au numéro de génération).
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

    _theme_pending = None

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

    # Thème auto, schéma sombre sur un fond clair : le texte pâle demande une teinte plus dense pour rester lisible.
    # Les fonds à L* 40 ou moins s'affichent comme avant.
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

    fileDragChanged = Signal()
    fileDragActive = Property(bool, lambda s: getattr(s, '_file_drag_active', False), notify=fileDragChanged)
    fileDragNearTop = Property(bool, lambda s: getattr(s, '_file_drag_near', False), notify=fileDragChanged)

    frameThickness = Property(int, lambda s: max(4, min(40, int(s.settings.get('frameThickness', 16)))), notify=changed)

    frameEnabled = Property(bool, lambda s: bool(s._state.get('settings', {}).get('screenFrame', True)), notify=changed)
    frameScreens = Property('QVariantList', lambda s: s.frame_screens(), notify=screensChanged)

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

    # Visibilité des surfaces : un dock ou un rail masqué n'est ni sondé, ni affiché, et ne garde aucun masque de
    # saisie.
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

    def stop(self):
        self.stopWallpaperThumbs()
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
