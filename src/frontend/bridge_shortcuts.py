"""Raccourcis clavier : noms de touches, capture et enregistrement."""
from __future__ import annotations
import ctypes
from PySide6.QtCore import QCoreApplication, Slot
from PySide6.QtWidgets import QFileDialog
from PySide6.QtGui import QKeySequence
from win_native import user32


class ShortcutMixin:
    """Raccourcis clavier : noms de touches, capture et enregistrement."""

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
