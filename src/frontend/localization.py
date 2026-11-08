# Python 3.12 / localization.py / Traductions de l'application Qt et détection de la langue de l'interface Windows.
import ctypes
import json
from pathlib import Path
from PySide6.QtCore import QTranslator

I18N_DIR = Path(__file__).resolve().parent / 'i18n'
LANGUAGES = ('fr', 'en', 'es')


def load_table():
    """Reconstruit TABLE (source -> {fr, en, es}) depuis i18n/<langue>.json, chargé une fois au démarrage."""
    tables = {}
    for lang in LANGUAGES:
        try:
            tables[lang] = json.loads((I18N_DIR / (lang + '.json')).read_text(encoding='utf-8'))
        except (OSError, ValueError):
            # Fichier absent ou illisible : cette langue retombe sur le texte source plutôt que de bloquer le démarrage.
            tables[lang] = {}
    # Le français liste toutes les clés ; une clé absente d'une autre langue reste le texte source.
    return {key: {lang: tables[lang].get(key, key) for lang in LANGUAGES} for key in tables['fr']}


TABLE = load_table()


def windows_language():
    try:
        buffer = ctypes.create_unicode_buffer(85)
        ctypes.windll.kernel32.GetUserDefaultUILanguage.restype = ctypes.c_ushort
        ident = ctypes.windll.kernel32.GetUserDefaultUILanguage()
        primary = ident & 0x3FF
        return {12: 'fr', 9: 'en', 10: 'es'}.get(primary, 'en')
    except Exception:
        return 'en'


class Translator(QTranslator):
    def __init__(self, config):
        super().__init__()
        self.path = Path(config) / 'language.json'
        try:
            self.mode = json.loads(self.path.read_text(encoding='utf-8'))['language']
        except (OSError, ValueError, KeyError):
            self.mode = 'auto'

    @property
    def language(self):
        return windows_language() if self.mode == 'auto' else self.mode

    def isEmpty(self):
        return False

    def translate(self, context, source, disambiguation=None, n=-1):
        return TABLE.get(source, {}).get(self.language, source)

    def select(self, mode):
        self.mode = mode if mode in ('auto', 'fr', 'en', 'es') else 'auto'
        self.path.write_text(json.dumps({'language': self.mode}), encoding='utf-8')
