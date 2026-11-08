# Python 3.12 / emoji_data.py / Table d'emojis embarquée (data/emoji.json), lue seulement à la première recherche.
import json
from pathlib import Path

EMOJI_FILE = Path(__file__).resolve().parent / 'data' / 'emoji.json'
_rows = None


def emoji_rows():
    """Lignes (emoji, mots-clés français, english keywords) ; le JSON n'est lu qu'une fois, à la demande."""
    global _rows
    if _rows is None:
        _rows = [tuple(row) for row in json.loads(EMOJI_FILE.read_text(encoding='utf-8'))]
    return _rows
