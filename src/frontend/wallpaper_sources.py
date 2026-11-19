# Python 3.12 / wallpaper_sources.py / Windows. Découverte en lecture seule du fond réellement affiché et d'images
# représentatives de ce fond.
# Ordre de résolution : hôte animé en cours avec lecteur de config (WallpaperEngine, Lively, DesktopHut, WallpaperX)
# -> hôte animé générique (capture) -> Windows (Spotlight / Diaporama / image).
# Vérifié sur la machine de dev : DesktopHut, Windows. NON vérifié ici (dispositions documentées seulement) :
# WallpaperEngine, Lively, contenu WallpaperX, Spotlight.
from dataclasses import dataclass, field, replace
from pathlib import Path
import ctypes, glob, hashlib, json, os, re, shutil, subprocess, sys, time

VIDEO_EXT = {'.mp4', '.webm', '.mkv', '.mov', '.avi', '.m4v', '.wmv'}
FRAME_COUNT = 8
FRAME_SIZE = 320
CACHE_LIMIT = 30
EXTRACT_TIMEOUT = 5.0


@dataclass
class WallpaperSource:
    provider: str
    key: str
    images: list = field(default_factory=list)
    video: str = None
    label: str = ''
    display: str = ''
    modified: int = 0
    needs_capture: bool = False
    thumbnail: str = ''
    # rect (x,y,l,h) : rectangle du moniteur, renseigné quand il y a plusieurs moniteurs et que la source ne les
    # couvre pas tous
    rect: tuple = None


def default_bases():
    # Dossiers de base, tous remplaçables (les tests les pointent vers un faux arbre).
    steam = ''
    try:
        import winreg

        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\Valve\Steam') as k:
            steam = winreg.QueryValueEx(k, 'SteamPath')[0]
    except Exception:
        # Steam absent : pas de dossier de fonds Steam.
        pass
    return {
        'localappdata': os.environ.get('LOCALAPPDATA', ''),
        'appdata': os.environ.get('APPDATA', ''),
        'programdata': os.environ.get('PROGRAMDATA', ''),
        'steam': steam,
    }


def process_names():
    # Un instantané Toolhelp par appel ; les appelants ne l'appellent qu'une fois par événement de changement.
    class ProcessEntry(ctypes.Structure):
        _fields_ = [
            ('size', ctypes.c_ulong),
            ('usage', ctypes.c_ulong),
            ('pid', ctypes.c_ulong),
            ('heap', ctypes.c_size_t),
            ('module', ctypes.c_ulong),
            ('threads', ctypes.c_ulong),
            ('parent', ctypes.c_ulong),
            ('priority', ctypes.c_long),
            ('flags', ctypes.c_ulong),
            ('exe', ctypes.c_wchar * 260),
        ]

    kernel = ctypes.windll.kernel32
    kernel.CreateToolhelp32Snapshot.restype = ctypes.c_void_p
    kernel.Process32FirstW.argtypes = [ctypes.c_void_p, ctypes.POINTER(ProcessEntry)]
    kernel.Process32NextW.argtypes = kernel.Process32FirstW.argtypes
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    handle = kernel.CreateToolhelp32Snapshot(2, 0)
    if handle in (None, ctypes.c_void_p(-1).value):
        return set()
    names = set()
    entry = ProcessEntry()
    entry.size = ctypes.sizeof(entry)
    try:
        ok = kernel.Process32FirstW(handle, ctypes.byref(entry))
        while ok:
            names.add(entry.exe.lower())
            ok = kernel.Process32NextW(handle, ctypes.byref(entry))
    finally:
        kernel.CloseHandle(handle)
    return names


def live_host_present():
    # Vrai quand une fenêtre enfant de Progman / WorkerW (hors vue des icônes) existe : c'est un hôte de fond animé
    # (24H2 : enfant de Progman ; avant : frère WorkerW).
    user = ctypes.windll.user32
    found = [False]
    proc = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)

    def cls(h):
        b = ctypes.create_unicode_buffer(128)
        user.GetClassNameW(ctypes.c_void_p(h), b, 128)
        return b.value

    def scan(parent, depth):
        def each(h, _):
            name = cls(h)
            if name == 'SHELLDLL_DefView':
                return True
            if name == 'WorkerW':
                if depth < 3:
                    scan(h, depth + 1)
                return True
            if user.IsWindowVisible(ctypes.c_void_p(h)):
                found[0] = True
                return False
            return True

        user.EnumChildWindows(ctypes.c_void_p(parent), proc(each), None)

    user.FindWindowW.restype = ctypes.c_void_p
    progman = user.FindWindowW('Progman', None)
    if progman:
        scan(progman, 0)
    if found[0]:
        return True

    def top(h, _):
        if cls(h) == 'WorkerW':
            scan(h, 0)
        return not found[0]

    user.EnumWindows(proc(top), None)
    return found[0]


