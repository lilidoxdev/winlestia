"""QML des Winjets hors écran : le rendu charge les vrais paquets (inclus et exemple) à plusieurs tailles sans aucun avertissement QML,
la mise en page suit les seuils minWidth/minHeight, chaque type de bloc se charge, et la page Réglages > Winjets s'instancie.

Aucune fenêtre bureau réelle, aucun réseau : le Bridge est celui du test de fumée (tube neutralisé), l'état Winjets est injecté.
"""
import json
import os
import sys
import tempfile
from pathlib import Path

import pytest

os.environ.setdefault('QT_QPA_PLATFORM', 'offscreen')
os.environ.setdefault('WINLESTIA_DATA_DIR', tempfile.mkdtemp(prefix='wl-winjets-'))
pytest.importorskip('PySide6.QtQml')

ROOT = Path(__file__).resolve().parent.parent
FRONT = ROOT / 'src' / 'frontend'
sys.path.insert(0, str(FRONT))
sys.path.insert(0, str(Path(__file__).resolve().parent))

from PySide6.QtCore import QUrl  # noqa: E402
from PySide6.QtQml import QQmlApplicationEngine, QQmlComponent  # noqa: E402

from test_qml_runtime import bridge, messages, pump, qapp  # noqa: E402,F401  (fixtures de session partagées)

PAQUETS = (
    ROOT / 'src' / 'winjets' / 'builtin' / 'winlestia.horloge',
    ROOT / 'src' / 'winjets' / 'builtin' / 'winlestia.calendrier',
    ROOT / 'src' / 'winjets' / 'builtin' / 'winlestia.graphique-btc',
    ROOT / 'docs' / 'winjets' / 'examples' / 'com.exemple.citation',
)
TAILLES = ((168, 168), (352, 168), (340, 340), (520, 340), (720, 352))
FAUTES = ('Warning', 'ReferenceError', 'TypeError', 'is not a type', 'Cannot assign', 'Unable to assign', 'Cannot read property',
          'Invalid property', 'is not installed', 'Binding loop', 'Error')

DONNEES_BTC = {
    'demo': {
        'value': {
            'price': 61234.5,
            'change24h': 0.0234,
            'points7d': [60000, 60500, 59800, 61000, 61500, 61200, 61234],
            'daily': [{'day': 'Lun', 'change': '+1,2 %', 'price': '61 000'}, {'day': 'Mar', 'change': '-0,4 %', 'price': '60 800'}],
        },
        'updated': '2026-01-01T00:00:00Z',
    }
}

# Un exemplaire de chaque bloc de la liste fermée (§5), conteneurs compris.
SYNTHETIQUE = {
    'size': {'default': [352, 352], 'min': [168, 168], 'max': [720, 720]},
    'settings': [{'id': 'secondes', 'type': 'toggle', 'label': 'Secondes', 'default': True}],
    'state': {'mode': 'a'},
    'layouts': [
        {
            'minWidth': 0,
            'root': {
                'type': 'column',
                'gap': 8,
                'children': [
                    {'type': 'text', 'text': 'Titre {{ settings.secondes }}', 'style': 'title'},
                    {'type': 'row', 'children': [
                        {'type': 'clock', 'variant': 'digital', 'showSeconds': '{{ settings.secondes }}'},
                        {'type': 'stat', 'label': 'Valeur', 'value': '{{ 0.5 | percent }}', 'delta': '+2,3'},
                    ]},
                    {'type': 'calendar', 'variant': 'month', 'firstDay': 'monday'},
                    {'type': 'chart', 'variant': 'line', 'series': [1, 3, 2, 5], 'axes': True},
                    {'type': 'chart', 'variant': 'bars', 'series': [1, 3, 2, 5]},
                    {'type': 'progress', 'variant': 'ring', 'value': 0.4, 'label': '40 %'},
                    {'type': 'progress', 'variant': 'bar', 'value': 0.7},
                    {'type': 'dots', 'pattern': 'torus'},
                    {'type': 'dots', 'matrix': [[0, 1], [1, 0]]},
                    {'type': 'buttons', 'bind': 'state.mode', 'options': [{'value': 'a', 'label': 'A'}, {'value': 'b', 'label': 'B'}]},
                    {'type': 'list', 'items': [{'t': 'x'}, {'t': 'y'}], 'title': 't', 'max': 2},
                    {'type': 'divider'},
                    {'type': 'spacer'},
                    {'type': 'grid', 'columns': 2, 'children': [{'type': 'text', 'text': 'a'}, {'type': 'text', 'text': 'b'}]},
                    {'type': 'stack', 'children': [{'type': 'image', 'src': 'assets/absent.png'}, {'type': 'text', 'text': 'sur'}]},
                ],
            },
        },
        {'minWidth': 520, 'minHeight': 340, 'root': {'type': 'text', 'text': 'large'}},
    ],
}


