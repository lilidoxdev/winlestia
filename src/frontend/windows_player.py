"""Façade QML du lecteur multimédia Windows (relayé par le backend)."""
from __future__ import annotations
from PySide6.QtCore import QObject, Property, Signal, Slot


class WindowsPlayer(QObject):
    changed = Signal()

    def __init__(self, bridge):
        super().__init__()
        self.bridge = bridge
        self.data = {}

    available = Property(bool, lambda s: bool(s.data.get('available')), notify=changed)
    trackTitle = Property(str, lambda s: s.data.get('title', ''), notify=changed)
    trackArtist = Property(str, lambda s: s.data.get('artist', ''), notify=changed)
    trackAlbum = Property(str, lambda s: s.data.get('album', ''), notify=changed)
    trackArtUrl = Property(str, lambda s: s.data.get('art', ''), notify=changed)
    identity = Property(str, lambda s: s.data.get('app', 'Lecteur Windows'), notify=changed)
    isPlaying = Property(bool, lambda s: bool(s.data.get('playing')), notify=changed)
    length = Property(float, lambda s: float(s.data.get('duration', 0)), notify=changed)
    volume = Property(float, lambda s: float(s.data.get('volume', 70)), notify=changed)
    canGoNext = Property(bool, lambda s: bool(s.data.get('canNext')), notify=changed)
    canGoPrevious = Property(bool, lambda s: bool(s.data.get('canPrevious')), notify=changed)
    canTogglePlaying = Property(bool, lambda s: bool(s.data.get('canToggle')), notify=changed)
    canSeek = Property(bool, lambda s: bool(s.data.get('canSeek')), notify=changed)
    positionSupported = Property(bool, lambda s: bool(s.data.get('canSeek')), notify=changed)
    canRaise = Property(bool, lambda s: bool(s.data.get('available')), notify=changed)
    canQuit = Property(bool, lambda s: False, constant=True)
    position = Property(
        float,
        lambda s: float(s.data.get('position', 0)),
        lambda s, v: s.bridge.action('media', {'action': 'seek', 'value': v}),
        notify=changed,
    )

    @Slot()
    def positionChanged(self):
        pass

    @Slot()
    def togglePlaying(self):
        self.bridge.action('media', {'action': 'toggle'})

    @Slot()
    def previous(self):
        self.bridge.action('media', {'action': 'previous'})

    @Slot()
    def next(self):
        self.bridge.action('media', {'action': 'next'})

    @Slot(name='raise')
    def raise_player(self):
        self.bridge.action('raiseMedia', {})

    @Slot()
    def quit(self):
        pass
