"""URLs locales, icônes d’applications, avatar et copie de fichiers."""
from __future__ import annotations
import shutil
import time
from app_state import CONFIG
from pathlib import Path
from PySide6.QtCore import QCoreApplication, QUrl, Slot
from PySide6.QtWidgets import QFileDialog


class AssetMixin:
    """URLs locales, icônes d’applications, avatar et copie de fichiers."""

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
