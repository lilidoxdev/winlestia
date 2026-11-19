"""Dictée vocale : minuteur de sondage, événements et état affiché."""
from __future__ import annotations
import time
from PySide6.QtWidgets import QApplication
from PySide6.QtCore import Slot
from win_native import kernel32


class DictationMixin:
    """Dictée vocale : minuteur de sondage, événements et état affiché."""

    @Slot(dict)
    def dictation_tick(self):
        # Ne tourne qu'entre dictation:start et un état final ; les requêtes sont fusionnées pour que le pipe
        # n'accumule jamais de demandes périmées.
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
