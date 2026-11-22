"""Sources de contexte des Winjets : `system.disk`, `system.diskMain`, `weather.*`, `files.recent`, `files.folders`, et les
actions `open` / `openDrive`.

Les fonctions de calcul sont pures (testées sans Qt ni réseau). `WinjetFeeds` (exposé en `Bridge.winjets.feeds`) tient des
compteurs de références : un minuteur ne tourne que tant qu'une carte visible lit la source (aucun minuteur au repos).
"""
from __future__ import annotations

import ctypes
import json
import os
import re
import shutil
import threading
import time
import urllib.parse
import urllib.request
import uuid
from datetime import date, datetime
from pathlib import Path

from PySide6.QtCore import Property, QObject, QTimer, QUrl, Signal, Slot

DISK_POLL_MS = 30_000
WEATHER_REFRESH_S = 15 * 60
LOCATE_WAIT_MS = 8_000
RECENT_MAX = 8
GB = 1024**3
FOLDERS = (
    ('Desktop', 'B4BFCC3A-DB2C-424C-B029-7FE99A87C641'),
    ('Downloads', '374DE290-123F-4565-9164-39C4925E467B'),
    ('Documents', 'FDD39AD0-238F-46AF-ADB4-6C85480369C7'),
    ('Pictures', '33E28130-4E1E-4676-835A-98395C3BC3BB'),
)
FOLDER_ICONS = {'Desktop': 'desktop_windows', 'Downloads': 'download', 'Documents': 'description', 'Pictures': 'image'}
FILE_ICONS = (
    (('.png', '.jpg', '.jpeg', '.gif', '.bmp', '.webp', '.svg', '.heic'), 'image'),
    (('.mp3', '.wav', '.flac', '.m4a', '.ogg', '.aac'), 'music_note'),
    (('.mp4', '.mkv', '.mov', '.avi', '.webm'), 'movie'),
    (('.pdf',), 'picture_as_pdf'),
    (('.zip', '.rar', '.7z', '.tar', '.gz'), 'folder_zip'),
    (('.xls', '.xlsx', '.csv'), 'table_chart'),
    (('.py', '.js', '.ts', '.cs', '.json', '.html', '.css', '.qml', '.cpp', '.h'), 'code'),
)
APP_EXTENSIONS = ('.exe', '.lnk', '.bat', '.cmd', '.msi', '.appref-ms')


# --- Disques ------------------------------------------------------------------------------------------------------------


def fixed_drives():
    """[(« C: », étiquette)] des disques fixes (GetLogicalDrives, GetDriveTypeW = DRIVE_FIXED, GetVolumeInformationW)."""
    try:
        kernel = ctypes.windll.kernel32
        mask = kernel.GetLogicalDrives()
    except Exception:
        return []
    out = []
    for index in range(26):
        if not mask & (1 << index):
            continue
        root = f'{chr(65 + index)}:\\'
        if kernel.GetDriveTypeW(root) != 3:
            continue
        label = ctypes.create_unicode_buffer(261)
        if not kernel.GetVolumeInformationW(root, label, 261, None, None, None, None, 0):
            label.value = ''
        out.append((root[:2], label.value))
    return out


def disk_feed(drives=None, usage=shutil.disk_usage):
    """Liste des disques fixes : {drive, label, usedGb, totalGb, freeGb, percent, ratio}. Un disque illisible est ignoré."""
    out = []
    for drive, label in fixed_drives() if drives is None else drives:
        try:
            total, used, free = usage(drive + '\\')
        except OSError:
            continue
        if total <= 0:
            continue
        out.append({
            'drive': drive, 'label': label or '',
            'usedGb': round(used / GB, 1), 'totalGb': round(total / GB, 1), 'freeGb': round(free / GB, 1),
            'percent': int(round(used * 100 / total)), 'ratio': round(used / total, 3),
        })
    return out


def main_disk(disks, system_drive=None):
    """Le disque système (SystemDrive), sinon le premier ; {} sans disque."""
    system_drive = (system_drive or os.environ.get('SystemDrive') or 'C:').upper()
    return next((d for d in disks if d['drive'].upper() == system_drive), disks[0] if disks else {})


# --- Fichiers -----------------------------------------------------------------------------------------------------------


