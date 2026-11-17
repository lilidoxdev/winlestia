"""Trace d'ouverture des panneaux, opt-in : WINLESTIA_TRACE_OPEN=1. Sans la variable, mark() ne fait rien.

Chaque ligne du journal (%TEMP%\\winlestia-open-trace.log) : « +<ms depuis l'événement> <étape> ». Étapes : event (événement
reçu côté Python), openMode, visible, first-frame (premier frameSwapped après l'ouverture), anim-done (fin de l'animation de
hauteur du panneau).
"""
from __future__ import annotations
import os, tempfile, time

ENABLED = os.environ.get('WINLESTIA_TRACE_OPEN') == '1'
_origin = 0.0


def begin(label):
    global _origin
    if ENABLED:
        _origin = time.perf_counter()
        _write(0.0, label)


def mark(label):
    if ENABLED:
        _write((time.perf_counter() - _origin) * 1000, label)


def _write(milliseconds, label):
    with open(os.path.join(tempfile.gettempdir(), 'winlestia-open-trace.log'), 'a', encoding='utf-8') as log:
        log.write(f'+{milliseconds:7.1f} ms  {label}\n')
