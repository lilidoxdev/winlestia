"""Thème, palette publiée, surfaces et sélecteur de couleur du Bridge."""
from __future__ import annotations
import ctypes
import json
import os
import time
from screen_picker import ClickPicker
from pathlib import Path
from PySide6.QtWidgets import QApplication
from PySide6.QtGui import QColor
from PySide6.QtCore import QTimer, Slot
from concurrent.futures import ThreadPoolExecutor
from win_native import user32
from custom_palette import valid_color


class ThemeMixin:
    """Thème, palette publiée, surfaces et sélecteur de couleur du Bridge."""

    def current_theme(self):
        return self.walltheme.for_screen(self._screen.name())

    def theme_ready(self):
        self._theme_revision += 1
        self.publish_palette()
        self.changed.emit()
        self.frameChanged.emit()

    # Winlestia Fichiers (WPF) ne voit pas le fond animé qui remplace le fond réel : la palette réellement affichée
    # ici lui est transmise dans %LOCALAPPDATA%\Panneaux\palette-shell.json (mode auto uniquement, écrite seulement si
    # elle a changé ; le fichier y est surveillé par événement, jamais sondé).
    def publish_palette(self):
        # Calculée ici, écrite hors du thread UI (exécuteur ordonné à un seul worker). Quitter le mode auto écrit
        # {"mode":"custom"} pour que Palette.cs abandonne la palette auto périmée.
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
        # Remplace le sondage explorateur de 4 s par des événements : ne s'exécute que si un réglage de thème, la
        # liste de dossiers ou la palette du fond a réellement changé.
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

    @Slot(str, int, result='QVariantMap')
    def paletteForScreen(self, name, revision):
        return self.walltheme.for_screen(name).get('palette', {})

    def theme_pending_expire(self):
        if self._theme_pending:
            self._theme_pending = None
            self.changed.emit()

    @Slot(str, int, result='QVariantMap')
    def surfaceForScreen(self, name, revision):
        return self.surface_scene.data.get(name, {}) if self.surface_scene else {}

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

    def frame_screens(self):
        result = []
        for screen in QApplication.screens():
            rect = screen.geometry()
            result.append(
                {'name': screen.name(), 'x': rect.x(), 'y': rect.y(), 'width': rect.width(), 'height': rect.height()}
            )
        return result

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