def _json(path):
    try:
        return json.loads(Path(path).read_text(encoding='utf-8-sig'))
    except (OSError, ValueError):
        return None


def _mtime(path):
    try:
        return Path(path).stat().st_mtime_ns
    except OSError:
        return 0


def _is_video(path):
    return Path(path).suffix.lower() in VIDEO_EXT


def _sha(text):
    return hashlib.sha1(text.encode('utf-8', 'replace')).hexdigest()


def desktophut_configs(bases):
    out = sorted(
        glob.glob(
            str(
                Path(bases['localappdata'])
                / 'Packages'
                / 'Desktophut.*'
                / 'LocalCache'
                / 'Roaming'
                / 'motion-photo-editor'
                / 'config.json'
            )
        )
    )
    out.append(str(Path(bases['appdata']) / 'motion-photo-editor' / 'config.json'))
    return [Path(p) for p in out if os.path.isfile(p)]


def read_desktophut(bases, names):
    if 'desktophut studio.exe' not in names:
        return {}
    result = {}
    for config in desktophut_configs(bases):
        data = _json(config)
        if not isinstance(data, dict):
            continue
        if (data.get('settings') or {}).get('engine') == 'mpv' and 'mpv.exe' not in names:
            continue
        library = {w.get('id'): w for w in data.get('wallpaperLibrary', []) if isinstance(w, dict)}
        for display, identity in (data.get('activeWallpapers') or {}).items():
            item = library.get(identity)
            if not item:
                continue
            thumb = config.parent / 'thumbnails' / Path(item.get('thumbnail', '') or 'x').name
            if not thumb.is_file():
                thumb = Path(item.get('thumbnail', ''))
            video = Path(item.get('path', ''))
            if not video.is_file():
                video = config.parent / 'videos' / Path(item.get('path', '') or item.get('name', 'x')).name
            thumb_ok = thumb.is_file()
            video_ok = video.is_file()
            if not (thumb_ok or video_ok):
                continue
            result[str(display)] = WallpaperSource(
                'DesktopHut',
                f'desktophut:{identity}',
                [str(thumb)] if thumb_ok else [],
                str(video) if video_ok else None,
                item.get('title') or video.name,
                str(display),
                max(_mtime(video), _mtime(thumb)),
                thumbnail=str(thumb) if thumb_ok else '',
            )
    return result


def steam_libraries(bases):
    root = bases.get('steam') or ''
    if not root:
        return []
    libs = [root]
    try:
        text = (Path(root) / 'steamapps' / 'libraryfolders.vdf').read_text(encoding='utf-8', errors='replace')
        libs += [m.replace('\\\\', '\\') for m in re.findall(r'"path"\s+"([^"]+)"', text)]
    except OSError:
        # libraryfolders.vdf absent : seule la bibliothèque principale compte.
        pass
    seen = []
    [seen.append(l) for l in libs if l not in seen]
    return seen


def wallpaper_engine_configs(bases):
    return [
        Path(l) / 'steamapps' / 'common' / 'wallpaper_engine' / 'config.json'
        for l in steam_libraries(bases)
        if (Path(l) / 'steamapps' / 'common' / 'wallpaper_engine' / 'config.json').is_file()
    ]


def read_wallpaper_engine(bases, names, monitors):
    if not ({'wallpaper32.exe', 'wallpaper64.exe'} & names):
        return {}
    result = {}
    for config in wallpaper_engine_configs(bases):
        data = _json(config)
        if not isinstance(data, dict):
            continue
        for user in data.values():
            selected = (
                ((user.get('wallpaperconfig') or {}).get('selectedwallpapers')) if isinstance(user, dict) else None
            )
            if not isinstance(selected, dict):
                continue
            for name, entry in selected.items():
                m = re.fullmatch(r'monitor(\d+)', name, re.I)
                file = entry.get('file') if isinstance(entry, dict) else None
                if not file:
                    continue
                if m:
                    index = int(m.group(1))
                    display = monitors[index].get('name') if index < len(monitors) else name
                elif name.lower() in ('default', 'all', 'span'):
                    display = 'span'
                else:
                    continue
                result[display] = _we_source(Path(file), display)
            if result:
                return result
    return result