def known_folders(resolve=None):
    """[{name, path}] Desktop, Downloads, Documents, Pictures (SHGetKnownFolderPath, repli : dossier utilisateur)."""
    resolve = resolve or _known_folder
    out = []
    for name, guid in FOLDERS:
        path = resolve(guid) or str(Path.home() / name)
        out.append({'name': name, 'path': path, 'icon': FOLDER_ICONS.get(name, 'folder')})
    return out


def _known_folder(guid):
    try:
        raw = uuid.UUID(guid).bytes_le
        ident = (ctypes.c_ubyte * 16).from_buffer_copy(raw)
        pointer = ctypes.c_wchar_p()
        if ctypes.windll.shell32.SHGetKnownFolderPath(ctypes.byref(ident), 0, None, ctypes.byref(pointer)) != 0:
            return ''
        try:
            return pointer.value or ''
        finally:
            ctypes.windll.ole32.CoTaskMemFree(pointer)
    except Exception:
        return ''


def _file_icon(path, kind):
    """Glyphe Material Symbols du type de fichier (dossier, application, puis extension)."""
    if kind == 'folder':
        return 'folder'
    if kind == 'app':
        return 'apps'
    lower = path.lower()
    for extensions, glyph in FILE_ICONS:
        if lower.endswith(extensions):
            return glyph
    return 'description'


def _modified(path):
    """Date de modification (secondes epoch) ou 0 si le fichier est illisible ; sert aux dates relatives."""
    try:
        return int(os.stat(path).st_mtime)
    except OSError:
        return 0


def recent_files(entries, limit=RECENT_MAX):
    """`files.recent` : {name, path, kind} depuis systemRecent (chemin dans `comment`) ; kind = folder|app|file."""
    out = []
    for entry in entries or []:
        if not isinstance(entry, dict):
            continue
        path = str(entry.get('comment') or entry.get('path') or '')
        if not path or is_url(path) or not os.path.isabs(path):
            continue
        if entry.get('icon') == 'folder':
            kind = 'folder'
        elif path.lower().endswith(APP_EXTENSIONS):
            kind = 'app'
        else:
            kind = 'file'
        out.append({'name': str(entry.get('name') or Path(path).name), 'path': path, 'kind': kind,
                    'icon': _file_icon(path, kind), 'modified': _modified(path)})
        if len(out) >= limit:
            break
    return out


_URL = re.compile(r'^[A-Za-z][A-Za-z0-9+.-]*:(//|\\\\)|^(https?|file|ftp|mailto|javascript|ms-[a-z]+):', re.I)


def is_url(text):
    text = str(text or '').strip()
    return bool(_URL.match(text)) or text.startswith('\\\\')  # URL, ou chemin réseau UNC


def is_builtin_package(package_id):
    """Fonctions sensibles (`open`, `files.*`) : réservées aux paquets intégrés (id « winlestia.* »)."""
    return str(package_id or '').startswith('winlestia.')


def open_allowed(path, allowed):
    """`open` : jamais d'URL ; seulement un chemin publié par `files.recent` ou `files.folders`."""
    path = str(path or '').strip()
    return bool(path) and not is_url(path) and path in allowed


def drive_valid(drive, drives):
    drive = str(drive or '').strip()
    return bool(re.fullmatch(r'[A-Za-z]:', drive)) and drive.upper() in {d.upper() for d in drives}


def _shell_open(path):
    from PySide6.QtGui import QDesktopServices

    return QDesktopServices.openUrl(QUrl.fromLocalFile(path))


# --- Météo --------------------------------------------------------------------------------------------------------------


def icon_for(code):
    """Icône (mêmes noms que la météo du shell) d'un code WMO."""
    return (
        'sunny' if code == 0
        else 'partly_cloudy_day' if code < 3
        else 'cloud' if code == 3
        else 'foggy' if code < 50
        else 'rainy' if code < 70
        else 'weather_snowy' if code < 80
        else 'thunderstorm' if code >= 95
        else 'rainy'
    )


