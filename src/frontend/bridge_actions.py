"""Actions du Bridge : envoi des commandes au backend, navigation et abonnements."""
from __future__ import annotations
import time
from PySide6.QtCore import QTimer, Slot
from custom_palette import DEFAULT
from win_native import kernel32, user32


class ActionMixin:
    """Actions du Bridge : envoi des commandes au backend, navigation et abonnements."""

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
