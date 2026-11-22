# Cohérence des langues : mêmes clés partout, câblage de localization.LANGUAGES, couverture QML complète, placeholders conservés.
import json
import re
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
FRONTEND = ROOT / 'src' / 'frontend'
QML = FRONTEND / 'qml'
I18N = FRONTEND / 'i18n'
REQUIRED = ('fr', 'en', 'es', 'ru')

sys.path.insert(0, str(FRONTEND))
sys.path.insert(0, str(Path(__file__).resolve().parent))

from test_i18n_coverage import extract_qstr  # noqa: E402

_PLACEHOLDER = re.compile(r'%(?:L?[1-9]|n)')


def _files():
    return sorted(p.stem for p in I18N.glob('*.json'))


def _table(lang):
    return json.loads((I18N / f'{lang}.json').read_text(encoding='utf-8'))


def _present():
    return [lang for lang in REQUIRED if (I18N / f'{lang}.json').exists()]


def test_every_language_has_same_keys_as_french_and_no_empty_value():
    fr = _table('fr')
    problems = []
    for lang in _files():
        table = _table(lang)
        missing = sorted(set(fr) - set(table))
        extra = sorted(set(table) - set(fr))
        empty = sorted(k for k, v in table.items() if not str(v).strip())
        if missing:
            problems.append(f'{lang}: {len(missing)} clé(s) manquante(s), ex. {missing[:5]}')
        if extra:
            problems.append(f'{lang}: {len(extra)} clé(s) en trop, ex. {extra[:5]}')
        if empty:
            problems.append(f'{lang}: {len(empty)} valeur(s) vide(s), ex. {empty[:5]}')
    assert not problems, '\n'.join(problems)


def test_languages_constant_matches_json_files_and_required_languages():
    import localization

    assert set(localization.LANGUAGES) == set(_files()), (localization.LANGUAGES, _files())
    for lang in REQUIRED:
        assert lang in localization.LANGUAGES, lang
        assert (I18N / f'{lang}.json').exists(), f'{lang}.json absent'


def test_every_qml_qstr_literal_is_a_french_key():
    fr = _table('fr')
    missing = {}
    for path in QML.rglob('*.qml'):
        for key in extract_qstr(path.read_text(encoding='utf-8')):
            if key not in fr:
                missing.setdefault(key, []).append(path.name)
    assert not missing, '\n'.join(f'{k!r} <- {v}' for k, v in sorted(missing.items()))


@pytest.mark.parametrize('lang', _files())
def test_placeholders_are_preserved(lang):
    bad = []
    for key, value in _table(lang).items():
        if sorted(_PLACEHOLDER.findall(key)) != sorted(_PLACEHOLDER.findall(str(value))):
            bad.append((key, value))
    assert not bad, bad[:10]


def test_translator_accepts_russian_and_maps_windows_id(tmp_path, monkeypatch):
    import localization

    translator = localization.Translator(tmp_path)
    translator.select('ru')
    assert translator.mode == 'ru'
    translator.select('xx')
    assert translator.mode == 'auto'