_CONDITIONS = {
    'en': {'sunny': 'Clear', 'partly_cloudy_day': 'Partly cloudy', 'cloud': 'Cloudy', 'foggy': 'Fog', 'rainy': 'Rain',
           'weather_snowy': 'Snow', 'thunderstorm': 'Thunderstorm'},
    'fr': {'sunny': 'Dégagé', 'partly_cloudy_day': 'Peu nuageux', 'cloud': 'Nuageux', 'foggy': 'Brouillard', 'rainy': 'Pluie',
           'weather_snowy': 'Neige', 'thunderstorm': 'Orage'},
    'es': {'sunny': 'Despejado', 'partly_cloudy_day': 'Poco nuboso', 'cloud': 'Nublado', 'foggy': 'Niebla', 'rainy': 'Lluvia',
           'weather_snowy': 'Nieve', 'thunderstorm': 'Tormenta'},
    'ru': {'sunny': 'Ясно', 'partly_cloudy_day': 'Малооблачно', 'cloud': 'Облачно', 'foggy': 'Туман', 'rainy': 'Дождь',
           'weather_snowy': 'Снег', 'thunderstorm': 'Гроза'},
}
_DAYS = {
    'en': ('Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'),
    'fr': ('Lun', 'Mar', 'Mer', 'Jeu', 'Ven', 'Sam', 'Dim'),
    'es': ('Lun', 'Mar', 'Mié', 'Jue', 'Vie', 'Sáb', 'Dom'),
    'ru': ('Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс'),
}
_CURRENT_LOCATION = {'en': 'Current location', 'fr': 'Position actuelle', 'es': 'Ubicación actual', 'ru': 'Текущее местоположение'}


def weather_status(status, source, city=''):
    return {'status': status, 'source': source, 'city': city, 'temp': None, 'feelsLike': None, 'condition': '', 'icon': '',
            'high': None, 'low': None, 'daily': []}


def weather_payload(forecast, city, source, lang='en'):
    """Réponse Open-Meteo (current + daily) -> `weather.*` du contrat ; lève KeyError/ValueError si la réponse est inutilisable."""
    lang = lang if lang in _CONDITIONS else 'en'
    current = forecast['current']
    daily = forecast.get('daily') or {}
    icon = icon_for(int(current['weather_code']))
    days = []
    times = daily.get('time') or []
    for i, stamp in enumerate(times[:7]):
        moment = date.fromisoformat(str(stamp)[:10])
        days.append({
            'day': _DAYS[lang][moment.weekday()],
            'icon': icon_for(int((daily.get('weather_code') or [0] * 7)[i])),
            'max': round(daily['temperature_2m_max'][i]), 'min': round(daily['temperature_2m_min'][i]),
        })
    # Échelle de la semaine (min/max globaux) : chaque jour porte sa plage en fraction 0..1 pour la barre de plage.
    week_low = min((d['min'] for d in days), default=0)
    week_high = max((d['max'] for d in days), default=0)
    scale = (week_high - week_low) or 1
    for d in days:
        d['rangeFrom'] = round((d['min'] - week_low) / scale, 3)
        d['rangeTo'] = round((d['max'] - week_low) / scale, 3)
    return {
        'status': 'ok', 'source': source, 'city': city, 'weekLow': week_low, 'weekHigh': week_high,
        'temp': round(current['temperature_2m']),
        'feelsLike': round(current.get('apparent_temperature', current['temperature_2m'])),
        'condition': _CONDITIONS[lang][icon], 'icon': icon,
        'high': days[0]['max'] if days else None, 'low': days[0]['min'] if days else None, 'daily': days,
    }


def location_label(lat, lon, global_geo, name='', lang='en'):
    """Nom d'une position : `name` du backend, sinon la ville globale si ses coordonnées coïncident, sinon « Current location »."""
    if name:
        return str(name)
    if global_geo and abs(global_geo[0] - lat) < 0.5 and abs(global_geo[1] - lon) < 0.5:
        return global_geo[2]
    return _CURRENT_LOCATION.get(lang, _CURRENT_LOCATION['en'])


def fetch_json(url):
    with urllib.request.urlopen(url, timeout=8) as response:
        return json.load(response)


def geocode(city):
    """(lat, lon, nom) de la première correspondance Open-Meteo, ou None."""
    results = fetch_json(
        'https://geocoding-api.open-meteo.com/v1/search?' + urllib.parse.urlencode({'name': city, 'count': 1, 'language': 'en'})
    ).get('results') or []
    if not results:
        return None
    return float(results[0]['latitude']), float(results[0]['longitude']), str(results[0].get('name') or city)


