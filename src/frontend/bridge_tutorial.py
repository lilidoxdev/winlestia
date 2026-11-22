"""Visite guidée : démarrage différé (verrou, plein écran), fin enregistrée, maintien des surfaces de bord."""
from __future__ import annotations
import time
from PySide6.QtCore import Slot
from app_state import CONFIG, ROOT
import tutorial

# Surface de bord -> (événement d'affichage, événement de masquage) ; None = pas d'événement pour le dock en aperçu.
EDGE_EVENTS = {'dock': ('dock', 'dockHide'), 'social': ('socialPeek', 'socialHide'), 'bookmarks': ('bookmarksPeek', 'bookmarksHide')}


class TutorialMixin:
    """Etat de la visite : en attente (jamais vue ou nouvelle installation), en cours, maintien du dock et des rails."""

    def init_tutorial(self):
        self.tutorial_wait = tutorial.is_pending(CONFIG, ROOT)
        self.tutorial_on = False
        self.tutorial_hold = set()

    def tutorial_check(self, *args):
        """Lance la visite dès que tout est calme : instantané reçu, pas verrouillé, pas de plein écran. Appelé sur
        les changements d'état (pas de minuteur)."""
        busy = (self._state.get('lock') or {}).get('locked') or self.screenFullscreen
        if self.tutorial_on and busy:
            self.abortTutorial()  # verrou ou plein écran en pleine visite : interrompue, pas vue ; reprise plus tard
            return
        if not self.tutorial_wait or self.tutorial_on or not self._state.get('settings') or not self.top:
            return
        if busy:
            return
        self.tutorial_wait = False
        self.tutorial_on = True
        self.tutorialPendingChanged.emit()
        self.tutorialRequested.emit()

    def tutorial_pending(self):
        return self.tutorial_wait or self.tutorial_on

    @Slot()
    def startTutorial(self):
        """Depuis les réglages : relance la visite (même verrou et plein écran que le premier lancement)."""
        if self.tutorial_on:
            return
        self.tutorial_wait = True
        self.tutorialPendingChanged.emit()
        self.tutorial_check()

    def tutorial_release(self):
        for name in list(self.tutorial_hold):
            self.holdEdge(name, False)

    @Slot(bool)
    def finishTutorial(self, completed):
        """Passer ou terminer : enregistré comme vu. Sans effet si la visite n'est pas en cours."""
        if not self.tutorial_on:
            return
        self.tutorial_on = False
        self.tutorial_wait = False
        self.tutorial_release()
        tutorial.save_state(CONFIG, ROOT, bool(completed))
        self.tutorialPendingChanged.emit()

    @Slot()
    def abortTutorial(self):
        """Interruption (verrou, plein écran, erreur QML, destruction) : relâche tout, ne marque pas comme vue et
        ré-arme l'attente ; la prochaine vérification (changement d'état) relancera la visite. Sans effet si inactive."""
        if not self.tutorial_on:
            return
        self.tutorial_on = False
        self.tutorial_wait = True
        self.tutorial_release()
        self.tutorialPendingChanged.emit()
        self.tutorialAborted.emit()

    @Slot(str, bool)
    def holdEdge(self, name, on):
        """Garde le dock ou un rail affiché pendant une étape (sans minuteur), le relâche à la sortie."""
        if name == 'panel':  # panneau du haut : maintenu ouvert, sans fermeture au pointeur (pas d'événement)
            (self.tutorial_hold.add if on else self.tutorial_hold.discard)(name)
            return
        events = EDGE_EVENTS.get(name)
        if not events:
            return
        if on:
            self.tutorial_hold.add(name)
            self.shellEvent.emit(events[0])
        else:
            self.tutorial_hold.discard(name)
            if not self.args.preview or name != 'dock':
                self.shellEvent.emit(events[1])

    @Slot(bool)
    def holdDock(self, on):
        self.holdEdge('dock', on)