def _we_source(file, display):
    project = file.parent
    info = _json(project / 'project.json') if (project / 'project.json').is_file() else None
    images = []
    video = None
    label = file.name
    if isinstance(info, dict):
        label = info.get('title') or label
        preview = info.get('preview')
        if preview and (project / preview).is_file():
            images = [str(project / preview)]
        target = project / info['file'] if info.get('file') else None
        if target and target.suffix.lower() in VIDEO_EXT and target.is_file():
            video = str(target)
    if video is None and file.suffix.lower() in VIDEO_EXT and file.is_file():
        video = str(file)
    if not images and file.suffix.lower() in {'.jpg', '.jpeg', '.png', '.bmp', '.webp'} and file.is_file():
        images = [str(file)]
    return WallpaperSource(
        'WallpaperEngine',
        'we:' + str(file).lower(),
        images,
        video,
        label,
        display,
        _mtime(images[0] if images else video or file),
        thumbnail=images[0] if images else '',
    )


def lively_dirs(bases):
    dirs = [Path(bases['localappdata']) / 'Lively Wallpaper']
    dirs += [
        Path(p)
        for p in sorted(
            glob.glob(
                str(
                    Path(bases['localappdata'])
                    / 'Packages'
                    / '12030rocksdanister.LivelyWallpaper_*'
                    / 'LocalCache'
                    / 'Local'
                    / 'Lively Wallpaper'
                )
            )
        )
    ]
    return [d for d in dirs if d.is_dir()]


def read_lively(bases, names, monitors):
    if not ({'livelywpf.exe', 'lively.exe'} & names):
        return {}
    result = {}
    for base in lively_dirs(bases):
        layout = _json(base / 'WallpaperLayout.json')
        if isinstance(layout, dict):
            layout = layout.get('Layout') or list(layout.values())
        if not isinstance(layout, list):
            continue
        settings = _json(base / 'Settings.json') or {}
        arrangement = settings.get('WallpaperArrangement') if isinstance(settings, dict) else None
        for i, item in enumerate(layout):
            if not isinstance(item, dict):
                continue
            folder = item.get('LivelyInfoFolderPath') or item.get('LivelyInfoPath') or ''
            folder = Path(folder) if os.path.isabs(folder) else base / 'Library' / 'wallpapers' / folder
            if folder.is_file():
                folder = folder.parent
            info = _json(folder / 'LivelyInfo.json') or {}
            if not isinstance(info, dict):
                info = {}
            thumb = folder / info['Thumbnail'] if info.get('Thumbnail') else None
            media = folder / info['FileName'] if info.get('FileName') else None
            video = str(media) if media and media.suffix.lower() in VIDEO_EXT and media.is_file() else None
            images = [str(thumb)] if thumb and thumb.is_file() else []
            if (
                not images
                and media
                and media.suffix.lower() in {'.jpg', '.jpeg', '.png', '.bmp', '.gif'}
                and media.is_file()
            ):
                images = [str(media)]
            if not (images or video):
                continue
            device = (item.get('Monitor') or {}).get('DeviceName') or (item.get('Monitor') or {}).get('DeviceId')
            display = (
                device
                if any(m.get('name') == device for m in monitors)
                else (monitors[i].get('name') if i < len(monitors) else 'span')
            )
            if arrangement == 1:
                display = 'span'
            result[display] = WallpaperSource(
                'Lively',
                'lively:' + str(folder).lower(),
                images,
                video,
                info.get('Title') or folder.name,
                display,
                _mtime(images[0] if images else video),
                thumbnail=images[0] if images else '',
            )
        if result:
            return result
    return result


def read_wallpaperx(bases, names):
    # Au mieux : ProgramData\LiveWallpaperConfig\ServiceConfig.json (GlobalFilePath / Screens[].FilePath). Vide (rien
    # de défini) sur la machine de dev.
    if not ({'wallpaper.service.exe', 'wallpaper x launcher.exe'} & names):
        return {}
    data = _json(Path(bases['programdata']) / 'LiveWallpaperConfig' / 'ServiceConfig.json')
    if not isinstance(data, dict):
        return {}
    entries = [('span', data.get('GlobalFilePath'))]
    for s in data.get('Screens') or []:
        if isinstance(s, dict):
            entries.append(
                (s.get('DeviceName') or s.get('Name') or 'span', s.get('FilePath') or s.get('WallpaperPath'))
            )
    result = {}
    for display, path in entries:
        if path and os.path.isfile(path):
            video = path if _is_video(path) else None
            result[display] = WallpaperSource(
                'WallpaperX',
                'wallpaperx:' + str(path).lower(),
                [] if video else [path],
                video,
                Path(path).name,
                display,
                _mtime(path),
            )
    return result


def spi_wallpaper():
    buffer = ctypes.create_unicode_buffer(32768)
    ctypes.windll.user32.SystemParametersInfoW(0x73, len(buffer), buffer, 0)
    return buffer.value