def charger(dossier):
    manifest = json.loads((dossier / 'manifest.json').read_text('utf-8'))
    definition = json.loads((dossier / 'widget.json').read_text('utf-8'))
    return {
        'id': manifest['id'], 'version': manifest.get('version', ''), 'name': manifest['name'], 'author': manifest.get('author', ''),
        'description': manifest.get('description', ''), 'builtin': True, 'dir': str(dossier),
        'preview': str(dossier / 'preview.png'),
        'network': ((manifest.get('permissions') or {}).get('network') or []),
        'definition': definition, 'manifest': manifest,
    }


@pytest.fixture(scope='module')
def moteur(qapp, bridge):
    engine = QQmlApplicationEngine()
    engine.addImportPath(str(FRONT / 'qml'))
    engine.rootContext().setContextProperty('Bridge', bridge)
    paquets = [charger(d) for d in PAQUETS]
    paquets.append({'id': 'test.tout', 'name': 'Tout', 'author': 'Test', 'builtin': False, 'dir': '', 'network': [],
                    'definition': SYNTHETIQUE, 'manifest': {}})
    instances = [{'id': 'i-' + p['id'], 'packageId': p['id'], 'hidden': False, 'settings': {}, 'state': {}} for p in paquets]
    bridge._state['winjets'] = {
        'editMode': False, 'packages': paquets, 'instances': instances,
        'data': {'i-winlestia.graphique-btc': DONNEES_BTC},
        'lastImport': {},
    }
    bridge.winjets.refresh_state()
    pump(qapp)
    yield engine
    engine.deleteLater()
    pump(qapp)


@pytest.fixture
def journal(messages):
    debut = len(messages)

    class Vue:
        def alertes(self):
            return [m for k, m in messages[debut:] if k >= 1]

    return Vue()


def rendu(engine, bridge, instance_id, w, h):
    comp = QQmlComponent(engine, QUrl.fromLocalFile(str(FRONT / 'qml' / 'winjets' / 'WinjetRenderer.qml')))
    obj = comp.create()
    assert obj is not None, [e.toString() for e in comp.errors()]
    obj.comp = comp  # le composant doit survivre à l'objet
    obj.setProperty('inst', bridge.winjets.instance(instance_id))
    obj.setProperty('width', float(w))
    obj.setProperty('height', float(h))
    return obj


@pytest.mark.parametrize('dossier', PAQUETS, ids=lambda d: d.name)
@pytest.mark.parametrize('taille', TAILLES, ids=lambda t: '%dx%d' % t)
def test_real_packages_render_without_warnings(qapp, bridge, moteur, journal, dossier, taille):
    pid = json.loads((dossier / 'manifest.json').read_text('utf-8'))['id']
    obj = rendu(moteur, bridge, 'i-' + pid, *taille)
    pump(qapp, 20)
    assert obj.property('layout') is not None
    assert not journal.alertes(), journal.alertes()
    obj.deleteLater()
    pump(qapp)


@pytest.mark.parametrize(
    'pid,taille,attendu',
    [
        ('winlestia.horloge', (168, 168), 0),
        ('winlestia.horloge', (352, 168), 1),
        ('winlestia.horloge', (340, 168), 1),
        ('winlestia.horloge', (340, 340), 2),
        ('winlestia.calendrier', (340, 168), 0),
        ('winlestia.calendrier', (340, 340), 1),
        ('winlestia.graphique-btc', (520, 168), 1),
        ('winlestia.graphique-btc', (520, 340), 2),
        ('com.exemple.citation', (340, 340), 1),
    ],
)
def test_layout_switches_at_breakpoints(qapp, bridge, moteur, pid, taille, attendu):
    obj = rendu(moteur, bridge, 'i-' + pid, *taille)
    pump(qapp)
    assert obj.property('layoutIndex') == attendu
    # Redimensionnement en direct : la sélection est recalculée sans recréer le rendu.
    obj.setProperty('width', 100.0)
    obj.setProperty('height', 100.0)
    pump(qapp)
    assert obj.property('layoutIndex') == 0
    obj.deleteLater()
    pump(qapp)


@pytest.mark.parametrize('taille', ((168, 168), (352, 352), (520, 340)), ids=lambda t: '%dx%d' % t)
def test_every_block_type_loads(qapp, bridge, moteur, journal, taille):
    obj = rendu(moteur, bridge, 'i-test.tout', *taille)
    pump(qapp, 30)
    assert not journal.alertes(), journal.alertes()
    obj.deleteLater()
    pump(qapp)


def test_unknown_block_and_bad_enum_do_not_warn(qapp, bridge, moteur, journal):
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    inst = dict(bridge.winjets.instance('i-test.tout'))
    inst['layouts'] = [{'minWidth': 0, 'root': {'type': 'column', 'children': [
        {'type': 'inconnu'},
        {'type': 'calendar', 'variant': '{{ settings.nope }}', 'firstDay': 'jeudi'},
        {'type': 'chart', 'variant': 'camembert', 'series': 'pas un tableau'},
        {'type': 'clock', 'variant': 'cadran'},
    ]}}]
    obj.setProperty('inst', inst)
    pump(qapp, 20)
    assert not journal.alertes(), journal.alertes()
    obj.deleteLater()
    pump(qapp)


