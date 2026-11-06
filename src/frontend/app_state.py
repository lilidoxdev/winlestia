"""Chemins, état global du processus (arrêt, erreurs QML, traducteur) et journal Qt du frontend."""
from __future__ import annotations
import os
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
BACKEND = ROOT.parent / 'Glass-v5'
CONFIG = (
    Path(
        os.environ.get('WINLESTIA_DATA_DIR')
        or (Path(os.environ.get('LOCALAPPDATA') or str(Path.home() / 'AppData' / 'Local')) / 'Panneaux')
    )
    / 'Caelestia'
)  # honours WINLESTIA_DATA_DIR like the backend
CONFIG.mkdir(parents=True, exist_ok=True)

SHUTTING_DOWN = False
QML_ISSUES = []
TRANSLATOR = None  # renseigné par app_main.main() ; lu par Bridge


def mark_shutting_down():
    """Coupe l'écriture du journal Qt pendant l'arrêt."""
    global SHUTTING_DOWN
    SHUTTING_DOWN = True


def message_handler(kind, context, message):
    if SHUTTING_DOWN:
        return
    if int(kind) > 0:
        QML_ISSUES.append(message)
    with (CONFIG / 'frontend.log').open('a', encoding='utf-8') as stream:
        stream.write(f'{time.strftime("%H:%M:%S")} {kind}: {message}\n')