def background_type():
    try:
        import winreg

        with winreg.OpenKey(
            winreg.HKEY_CURRENT_USER, r'Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers'
        ) as k:
            return int(winreg.QueryValueEx(k, 'BackgroundType')[0])
    except Exception:
        return 0


def spotlight_image(bases):
    transcoded = Path(bases['appdata']) / 'Microsoft' / 'Windows' / 'Themes' / 'TranscodedWallpaper'
    if transcoded.is_file():
        return str(transcoded)
    candidates = []
    for pattern in (
        'Packages/MicrosoftWindows.Client.CBS_*/LocalCache/Microsoft/IrisService/**/*',
        'Packages/Microsoft.Windows.ContentDeliveryManager_*/LocalState/Assets/*',
    ):
        for p in glob.glob(str(Path(bases['localappdata']) / pattern), recursive=True):
            try:
                if os.path.isfile(p) and os.path.getsize(p) > 100_000:
                    candidates.append((os.path.getmtime(p), p))
            except OSError:
                # Fichier disparu pendant le parcours : ignoré.
                pass
    return max(candidates)[1] if candidates else ''


def windows_source(bases, monitor, kind, spi=None):
    path = monitor.get('path') or ''
    if not path or not os.path.isfile(path):
        path = (spi() if spi else spi_wallpaper()) or ''
    if not path or not os.path.isfile(path):
        alt = Path(bases['appdata']) / 'Microsoft' / 'Windows' / 'Themes' / 'TranscodedWallpaper'
        path = str(alt) if alt.is_file() else ''
    provider = 'Spotlight' if kind == 3 else 'Slideshow' if kind == 2 else 'Windows'
    if provider == 'Spotlight':
        path = spotlight_image(bases) or path
    return WallpaperSource(
        provider,
        f'{provider.lower()}:{path.lower()}',
        [path] if path else [],
        None,
        Path(path).name,
        monitor.get('name', ''),
        _mtime(path),
    )


def resolve(monitors, names=None, bases=None, live=None, kind=None, spi=None):
    # {nom_moniteur: WallpaperSource} ; names/bases/live/kind/spi sont injectables pour que les tests évitent le système réel.
    bases = bases or default_bases()
    names = process_names() if names is None else names
    monitors = monitors or [{'name': ''}]
    found = {}
    for reader in (
        lambda: read_wallpaper_engine(bases, names, monitors),
        lambda: read_lively(bases, names, monitors),
        lambda: read_desktophut(bases, names),
        lambda: read_wallpaperx(bases, names),
    ):
        found = reader()
        if found:
            break
    live_present = None
    if not found:
        live_present = live_host_present() if live is None else live
    kind = background_type() if kind is None else kind
    out = {}
    for monitor in monitors:
        name = monitor.get('name', '')
        source = found.get(name) or found.get('span') or found.get('all')
        if source is None and len(monitors) == 1 and len(found) == 1:
            source = next(iter(found.values()))
        if source is None and live_present:
            source = WallpaperSource(
                'LiveGeneric', 'live:' + name, [], None, 'Live wallpaper', name, 0, needs_capture=True
            )
        if source is None:
            source = windows_source(bases, monitor, kind, spi)
        if (
            len(monitors) > 1
            and monitor.get('width')
            and (source.display not in ('span', 'all') or source.needs_capture)
        ):
            source = replace(
                source, rect=(monitor.get('x', 0), monitor.get('y', 0), monitor['width'], monitor['height'])
            )
        out[name] = source
    return out


def desktop_hut_active():
    # Forme rétrocompatible pour src/terminal/sync_theme.py.
    found = read_desktophut(default_bases(), process_names())
    return [
        {
            'display': d,
            'path': s.thumbnail or (s.images[0] if s.images else ''),
            'modified': s.modified,
            'label': s.label,
            'provider': 'DesktopHut',
        }
        for d, s in found.items()
        if s.thumbnail
    ]


# ---- representative images -------------------------------------------------------------------------------------------------------------


# Les fonctions de cache d'images vivent dans wallpaper_frames.py (qui importe ce module) : résolution paresseuse
# pour éviter l'import circulaire tout en gardant `wallpaper_sources.extract_frames` etc. valides.
_FRAMES = {'cache_root', 'backend_exe', 'prune_cache', 'find_mpv', '_extract_mpv', 'extract_frames', 'representative_images'}


def __getattr__(name):
    if name in _FRAMES:
        import wallpaper_frames
        return getattr(wallpaper_frames, name)
    raise AttributeError(f'module {__name__!r} has no attribute {name!r}')
