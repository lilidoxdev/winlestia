"""Profilage opt-in de l'animation d'ouverture/fermeture des panneaux : WINLESTIA_PROFILE_PANELS=1 (lu une fois à l'import).

Désactivé, chaque point d'entrée retourne immédiatement : aucun minuteur, aucune connexion de signal, aucun crochet d'image.
Activé : begin(panel) ... end(panel) se branche sur frameSwapped de la fenêtre, compte les images dépassant 20 ms et écrit une
ligne dans frontend.log (panneau, durée, images, manquées, pire image, repères avec décalage, coût cumulé du travail Python).
"""
from __future__ import annotations
import logging
import os
import time

ENABLED = os.environ.get('WINLESTIA_PROFILE_PANELS') == '1'
MISSED_MS = 20.0
MAX_MARKS = 64

log = logging.getLogger('panneaux.panel_profile')
_marks = []  # (label, t) en attente ou de l'animation en cours
_state = {'panel': None, 'origin': 0.0, 'frames': [], 'cost': {}, 'window': None}


def frame_stats(stamps, limit_ms=MISSED_MS):
    """(images, manquées, pire intervalle en ms) à partir d'horodatages en secondes. Fonction pure."""
    intervals = [(b - a) * 1000.0 for a, b in zip(stamps, stamps[1:])]
    return len(stamps), sum(1 for v in intervals if v > limit_ms), max(intervals, default=0.0)


def format_line(panel, duration_ms, frames, missed, worst_ms, marks, cost):
    """Ligne de journal unique. marks : [(label, décalage ms)], cost : {nom: ms}. Fonction pure."""
    marks_text = ' '.join(f'{label}@{offset:+.1f}' for label, offset in marks) or '-'
    cost_text = ' '.join(f'{name}={ms:.1f}ms' for name, ms in sorted(cost.items())) or '-'
    return (
        f'panel-profile panel={panel} duration={duration_ms:.1f}ms frames={frames} missed={missed} '
        f'worst={worst_ms:.1f}ms marks=[{marks_text}] cost=[{cost_text}]'
    )


def mark(name):
    if not ENABLED:
        return
    _marks.append((str(name), time.perf_counter()))
    if len(_marks) > MAX_MARKS and _state['panel'] is None:
        del _marks[: len(_marks) - MAX_MARKS]


def tick():
    """Début d'une mesure de coût ; 0.0 sans effet si désactivé."""
    return time.perf_counter() if ENABLED else 0.0


def tock(name, started):
    """Cumule le coût (ms) depuis tick() dans l'animation en cours."""
    if not ENABLED or not started or _state['panel'] is None:
        return
    elapsed = (time.perf_counter() - started) * 1000.0
    _state['cost'][name] = _state['cost'].get(name, 0.0) + elapsed


def _on_frame():
    _state['frames'].append(time.perf_counter())


def begin(panel, window=None):
    if not ENABLED:
        return
    if _state['panel'] is not None:
        end(_state['panel'])
    _state.update(panel=str(panel), origin=time.perf_counter(), frames=[], cost={}, window=None)
    if window is not None:
        try:
            window.frameSwapped.connect(_on_frame)
            _state['window'] = window
        except (AttributeError, RuntimeError):
            pass
    mark('begin')


def end(panel=None, emit=None):
    if not ENABLED or _state['panel'] is None:
        return
    now = time.perf_counter()
    window = _state['window']
    if window is not None:
        try:
            window.frameSwapped.disconnect(_on_frame)
        except (AttributeError, RuntimeError, TypeError):
            pass
    origin = _state['origin']
    frames, missed, worst = frame_stats(_state['frames'])
    marks = [(label, (t - origin) * 1000.0) for label, t in _marks if t >= origin - 1.0]
    line = format_line(_state['panel'], (now - origin) * 1000.0, frames, missed, worst, marks, _state['cost'])
    _state.update(panel=None, frames=[], cost={}, window=None)
    _marks.clear()
    (emit or _write)(line)


def _write(line):
    log.info(line)
    try:
        import app_state

        with (app_state.CONFIG / 'frontend.log').open('a', encoding='utf-8') as stream:
            stream.write(f'{time.strftime("%H:%M:%S")} profile: {line}\n')
    except Exception:
        pass
