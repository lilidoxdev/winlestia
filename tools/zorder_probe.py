"""Sonde de diagnostic de l'ordre Z des fenêtres Winlestia (lecture seule, Python 3.11 + ctypes, aucune dépendance).

Liste les fenêtres de premier niveau dans l'ordre Z (EnumWindows : du dessus vers le dessous), repère celles de
Winlestia (pythonw/python du frontend, Panneaux.Glass.exe, classe ou titre contenant Panneaux/Winlestia/Cadre) et
signale « SANDWICH » quand une fenêtre étrangère visible, dont le rectangle recoupe une fenêtre de contenu, se trouve
entre ce contenu et l'un de ses hôtes de fond natif.

Reproduction : lancer la sonde (mode événements par défaut), puis passer une fenêtre quelconque en « toujours au
premier plan » au-dessus du dock (PowerToys Always On Top : Win+Ctrl+T, ou Gestionnaire des tâches > Toujours visible).
Surveiller les lignes SANDWICH.

Usage : py -3.11 tools/zorder_probe.py [--once] [--duration SECONDES] [--log FICHIER]
Ctrl+C quitte et retire les crochets. La sonde ne déplace ni ne modifie aucune fenêtre.
"""
import argparse
import ctypes
import sys
import time
from ctypes import wintypes

WINLESTIA_EXES = ('panneaux.glass.exe',)
FRONTEND_EXES = ('pythonw.exe', 'python.exe')
NAME_HINTS = ('panneaux', 'winlestia', 'cadre')
HOST_CLASS = 'continuousglass'  # classe des hôtes de fond natifs (WinLestiaContinuousGlass91)


def intersects(a, b):
    """Rectangles (gauche, haut, droite, bas) : vrai si l'intersection n'est pas vide."""
    return a[0] < b[2] and b[0] < a[2] and a[1] < b[3] and b[1] < a[3]


def is_winlestia(entry):
    """Entrée : dict avec exe, title, cls. Vrai pour Panneaux.Glass.exe ou un nom/classe portant un indice."""
    exe = (entry.get('exe') or '').lower()
    text = ((entry.get('title') or '') + ' ' + (entry.get('cls') or '')).lower()
    if exe in WINLESTIA_EXES:
        return True
    return any(hint in text for hint in NAME_HINTS)


def is_host(entry):
    return (HOST_CLASS in (entry.get('cls') or '').lower()) and bool(entry.get('winlestia'))


def find_sandwiches(entries):
    """entries : liste dans l'ordre Z (dessus -> dessous), dicts {hwnd, pid, visible, rect, winlestia, content, host}.
    Pour chaque contenu, les hôtes visibles du même processus situés dessous sont cherchés ; toute fenêtre étrangère
    visible qui recoupe le contenu et se place entre lui et un de ces hôtes est renvoyée : (contenu, étrangère, hôte)."""
    found = []
    for start, content in enumerate(entries):
        if not (content.get('winlestia') and content.get('content')):
            continue
        hosts = [i for i in range(start + 1, len(entries))
                 if entries[i].get('host') and entries[i]['pid'] == content['pid'] and entries[i]['visible']]
        if not hosts:
            continue
        for i in range(start + 1, hosts[-1]):
            other = entries[i]
            if other.get('winlestia') or not other['visible'] or not intersects(other['rect'], content['rect']):
                continue
            found.append((content, other, entries[next(h for h in hosts if h > i)]))
    return found


# --- Accès Win32 (lecture seule) ---

WS_EX_TOPMOST = 0x8
GWL_EXSTYLE = -20
EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MINIMIZEEND, EVENT_OBJECT_REORDER = 0x3, 0xB, 0x17, 0x8004
WINEVENT_OUTOFCONTEXT = 0
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
_exe_cache = {}


def _libs():
    user32 = ctypes.WinDLL('user32', use_last_error=True)
    kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
    enum_proc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.EnumWindows.argtypes = [enum_proc, wintypes.LPARAM]
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.SetWinEventHook.restype = wintypes.HANDLE
    user32.PeekMessageW.argtypes = [ctypes.POINTER(wintypes.MSG), wintypes.HWND, wintypes.UINT, wintypes.UINT, wintypes.UINT]
    kernel32.OpenProcess.restype = wintypes.HANDLE
    kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    return user32, kernel32, enum_proc


def exe_name(kernel32, pid):
    if pid not in _exe_cache:
        name = ''
        handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
        if handle:
            buffer = ctypes.create_unicode_buffer(1024)
            size = wintypes.DWORD(1024)
            if kernel32.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(size)):
                name = buffer.value.rsplit('\\', 1)[-1]
            kernel32.CloseHandle(handle)
        _exe_cache[pid] = name
    return _exe_cache[pid]


