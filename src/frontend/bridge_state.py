"""Réception des réponses du backend : fusion de l’état et dispatch des événements."""
from __future__ import annotations
import ctypes
import ctypes.wintypes
import time
from win_native import MonitorInfo, user32
from PySide6.QtWidgets import QApplication


class StateMixin:
    """Réception des réponses du backend : fusion de l’état et dispatch des événements."""

    def screen_names(self, devices):
        # Le backend nomme les moniteurs par périphérique Win32 (\\.\DISPLAY1) alors que QScreen.name() est le nom du
        # modèle sous Qt 6 : on traduit via le moniteur situé sous chaque écran.
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
            # Les compteurs de performance changent presque à chaque sondage ; ils ont leur propre signal pour que la
            # rafale de Bridge.changed (toutes les liaisons réévaluées) ne parte que pour de vrais changements.
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
                continue  # mode silencieux : pas d'OSD, donc rien ne prend le focus ni n'anime par-dessus le jeu
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
