"""Visite guidée du premier lancement : état persistant (CONFIG/tutorial.json). Elle ne se lance seule que si ce fichier
n'existe pas ; le tampon d'installation est seulement mémorisé (seenStamp), il ne la relance plus."""
from __future__ import annotations
import json
import os
from pathlib import Path
from atomicfile import write_text as atomic_write_text

STAMP_NAME = 'install-stamp.txt'


def stamp_path(root: Path) -> Path:
    r"""Racine d'installation = parent du dossier du frontend (<racine>\Caelestia-Windows) ; WINLESTIA_INSTALL_STAMP
    force un autre chemin (tests)."""
    override = os.environ.get('WINLESTIA_INSTALL_STAMP')
    return Path(override) if override else Path(root).parent / STAMP_NAME


def read_stamp(root: Path) -> str | None:
    """Contenu nettoyé du tampon, ou None s'il est absent ou vide (exécution de développement)."""
    try:
        text = stamp_path(root).read_text(encoding='utf-8-sig').strip()
    except (OSError, ValueError):
        return None
    return text or None


def load_state(config: Path) -> dict | None:
    """Etat enregistré, ou None si le fichier est absent, illisible ou mal formé."""
    try:
        data = json.loads((Path(config) / 'tutorial.json').read_text(encoding='utf-8'))
    except (OSError, ValueError):
        return None
    if not isinstance(data, dict):
        return None
    seen = data.get('seenStamp')
    return {'seenStamp': seen if isinstance(seen, str) else None, 'done': bool(data.get('done'))}


def is_pending(config: Path, root: Path) -> bool:
    """Vrai tant que l'utilisateur n'a jamais vu la visite (tutorial.json absent ou illisible)."""
    return load_state(config) is None


def save_state(config: Path, root: Path, completed: bool) -> None:
    """Passer et terminer comptent tous deux comme « vu » ; 'done' garde la différence."""
    payload = {'seenStamp': read_stamp(root), 'done': bool(completed)}
    try:
        atomic_write_text(Path(config) / 'tutorial.json', json.dumps(payload))
    except OSError:
        pass