def fetch_forecast(lat, lon):
    return fetch_json(
        'https://api.open-meteo.com/v1/forecast?'
        + urllib.parse.urlencode({
            'latitude': lat, 'longitude': lon, 'timezone': 'auto', 'forecast_days': 7,
            'current': 'temperature_2m,apparent_temperature,weather_code',
            'daily': 'weather_code,temperature_2m_max,temperature_2m_min',
        })
    )


def compute_weather(key, mode, location, global_city, lang='en'):
    """Travail réseau d'une clé (ville de l'instance, '' = auto). mode = 'city' ou 'location'. Renvoie un dict `weather.*`."""
    try:
        if mode == 'location':
            lat, lon = float(location['lat']), float(location['lon'])
            geo = geocode(global_city) if global_city else None
            label = location_label(lat, lon, geo, location.get('name') or '', lang)
            return weather_payload(fetch_forecast(lat, lon), label, 'location', lang)
        geo = geocode(key or global_city)
        if geo is None:
            return weather_status('error', 'city', key or global_city)
        return weather_payload(fetch_forecast(geo[0], geo[1]), geo[2], 'city', lang)
    except Exception:
        return weather_status('error', 'location' if mode == 'location' else 'city', key or global_city)


# --- Pont QML -----------------------------------------------------------------------------------------------------------


