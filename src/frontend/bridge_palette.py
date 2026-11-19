"""Palette de recherche : requêtes, fichiers indexés et exécution d’un résultat."""
from __future__ import annotations
import app_state
import os
from PySide6.QtCore import QTimer, Slot
from search import emoji_choices, palette_search, settings_uri_known


class PaletteMixin:
    """Palette de recherche : requêtes, fichiers indexés et exécution d’un résultat."""

    @Slot(str)
    def searchPalette(self, text):
        self.query = text
        self._results = palette_search(text, self._state, app_state.TRANSLATOR.language, self._windows)
        self.searchChanged.emit()

    def palette_lifecycle(self, was):
        if self.mode == 'palette' and was != 'palette':
            self.action('windowList', {})
        elif was == 'palette' and self.mode != 'palette':
            self._windows = []
            self.fileQuery('')

    @Slot(str)
    def fileQuery(self, term):
        # Moins de 2 caractères : aucune requête. Sinon un minuteur de 200 ms envoie le dernier terme ; les réponses
        # plus anciennes sont écartées par la génération.
        self._file_gen += 1
        self._file_term = term.strip()
        self._file_items = []
        if self._file_timer is None:
            self._file_timer = QTimer(self)
            self._file_timer.setSingleShot(True)
            self._file_timer.setInterval(200)
            self._file_timer.timeout.connect(self.file_send)
        self._file_timer.stop()
        if len(self._file_term) < 2:
            if self._file_sent:
                self._file_sent = False
                self.action('fileCancel', {})
            self._file_status = 'idle'
        else:
            self._file_status = 'searching'
            self._file_timer.start()
        self.fileChanged.emit()

    def file_send(self):
        self._file_sent = True
        self.action('fileSearch', {'term': self._file_term, 'gen': self._file_gen})

    def files_arrived(self, data):
        if not isinstance(data, dict) or data.get('gen') != self._file_gen or self.mode != 'palette':
            return
        gen = self._file_gen
        self._file_status = str(data.get('status') or 'ok')
        self._file_items = [
            {
                'id': 'file:%d' % int(f.get('index', 0)),
                'index': int(f.get('index', 0)),
                'gen': gen,
                'name': str(f.get('name') or ''),
                'comment': str(f.get('path') or ''),
                'icon': 'draft',
                'kind': 'file',
            }
            for f in (data.get('items') or [])
            if isinstance(f, dict)
        ]
        self.fileChanged.emit()

    @Slot(str, result='QVariantList')
    def emojiChoices(self, query):
        return emoji_choices(query)

    @Slot('QVariant')
    def executeResult(self, value):
        if hasattr(value, 'toVariant'):
            value = value.toVariant()
        if not value:
            return
        kind = value.get('kind', 'app')
        key = value.get('id', '')
        if kind == 'navigate':
            self.navigate(key)
        elif kind == 'calc':
            self.closePanel()
            self.action('copyText', {'text': value['name'][2:]})
        elif kind == 'autocomplete':
            self.prefillPalette(key)
        elif kind == 'session':
            # Toute action de session, verrouillage compris, attend la boîte de confirmation de Main.qml, qui envoie
            # elle-même confirmed:true.
            if key in ('shutdown', 'restart', 'logout', 'lock', 'sleep', 'hibernate'):
                self.sessionRequested.emit(key)
        elif kind == 'wallpaper':
            self.closePanel()
            self.setWallpaper(key)
        elif kind == 'emoji':
            self.closePanel()
            self.action('copyText', {'text': key})
        elif kind == 'window':
            hwnd = value.get('hwnd')
            if isinstance(hwnd, int) and any(w.get('hwnd') == hwnd for w in self._windows):
                self.closePanel()
                self.action('activate', {'hwnd': hwnd})
        elif kind == 'setting':
            if settings_uri_known(key):
                self.closePanel()
                try:
                    os.startfile(key)
                except OSError:
                    # Fichier introuvable ou sans application associée : rien à ouvrir.
                    pass
        elif kind == 'file':
            # Le backend retrouve le chemin dans son dernier résultat ; seuls une génération et un index repartent
            # (mis en file avant la fermeture du panneau, qui annule la recherche).
            if value.get('gen') == self._file_gen and self.mode == 'palette':
                self.action('fileOpen', {'gen': value['gen'], 'index': value.get('index', -1)})
                self.closePanel()
        else:
            self.closePanel()
            self.action('paletteRun', {'kind': kind, 'id': key, 'query': self.query})

    @Slot()
    def focusPalette(self):
        def visit(item):
            if item.objectName() == 'paletteQuery':
                item.setProperty('text', '')
                item.forceActiveFocus()
                return True
            return any(visit(child) for child in item.childItems())

        if self.top:
            visit(self.top.contentItem())

    @Slot(str)
    def prefillPalette(self, text):
        def visit(item):
            if item.objectName() == 'paletteQuery':
                item.setProperty('text', text)
                return True
            return any(visit(child) for child in item.childItems())

        if self.top:
            visit(self.top.contentItem())
        self.searchPalette(text)