def test_button_click_writes_instance_state(qapp, bridge, moteur, monkeypatch):
    calls = []
    monkeypatch.setattr(bridge, 'action', lambda name, args=None: calls.append((name, args)))
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    pump(qapp)
    from PySide6.QtCore import QMetaObject, Q_ARG

    QMetaObject.invokeMethod(obj, 'setState', Q_ARG('QString', 'mode'), Q_ARG('QVariant', 'b'))
    assert calls and calls[-1][0] == 'winjets.setState'
    assert calls[-1][1] == {'instanceId': 'i-test.tout', 'key': 'mode', 'value': 'b'}
    obj.deleteLater()
    pump(qapp)


def luminance(c):
    def lin(v):
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4

    return 0.2126 * lin(c[0]) + 0.7152 * lin(c[1]) + 0.0722 * lin(c[2])


def contrast(a, b):
    la, lb = sorted((luminance(a), luminance(b)), reverse=True)
    return (la + 0.05) / (lb + 0.05)


@pytest.mark.parametrize('fond', ((0.0, 0.0, 0.0), (1.0, 1.0, 1.0)), ids=('noir', 'blanc'))
def test_secondary_text_contrast_on_veil(qapp, bridge, moteur, fond):
    """Texte secondaire >= 4.5:1 sur le voile, que le bureau derrière soit noir ou blanc."""
    comp = QQmlComponent(moteur)
    comp.setData(
        b'import QtQuick' + bytes([10]) + b'import "winjets"' + bytes([10])
        + b'QtObject { property color veil: WinjetTheme.veil; property color text: WinjetTheme.textSecondary }',
        QUrl.fromLocalFile(str(FRONT / 'qml' / 'probe.qml')),
    )
    obj = comp.create()
    assert obj is not None, [e.toString() for e in comp.errors()]
    veil, text = obj.property('veil'), obj.property('text')
    alpha = veil.alphaF()
    surface = tuple(alpha * c + (1 - alpha) * f for c, f in zip((veil.redF(), veil.greenF(), veil.blueF()), fond))
    assert contrast((text.redF(), text.greenF(), text.blueF()), surface) >= 4.5
    obj.deleteLater()


def test_settings_page_loads_with_fixture_state(qapp, bridge, moteur, journal, monkeypatch):
    monkeypatch.setattr(bridge, 'action', lambda name, args=None: None)
    bridge._state['winjets'] = {**bridge._state['winjets'], 'lastImport': {
        'ok': True, 'inspect': True, 'updated': False, 'packageId': 'test.tout',
        'package': {'id': 'test.tout', 'name': 'Tout', 'author': 'Test', 'network': ['api.example.com'], 'preview': ''},
    }}
    bridge.winjets.refresh_state()
    comp = QQmlComponent(moteur, QUrl.fromLocalFile(str(FRONT / 'qml' / 'settings' / 'PageWinjets.qml')))
    obj = comp.create()
    assert obj is not None, [e.toString() for e in comp.errors()]
    obj.setProperty('inspectedPath', 'C:/tmp/x.zip')
    bridge.winjets.requestSettings('i-winlestia.graphique-btc')
    pump(qapp, 30)
    assert obj.property('confirming') is True
    assert obj.property('openSettings') == 'i-winlestia.graphique-btc'
    assert not journal.alertes(), journal.alertes()
    obj.deleteLater()
    pump(qapp)
    bridge._state['winjets'] = {**bridge._state['winjets'], 'lastImport': {}}
    bridge.winjets.refresh_state()


def test_only_the_changed_instance_is_notified(qapp, bridge, moteur):
    vus = []
    bridge.winjets.instanceChanged.connect(vus.append)
    etat = bridge._state['winjets']
    donnees = dict(etat['data'])
    donnees['i-winlestia.graphique-btc'] = {'demo': {'value': {'price': 1.0}, 'updated': 'x'}}
    bridge._state['winjets'] = {**etat, 'data': donnees}
    bridge.winjets.refresh_state()
    assert vus == ['i-winlestia.graphique-btc']
    vus.clear()
    bridge.winjets.refresh_state()  # rien de neuf : aucun signal
    assert vus == []


def test_prototype_names_are_not_block_types(qapp, bridge, moteur, journal):
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    inst = dict(bridge.winjets.instance('i-test.tout'))
    inst['layouts'] = [{'minWidth': 0, 'root': {'type': 'column', 'children': [{'type': 'constructor'}, {'type': 'toString'}]}}]
    obj.setProperty('inst', inst)
    pump(qapp, 10)
    assert not journal.alertes(), journal.alertes()
    obj.deleteLater()
    pump(qapp)
