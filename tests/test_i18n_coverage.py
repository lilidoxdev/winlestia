# Chaque qsTr("...") des fichiers QML listés doit avoir une traduction non vide en fr, en et es.
import json
import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
QML = ROOT / 'src' / 'frontend' / 'qml'
I18N = ROOT / 'src' / 'frontend' / 'i18n'
LANGS = ('fr', 'en', 'es', 'ru')

# Fichiers dont tous les qsTr doivent être traduits (chemins relatifs à src/frontend/qml). Liste simple : on peut y ajouter des fichiers.
COVERED_QML = [
    'settings/PagePersonalisation.qml',
    'settings/OptionPreview.qml',
    'settings/SettingsIndex.qml',
    'Tutorial.qml',
    'TutorialCard.qml',
    'KeyCap.qml',
    'settings/SystemPreview.qml',
    'settings/PagePreview.qml',
    'settings/PageDocks.qml',
    'settings/PageShortcuts.qml',
    'settings/PageMedia.qml',
    'settings/PageApps.qml',
    'settings/PageFiles.qml',
    'settings/PageGeneral.qml',
    'settings/PageLock.qml',
]

# Littéral de chaîne QML (guillemets simples ou doubles, échappements compris) passé à qsTr(...).
_QSTR = re.compile(r"""\bqsTr\(\s*(?P<q>["'])(?P<body>(?:\\.|(?!(?P=q)).)*)(?P=q)\s*[,)]""", re.S)
_ESCAPES = {'n': '\n', 't': '\t', 'r': '\r', 'b': '\b', 'f': '\f', '0': '\0'}


def _unescape(body):
    return re.sub(
        r'\\(?:u([0-9a-fA-F]{4})|(.))',
        lambda m: chr(int(m.group(1), 16)) if m.group(1) else _ESCAPES.get(m.group(2), m.group(2)),
        body,
        flags=re.S,
    )


def extract_qstr(text):
    """Tous les littéraux passés à qsTr(), dans l'ordre, sans doublon."""
    seen = []
    for m in _QSTR.finditer(text):
        s = _unescape(m.group('body'))
        if s not in seen:
            seen.append(s)
    return seen


def _tables():
    return {lang: json.loads((I18N / f'{lang}.json').read_text(encoding='utf-8')) for lang in LANGS}


def test_extract_handles_quotes_and_escapes():
    src = r'''a: qsTr("Dis \"bonjour\" %1") b: qsTr('l\'été') d: qsTr("deux\nlignes", "ctx")'''
    assert extract_qstr(src) == ['Dis "bonjour" %1', "l'été", 'deux\nlignes']


@pytest.mark.parametrize('rel', COVERED_QML)
def test_qml_strings_are_translated(rel):
    path = QML / rel
    assert path.exists(), rel
    keys = extract_qstr(path.read_text(encoding='utf-8'))
    tables = _tables()
    missing = [(lang, k) for lang in LANGS for k in keys if not str(tables[lang].get(k, '')).strip()]
    assert not missing, missing


@pytest.mark.parametrize('name', ['OptionPreview.qml', 'PagePersonalisation.qml'])
def test_no_hardcoded_colour(name):
    text = (QML / 'settings' / name).read_text(encoding='utf-8')
    assert not re.search(r'["\']#[0-9a-fA-F]{3,8}["\']', text)
    assert not re.search(r'#[0-9a-fA-F]{3,8}\b', text)
    # Qt.rgba / Qt.rgb avec des nombres littéraux seulement (Qt.alpha(palette, x) reste permis).
    assert not re.search(r'Qt\.rgba?\(\s*[0-9.]+\s*,', text)
