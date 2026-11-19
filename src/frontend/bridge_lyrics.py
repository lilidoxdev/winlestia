"""Paroles synchronisées du morceau en cours : état exposé à QML (`Bridge.lyrics`) et cycle de la recherche.

La recherche réseau tourne dans le backend, hors du canal : la commande `lyrics` répond tout de suite (résultat connu ou
`loading`), puis l'évènement `lyrics` invite à la renvoyer pour lire le résultat. Aucun minuteur : tout est piloté par les
instantanés d'état (changement de morceau, réglage) et par cet évènement."""
from __future__ import annotations
import time
from PySide6.QtCore import Slot

# Filet de sécurité si l'évènement `lyrics` s'est perdu (file d'évènements pleine) : on redemande au plus tous les RETRY s.
RETRY = 6.0


def lyrics_data(state='idle', key='', lines=None, plain=''):
    return {'state': state, 'lines': list(lines or []), 'plain': plain, 'key': key}


class LyricsMixin:
    """Paroles : compteur de consommateurs, déclenchement au changement de morceau, rejet des réponses périmées."""

    _lyrics_refs = 0
    _lyrics_data = None
    _lyrics_asked = 0.0

    @Slot(bool)
    def lyricsRef(self, take):
        self._lyrics_refs = max(0, self._lyrics_refs + (1 if take else -1))
        self.lyrics_sync()

    def lyrics_current(self):
        return self._lyrics_data or lyrics_data()

    def lyrics_set(self, data):
        if data != self.lyrics_current():
            self._lyrics_data = data
            self.lyricsChanged.emit()

    def lyrics_track(self):
        """(clé, arguments) du morceau en cours, ou None sans titre."""
        media = self._state.get('media') or {}
        title = str(media.get('title') or '').strip()
        if not title:
            return None
        artist = str(media.get('artist') or '').strip()
        args = {
            'title': title,
            'artist': artist,
            'album': str(media.get('album') or ''),
            'duration': float(media.get('duration') or 0),
        }
        return f'{artist}|{title}', args

    def lyrics_ask(self, key, args):
        self._lyrics_asked = time.monotonic()
        self.action('lyrics', {**args, 'key': key})

    def lyrics_sync(self):
        """Aligne l'état sur le réglage, les consommateurs et le morceau. Appelé à chaque instantané d'état."""
        if not (self._state.get('settings') or {}).get('lyrics', True):
            self.lyrics_set(lyrics_data('off'))
            return
        track = self.lyrics_track() if self._lyrics_refs > 0 else None
        if track is None:
            self.lyrics_set(lyrics_data('idle'))
            return
        key, args = track
        current = self.lyrics_current()
        if current['key'] != key or current['state'] in ('idle', 'off'):
            self.lyrics_set(lyrics_data('loading', key))
            self.lyrics_ask(key, args)
        elif current['state'] == 'loading' and time.monotonic() - self._lyrics_asked > RETRY:
            self.lyrics_ask(key, args)

    def lyrics_event(self):
        """Évènement `lyrics` du backend : une recherche vient de finir, on relit le résultat."""
        current = self.lyrics_current()
        track = self.lyrics_track() if self._lyrics_refs > 0 else None
        if current['state'] == 'loading' and track is not None and track[0] == current['key']:
            self.lyrics_ask(*track)

    def lyrics_reply(self, reply):
        """Réponse de la commande `lyrics` : rejetée si elle ne concerne plus le morceau attendu."""
        current = self.lyrics_current()
        if not isinstance(reply, dict) or reply.get('key') != current['key'] or current['state'] in ('idle', 'off'):
            return
        state = reply.get('state')
        if state == 'off':
            self.lyrics_set(lyrics_data('off'))
        elif state in ('synced', 'plain', 'instrumental', 'none'):
            lines = [
                {'time': float(line.get('time', 0)), 'text': str(line.get('text', ''))}
                for line in (reply.get('lines') or [])
                if isinstance(line, dict)
            ]
            self.lyrics_set(
                lyrics_data(
                    state,
                    current['key'],
                    lines if state == 'synced' else [],
                    str(reply.get('plain') or '') if state == 'plain' else '',
                )
            )