class WinjetFeeds(QObject):
    changed = Signal()
    _weatherDone = Signal(str, dict)

    def __init__(self, bridge):
        super().__init__(bridge)
        self.bridge = bridge
        self._revision = 0
        self._holds = {'disk': 0, 'files': 0}
        self._weather_holds = {}
        self._weather = {}
        self._weather_at = {}
        self._busy = set()
        self._disk = []
        self._recent = []
        self._folders = []
        self._allowed = set()
        self._disk_timer = QTimer(self)
        self._disk_timer.setInterval(DISK_POLL_MS)
        self._disk_timer.timeout.connect(self._poll_disk)
        self._weather_timer = QTimer(self)
        self._weather_timer.setInterval(WEATHER_REFRESH_S * 1000)
        self._weather_timer.timeout.connect(self._weather_tick)
        self._locate_timer = QTimer(self)
        self._locate_timer.setSingleShot(True)
        self._locate_timer.setInterval(LOCATE_WAIT_MS)
        self._locate_timer.timeout.connect(self._locate_timeout)
        self._located = None  # dernier état `location` vu
        self._weatherDone.connect(self._weather_ready)
        if hasattr(bridge, 'changed'):
            bridge.changed.connect(self._bridge_changed)

    revision = Property(int, lambda s: s._revision, notify=changed)
    disk = Property('QVariantList', lambda s: list(s._disk), notify=changed)
    diskMain = Property('QVariantMap', lambda s: main_disk(s._disk), notify=changed)
    filesRecent = Property('QVariantList', lambda s: list(s._recent), notify=changed)
    filesFolders = Property('QVariantList', lambda s: list(s._folders), notify=changed)

    def _bump(self):
        self._revision += 1
        self.changed.emit()

    # Références ---------------------------------------------------------------------------------------------------------

    @Slot(str, str)
    def hold(self, feed, arg=''):
        if feed == 'weather':
            key = str(arg or '').strip()
            self._weather_holds[key] = self._weather_holds.get(key, 0) + 1
            if self._weather_holds[key] == 1:
                self._weather_timer.start()
                self._start_weather(key)
            return
        if feed not in self._holds:
            return
        self._holds[feed] += 1
        if self._holds[feed] > 1:
            return
        if feed == 'disk':
            self._poll_disk()
            self._disk_timer.start()
        else:
            self._refresh_files(force=True)

    @Slot(str, str)
    def release(self, feed, arg=''):
        if feed == 'weather':
            key = str(arg or '').strip()
            count = self._weather_holds.get(key, 0) - 1
            if count > 0:
                self._weather_holds[key] = count
                return
            self._weather_holds.pop(key, None)
            if not self._weather_holds:
                self._weather_timer.stop()
                self._locate_timer.stop()
            return
        if feed in self._holds and self._holds[feed] > 0:
            self._holds[feed] -= 1
            if feed == 'disk' and self._holds[feed] == 0:
                self._disk_timer.stop()

    # Disques et fichiers ------------------------------------------------------------------------------------------------

    def _poll_disk(self):
        disks = disk_feed()
        if disks != self._disk:
            self._disk = disks
            self._bump()

    def _refresh_files(self, force=False):
        recent = recent_files((getattr(self.bridge, '_state', None) or {}).get('systemRecent'))
        folders = self._folders if self._folders and not force else known_folders()
        if recent != self._recent or folders != self._folders:
            self._recent, self._folders = recent, folders
            self._allowed = {e['path'] for e in recent} | {f['path'] for f in folders}
            self._bump()

    def _bridge_changed(self):
        if self._holds['files']:
            self._refresh_files()
        if '' in self._weather_holds and self._weather.get('', {}).get('status') == 'locating':
            self._start_weather('')

    # Actions ------------------------------------------------------------------------------------------------------------

    @Slot(str, result=bool)
    def openPath(self, path):
        """Action `open` : refuse les URL et tout chemin qui ne vient pas de `files.recent` / `files.folders`."""
        if not self._allowed:
            self._refresh_files(force=True)
        path = str(path or '').strip()
        if not open_allowed(path, self._allowed) or not os.path.exists(path):
            return False
        return bool(_shell_open(path))

    @Slot(str, str, result=bool)
    def openPathFor(self, package_id, path):
        """Action `open` d'une carte : refusée pour tout paquet qui n'est pas intégré."""
        return is_builtin_package(package_id) and self.openPath(path)

    @Slot(str, result=bool)
    def openDrive(self, drive):
        """Action `openDrive` : « C: » d'un disque fixe, ouvert dans l'explorateur."""
        drives = [d for d, _ in fixed_drives()]
        if not drive_valid(drive, drives):
            return False
        return bool(_shell_open(str(drive).strip().upper() + '\\'))

    # Météo --------------------------------------------------------------------------------------------------------------

    @Slot(str, result='QVariantMap')
    def weatherFor(self, city):
        return dict(self._weather.get(str(city or '').strip()) or weather_status('unconfigured', 'city'))

    def _global_city(self):
        return str(getattr(self.bridge, 'city', '') or '').strip()

    def _lang(self):
        language = str(getattr(self.bridge, 'language', '') or '')[:2]
        return language if language in _CONDITIONS else 'en'

    def _set_weather(self, key, payload):
        if self._weather.get(key) != payload:
            self._weather[key] = payload
            self._bump()

    def _start_weather(self, key, force=False):
        if key in self._busy:
            return
        recent = time.monotonic() - self._weather_at.get(key, -WEATHER_REFRESH_S) < WEATHER_REFRESH_S
        if recent and not force and self._weather.get(key, {}).get('status') == 'ok':
            return
        if key:
            return self._spawn(key, 'city', None)
        location = (getattr(self.bridge, '_state', None) or {}).get('location')
        if isinstance(location, dict) and location.get('ok') and 'lat' in location and 'lon' in location:
            self._locate_timer.stop()
            return self._spawn(key, 'location', location)
        if isinstance(location, dict) and location.get('ok') is False and location.get('requested') is True:
            return self._fallback(key)
        if self._weather.get(key, {}).get('status') != 'locating':
            self._set_weather(key, weather_status('locating', 'location'))
            self.bridge.action('locate', {})
            self._locate_timer.start()

    def _locate_timeout(self):
        if '' in self._weather_holds and self._weather.get('', {}).get('status') == 'locating':
            self._fallback('')

    def _fallback(self, key):
        self._locate_timer.stop()
        if self._global_city():
            self._spawn(key, 'city', None)
        else:
            self._set_weather(key, weather_status('unconfigured', 'city'))

    def _spawn(self, key, mode, location):
        self._busy.add(key)
        glob, lang = self._global_city(), self._lang()

        def work():
            self._weatherDone.emit(key, compute_weather(key, mode, location, glob, lang))

        threading.Thread(target=work, daemon=True).start()

    @Slot(str, dict)
    def _weather_ready(self, key, payload):
        self._busy.discard(key)
        self._weather_at[key] = time.monotonic()
        if key in self._weather_holds or key in self._weather:
            self._set_weather(key, payload)

    def _weather_tick(self):
        for key in list(self._weather_holds):
            self._start_weather(key, force=True)
