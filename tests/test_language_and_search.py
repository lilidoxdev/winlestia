"""Language selection/persistence and recent-first ordering of the palette search."""
from pathlib import Path
import sys, tempfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src/frontend'))
from localization import Translator, windows_language
from search import palette_search


def test_language_selection_and_persistence():
    with tempfile.TemporaryDirectory() as path:
        t = Translator(path)
        for mode, expected in [('en', 'Settings'), ('fr', 'Paramètres'), ('es', 'Ajustes')]:
            t.select(mode)
            assert t.translate('', 'Paramètres') == expected, f'mode {mode}: wrong translation of "Paramètres"'
        t.select('auto')
        assert t.language in ('en', 'fr', 'es'), f'auto mode resolved to unsupported language {t.language!r}'
        t.select('es')
        assert Translator(path).mode == 'es', 'selected language must persist across Translator instances'


def test_palette_search_lists_recent_first():
    a = palette_search(
        '', {'systemRecent': [{'id': 'recent:C', 'name': 'Canada'}], 'quickTabs': [{'id': 'games', 'name': 'Jeux'}]}
    )
    assert a[0]['name'] == 'Canada', f'recent entry must come first, got {a[0]["name"]!r}'