def snapshot(libs):
    """Liste des fenêtres de premier niveau, dessus -> dessous."""
    user32, kernel32, enum_proc = libs
    _exe_cache.clear()  # pas de cache entre instantanés : les pid sont réutilisés
    entries = []

    def visit(hwnd, _):
        title, cls = ctypes.create_unicode_buffer(256), ctypes.create_unicode_buffer(256)
        rect, pid = wintypes.RECT(), wintypes.DWORD()
        user32.GetWindowTextW(hwnd, title, 256)
        user32.GetClassNameW(hwnd, cls, 256)
        user32.GetWindowRect(hwnd, ctypes.byref(rect))
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        entries.append({
            'hwnd': hwnd or 0, 'pid': pid.value, 'exe': exe_name(kernel32, pid.value), 'title': title.value,
            'cls': cls.value, 'visible': bool(user32.IsWindowVisible(hwnd)),
            'rect': (rect.left, rect.top, rect.right, rect.bottom),
            'topmost': bool(user32.GetWindowLongW(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST),
        })
        return True

    user32.EnumWindows(enum_proc(visit), 0)
    for entry in entries:
        entry['winlestia'] = is_winlestia(entry)
        entry['host'] = is_host(entry)
        entry['content'] = entry['winlestia'] and not entry['host']
    return entries


def describe(entries):
    """Lignes de texte : une par fenêtre Winlestia, puis les SANDWICH."""
    lines = []
    for z, e in enumerate(entries):
        if e['winlestia']:
            kind = 'hote' if e['host'] else 'contenu'
            lines.append(f"z={z:<4} hwnd={e['hwnd']:#010x} {kind:<7} top={int(e['topmost'])} vis={int(e['visible'])} "
                         f"rect={e['rect']} cls={e['cls']!r} titre={e['title']!r}")
    for content, other, host in find_sandwiches(entries):
        lines.append(f"SANDWICH: {other['exe'] or '?'} {other['title']!r} (hwnd={other['hwnd']:#010x}) entre le contenu "
                     f"{content['hwnd']:#010x} et l'hote {host['hwnd']:#010x}")
    return lines


class Reporter:
    """N'écrit que si l'instantané a changé (journal court)."""

    def __init__(self, libs, log):
        self.libs, self.log, self.last = libs, log, None

    def emit(self, text):
        print(text, flush=True)
        if self.log:
            self.log.write(text + '\n')
            self.log.flush()

    def report(self, reason, full=False):
        lines = describe(snapshot(self.libs))
        if not full and lines == self.last:
            return
        self.last = lines
        self.emit(f"[{time.strftime('%H:%M:%S')}] {reason}")
        for line in lines:
            self.emit('  ' + line)


def main(argv=None):
    parser = argparse.ArgumentParser(description='Sonde Z-order Winlestia (lecture seule).')
    parser.add_argument('--once', action='store_true', help='un instantané puis sortie')
    parser.add_argument('--duration', type=float, default=0, help='durée en secondes (0 = jusqu\'à Ctrl+C)')
    parser.add_argument('--log', help='fichier journal en plus de stdout')
    args = parser.parse_args(argv)
    if sys.platform != 'win32':
        print('Windows uniquement.', file=sys.stderr)
        return 2
    libs = _libs()
    user32 = libs[0]
    log = open(args.log, 'a', encoding='utf-8') if args.log else None
    reporter = Reporter(libs, log)
    try:
        reporter.report('instantané', full=True)
        if args.once:
            return 0
        names = {EVENT_SYSTEM_FOREGROUND: 'FOREGROUND', EVENT_OBJECT_REORDER: 'REORDER',
                 EVENT_SYSTEM_MOVESIZEEND: 'MOVESIZEEND', EVENT_SYSTEM_MINIMIZEEND: 'MINIMIZEEND'}
        proc_type = ctypes.WINFUNCTYPE(None, wintypes.HANDLE, wintypes.DWORD, wintypes.HWND, wintypes.LONG, wintypes.LONG,
                                       wintypes.DWORD, wintypes.DWORD)

        def on_event(hook, event, hwnd, object_id, child_id, thread, ms):
            try:
                reporter.report(names.get(event, hex(event)))
            except Exception as error:  # un rappel ne doit jamais lever
                print(f'erreur: {error}', file=sys.stderr)

        callback = proc_type(on_event)  # référence gardée pendant toute la boucle
        user32.SetWinEventHook.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.HMODULE, proc_type,
                                           wintypes.DWORD, wintypes.DWORD, wintypes.DWORD]
        hooks = [user32.SetWinEventHook(e, e, None, callback, 0, 0, WINEVENT_OUTOFCONTEXT) for e in names]
        deadline = time.time() + args.duration if args.duration > 0 else None
        message = wintypes.MSG()
        try:
            while deadline is None or time.time() < deadline:
                # PeekMessage + courte attente : Ctrl+C et --duration restent honorés (GetMessage bloquerait).
                if user32.PeekMessageW(ctypes.byref(message), None, 0, 0, 1):
                    user32.TranslateMessage(ctypes.byref(message))
                    user32.DispatchMessageW(ctypes.byref(message))
                else:
                    time.sleep(0.02)
        except KeyboardInterrupt:
            pass
        finally:
            for hook in hooks:
                if hook:
                    user32.UnhookWinEvent(hook)
    finally:
        if log:
            log.close()
    return 0


if __name__ == '__main__':
    sys.exit(main())
