"""Trace d'ouverture des panneaux, opt-in : WINLESTIA_TRACE_OPEN=1 ou fichier drapeau « trace-open » placé À CÔTÉ de ce module
(même dossier ; le shell lancé par une tâche planifiée n'hérite pas de l'environnement). Le drapeau est relu à chaque begin().
Sans l'un des deux, mark() ne fait rien.

Chaque ligne du journal (%LOCALAPPDATA%\\Panneaux\\winlestia-open-trace.log, à défaut le dossier temporaire) :
« +<ms depuis l'événement> <étape> ». Étapes : event (événement reçu côté Python), openMode, close (palette déjà ouverte :
fermeture), palette-loaded (panneau palette chargé), visible, first-frame (premier frameSwapped après l'ouverture),
anim-start / anim-done (animation de hauteur du panneau), shock-grab / shock fired / shock skipped / shock shown / shock end
(onde de choc de la palette).
"""
from __future__ import annotations
import os, tempfile, time

FLAG_NAME = 'trace-open'


def _flag_file():
    return os.path.exists(os.path.join(os.path.dirname(os.path.abspath(__file__)), FLAG_NAME))


ENABLED = os.environ.get('WINLESTIA_TRACE_OPEN') == '1' or _flag_file()
_origin = 0.0


def begin(label):
    global _origin, ENABLED
    ENABLED = os.environ.get('WINLESTIA_TRACE_OPEN') == '1' or _flag_file()
    if ENABLED:
        _origin = time.perf_counter()
        _write(0.0, label)


def mark(label):
    if ENABLED:
        _write((time.perf_counter() - _origin) * 1000, label)


def _write(milliseconds, label):
    base = os.environ.get('LOCALAPPDATA')
    folder = os.path.join(base, 'Panneaux') if base else tempfile.gettempdir()
    try:
        os.makedirs(folder, exist_ok=True)
    except OSError:
        folder = tempfile.gettempdir()
    with open(os.path.join(folder, 'winlestia-open-trace.log'), 'a', encoding='utf-8') as log:
        log.write(f'+{milliseconds:7.1f} ms  {label}\n')
