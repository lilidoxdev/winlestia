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


def test_winjet_and_app_category_keys_translated():
    import json
    from localization import I18N_DIR
    tables = {l: json.loads((I18N_DIR / (l + '.json')).read_text(encoding='utf-8')) for l in ('fr', 'en', 'es')}
    expected_en = {
        'par %1': 'by %1', 'Aucun accès réseau': 'No network access', 'Stockage': 'Storage', 'Système': 'System',
        'Importer un Winjet': 'Import a Winjet', 'Déposez un fichier .zip ici': 'Drop a .zip file here',
        'Choisir un fichier': 'Choose a file', 'Sur le bureau': 'On the desktop',
        'Aucun Winjet sur le bureau pour le moment.': 'No Winjet on the desktop yet.',
        '{{ system.diskMain.freeGb }} Go libres': '{{ system.diskMain.freeGb }} GB free',
        'Tout': 'All', 'Audio, vidéo et capture': 'Audio, video and capture', 'Autres logiciels': 'Other software',
        'Bureautique et finance': 'Office and finance', 'Autres': 'Other',
    }
    for key, english in expected_en.items():
        assert tables['en'].get(key) == english, f'en.json: {key!r} -> {tables["en"].get(key)!r}'
        assert key in tables['fr'] and key in tables['es'], f'{key!r} missing in fr.json or es.json'


def test_winjet_templates_translate_through_bridge():
    import bridge_winjets
    from localization import TABLE
    source = '{{ system.diskMain.freeGb }} Go libres'
    assert TABLE[source]['en'] == '{{ system.diskMain.freeGb }} GB free'
    assert bridge_winjets._translated('texte inconnu') == 'texte inconnu'
