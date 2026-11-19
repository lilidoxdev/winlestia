"""Façade QML du lecteur multimédia Windows (relayé par le backend)."""
from __future__ import annotations
import time
from PySide6.QtCore import QCoreApplication, QObject, Property, Signal, Slot


class WindowsPlayer(QObject):
    changed = Signal()
    # Émis par le minuteur du tableau de bord (positionChanged) et à chaque mise à jour du backend : seules les barres de progression
    # écoutent.
    positionUpdated = Signal()
    # Durée maximale (secondes) d'extrapolation de la position sans nouvel instantané du backend.
    MAX_EXTRAPOLATION = 3.0

    def __init__(self, bridge):
        super().__init__()
        self.bridge = bridge
        self.data = {}
        # Instant monotone où self.data a été remplacé par un nouvel instantané du backend.
        self.stamp = time.monotonic()
        self.changed.connect(self.positionUpdated)

    def seek(self, value):
        # Un saut invalide la base d'extrapolation : on relance le repère pour ne pas ajouter l'ancien décalage.
        self.stamp = time.monotonic()
        self.bridge.action('media', {'action': 'seek', 'value': value})

    def current_position(self):
        # Entre deux instantanés, la position avance localement (temps écoulé x vitesse) en lecture avec durée connue.
        reported = float(self.data.get('position', 0))
        duration = float(self.data.get('duration', 0))
        if not self.data.get('playing') or not self.data.get('durationKnown', True) or duration <= 0:
            return reported
        rate = float(self.data.get('rate', 1) or 1)
        elapsed = min(max(0.0, time.monotonic() - self.stamp), self.MAX_EXTRAPOLATION)
        return min(reported + elapsed * rate, duration)

    available = Property(bool, lambda s: bool(s.data.get('available')), notify=changed)
    trackTitle = Property(str, lambda s: s.data.get('title', ''), notify=changed)
    trackArtist = Property(str, lambda s: s.data.get('artist', ''), notify=changed)
    trackAlbum = Property(str, lambda s: s.data.get('album', ''), notify=changed)
    trackArtUrl = Property(str, lambda s: s.data.get('art', ''), notify=changed)
    identity = Property(str, lambda s: QCoreApplication.translate('', s.data.get('app') or 'Lecteur Windows'), notify=changed)
    isPlaying = Property(bool, lambda s: bool(s.data.get('playing')), notify=changed)
    length = Property(float, lambda s: float(s.data.get('duration', 0)), notify=changed)
    lengthKnown = Property(
        bool, lambda s: bool(s.data.get('durationKnown', True)) and float(s.data.get('duration', 0)) > 0, notify=changed
    )
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
        lambda s: s.current_position(),
        lambda s, v: s.seek(v),
        notify=positionUpdated,
    )

    @Slot()
    def positionChanged(self):
        # Appelé par le seul minuteur du tableau de bord visible : réévalue la progression sans interroger le backend.
        if self.data.get('playing'):
            self.positionUpdated.emit()

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
