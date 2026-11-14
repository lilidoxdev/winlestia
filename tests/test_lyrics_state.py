"""Paroles : compteur de consommateurs, chargement au changement de morceau, réponse périmée rejetée, état « off »."""
import sys
from pathlib import Path

import pytest

pytest.importorskip('PySide6.QtCore')
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src' / 'frontend'))

import bridge_lyrics  # noqa: E402
from bridge_lyrics import LyricsMixin  # noqa: E402


class Signal:
    def __init__(self):
        self.count = 0

    def emit(self):
        self.count += 1


class Fake(LyricsMixin):
    def __init__(self, title='Titre', artist='Artiste', setting=True):
        self.lyricsChanged = Signal()
        self.sent = []
        self._state = {'settings': {'lyrics': setting}, 'media': {'title': title, 'artist': artist, 'album': 'Al', 'duration': 200}}

    def action(self, name, args):
        self.sent.append((name, args))

    def play(self, title, artist='Artiste'):
        self._state = {**self._state, 'media': {'title': title, 'artist': artist, 'duration': 100}}
        self.lyrics_sync()


def synced(key, text='a'):
    return {'state': 'synced', 'key': key, 'lines': [{'time': 1, 'text': text}], 'plain': ''}


def test_nothing_is_fetched_without_consumer():
    bridge = Fake()
    bridge.lyrics_sync()
    assert bridge.lyrics_current()['state'] == 'idle'
    assert bridge.sent == []


def test_ref_count_gates_the_fetch():
    bridge = Fake()
    bridge.lyricsRef(True)
    bridge.lyricsRef(True)
    assert bridge.lyrics_current()['state'] == 'loading'
    assert len(bridge.sent) == 1 and bridge.sent[0][1]['key'] == 'Artiste|Titre' and bridge.sent[0][1]['duration'] == 200
    bridge.lyricsRef(False)
    assert bridge.lyrics_current()['state'] == 'loading'
    bridge.lyricsRef(False)
    assert bridge.lyrics_current()['state'] == 'idle'
    bridge.lyricsRef(False)
    assert bridge._lyrics_refs == 0


def test_track_change_goes_loading_at_once_and_stale_reply_is_dropped():
    bridge = Fake()
    bridge.lyricsRef(True)
    bridge.lyrics_reply(synced('Artiste|Titre'))
    assert bridge.lyrics_current()['state'] == 'synced'
    bridge.play('Autre')
    assert bridge.lyrics_current() == {'state': 'loading', 'lines': [], 'plain': '', 'key': 'Artiste|Autre'}
    bridge.lyrics_reply(synced('Artiste|Titre', 'ancien'))
    assert bridge.lyrics_current()['state'] == 'loading'
    bridge.lyrics_reply(synced('Artiste|Autre', 'neuf'))
    assert bridge.lyrics_current()['lines'] == [{'time': 1.0, 'text': 'neuf'}]


def test_event_rereads_the_result_only_while_loading():
    bridge = Fake()
    bridge.lyricsRef(True)
    bridge.lyrics_event()
    assert len(bridge.sent) == 2
    bridge.lyrics_reply({'state': 'loading', 'key': 'Artiste|Titre'})
    assert bridge.lyrics_current()['state'] == 'loading'
    bridge.lyrics_reply({'state': 'plain', 'key': 'Artiste|Titre', 'plain': 'texte'})
    bridge.lyrics_event()
    assert len(bridge.sent) == 2 and bridge.lyrics_current()['plain'] == 'texte'


def test_setting_off_wins_and_back_on_reloads():
    bridge = Fake(setting=False)
    bridge.lyricsRef(True)
    assert bridge.lyrics_current()['state'] == 'off' and bridge.sent == []
    bridge._state['settings']['lyrics'] = True
    bridge.lyrics_sync()
    assert bridge.lyrics_current()['state'] == 'loading' and len(bridge.sent) == 1


def test_no_title_is_idle_and_unchanged_state_does_not_emit():
    bridge = Fake(title='')
    bridge.lyricsRef(True)
    assert bridge.lyrics_current()['state'] == 'idle'
    emitted = bridge.lyricsChanged.count
    bridge.lyrics_sync()
    assert bridge.lyricsChanged.count == emitted


def test_lost_event_is_retried_after_the_delay(monkeypatch):
    bridge = Fake()
    bridge.lyricsRef(True)
    bridge.lyrics_sync()
    assert len(bridge.sent) == 1
    bridge._lyrics_asked -= bridge_lyrics.RETRY + 1
    bridge.lyrics_sync()
    assert len(bridge.sent) == 2
