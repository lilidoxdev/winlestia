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
    ROOT / 'src' / 'winjets' / 'builtin' / 'winlestia.lecteur',
    ROOT / 'src' / 'winjets' / 'builtin' / 'winlestia.systeme',
    ROOT / 'src' / 'winjets' / 'builtin' / 'winlestia.marches',
    ROOT / 'src' / 'winjets' / 'builtin' / 'winlestia.stockage',
    ROOT / 'src' / 'winjets' / 'builtin' / 'winlestia.fichiers',
    ROOT / 'src' / 'winjets' / 'builtin' / 'winlestia.notes',
    ROOT / 'src' / 'winjets' / 'builtin' / 'winlestia.agenda',
    ROOT / 'src' / 'winjets' / 'builtin' / 'winlestia.meteo',
    ROOT / 'docs' / 'winjets' / 'examples' / 'com.exemple.citation',
)
TAILLES = ((168, 168), (352, 168), (340, 340), (520, 340), (720, 352))
FAUTES = ('Warning', 'ReferenceError', 'TypeError', 'is not a type', 'Cannot assign', 'Unable to assign', 'Cannot read property',
          'Invalid property', 'is not installed', 'Binding loop', 'Error')

BOUGIES = [[1700000000 + 3600 * i, 100 + i, 105 + i, 95 + i, 102 + i] for i in range(12)]
DONNEES_MARCHES = {
    'market': {
        'value': {
            'live': True,
            'updated': '2026-01-01T00:00:00Z',
            'symbols': {
                nom: {'price': 61234.5, 'change24h': 2.41, 'candles': {tf: BOUGIES for tf in ('1h', '1j', '1s', '1m')}}
                for nom in ('BTC', 'ETH', 'SOL')
            },
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
        'data': {'i-winlestia.marches': DONNEES_MARCHES},
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
        ('winlestia.lecteur', (352, 168), 0),
        ('winlestia.systeme', (536, 536), 2),
        ('winlestia.marches', (720, 536), 1),
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


@pytest.mark.parametrize('opacite,alpha', [(None, None), (0, 0.0), (30, 0.3), (80, 0.8), (100, 1.0), (250, 1.0), (-4, 0.0)])
def test_card_fill_alpha_follows_instance_opacity(qapp, bridge, moteur, opacite, alpha):
    """Alpha du fond de carte : null = voile global ; sinon alpha demandé (borné), teinte m3surface inchangée."""
    comp = QQmlComponent(moteur)
    comp.setData(
        b'import QtQuick' + bytes([10]) + b'import "winjets"' + bytes([10])
        + b'QtObject { property var o: null; property color veil: WinjetTheme.veil; property color fill: WinjetTheme.fill(o) }',
        QUrl.fromLocalFile(str(FRONT / 'qml' / 'probe.qml')),
    )
    obj = comp.create()
    assert obj is not None, [e.toString() for e in comp.errors()]
    obj.setProperty('o', opacite)
    veil, fill = obj.property('veil'), obj.property('fill')
    if alpha is None:
        assert fill == veil
    else:
        assert fill.alphaF() == pytest.approx(alpha, abs=0.01)
        assert (fill.red(), fill.green(), fill.blue()) == (veil.red(), veil.green(), veil.blue())
    obj.deleteLater()


@pytest.mark.parametrize('reglages,attendu', [
    ({'themeMode': 'auto', 'glassOpacity': 0.95}, 94),
    ({'themeMode': 'auto', 'glassOpacity': 0.1}, 98),
    ({'themeMode': 'custom', 'customGlass': True, 'customGlassOpacity': 0.12}, 98),
    ({'themeMode': 'custom', 'customGlass': True, 'customGlassOpacity': 0.02}, 100),
    ({'themeMode': 'custom', 'customGlass': False}, 0),
    ({'themeMode': 'dark'}, 0),
])
def test_default_transparency_follows_shell_glass_modes(qapp, bridge, moteur, reglages, attendu):
    """Sans valeur propre : transparence = celle des panneaux (auto / personnalisé) ; sans verre = 0 % (opaque)."""
    avant = bridge._state.get('settings', {})
    bridge._state['settings'] = {**avant, **reglages}
    bridge.changed.emit()
    comp = QQmlComponent(moteur)
    comp.setData(
        b'import QtQuick' + bytes([10]) + b'import "winjets"' + bytes([10])
        + b'QtObject { property int t: WinjetTheme.defaultTransparency; property color fill: WinjetTheme.fill(null);'
        + b' property color veil: WinjetTheme.veil }',
        QUrl.fromLocalFile(str(FRONT / 'qml' / 'probe.qml')),
    )
    obj = comp.create()
    assert obj is not None, [e.toString() for e in comp.errors()]
    try:
        assert abs(obj.property('t') - attendu) <= 1
        assert obj.property('fill') == obj.property('veil')
    finally:
        obj.deleteLater()
        bridge._state['settings'] = avant
        bridge.changed.emit()


def test_opacity_slider_keeps_value_when_bridge_updates_mid_drag(qapp, bridge, moteur, monkeypatch):
    """Cause du saut : Binding réactivé au relâchement avec l'ancienne valeur Bridge. Ici : mises à jour pendant et après le geste."""
    envoyes = []
    monkeypatch.setattr(bridge, 'action', lambda name, args=None: envoyes.append((name, args)))
    comp = QQmlComponent(moteur, QUrl.fromLocalFile(str(FRONT / 'qml' / 'winjets' / 'WinjetSettingsForm.qml')))
    form = comp.createWithInitialProperties({'inst': {'id': 'i1', 'opacity': None, 'settingsSpec': []}})
    assert form is not None, [e.toString() for e in comp.errors()]
    slider = form.findChild(object, 'winjetOpacity')
    assert slider is not None
    base = {'id': 'i1', 'settingsSpec': []}
    # Geste : l'aperçu local (draft) est posé ; le Bridge republie l'instance (sans opacité) avant puis après le relâchement.
    form.setProperty('draft', 37)
    pump(qapp, 5)
    assert slider.property('value') == 37
    form.setProperty('inst', {**base, 'opacity': None, 'tick': 1})
    pump(qapp, 5)
    assert slider.property('value') == 37 and form.property('draft') == 37
    assert envoyes == []  # aucune écriture backend par tick
    # Écho du backend (opacité stockée = 100 - transparence) : le brouillon s'efface sans changer la valeur affichée.
    form.setProperty('inst', {**base, 'opacity': 63})
    pump(qapp, 5)
    assert form.property('draft') is None
    assert slider.property('value') == 37
    form.deleteLater()


def test_set_opacity_sends_clamped_value(qapp, bridge, monkeypatch):
    sent = []
    monkeypatch.setattr(bridge, 'action', lambda name, args=None: sent.append((name, args)))
    bridge.winjets.setOpacity('i1', 250)
    bridge.winjets.setOpacity('i1', None)
    assert sent == [('winjets.setOpacity', {'instanceId': 'i1', 'opacity': 100}),
                    ('winjets.setOpacity', {'instanceId': 'i1', 'opacity': None})]


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
    bridge.winjets.requestSettings('i-winlestia.marches')
    pump(qapp, 30)
    assert obj.property('confirming') is True
    assert obj.property('openSettings') == 'i-winlestia.marches'
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
    donnees['i-winlestia.marches'] = {'market': {'value': {'live': False, 'symbols': {}}, 'updated': 'x'}}
    bridge._state['winjets'] = {**etat, 'data': donnees}
    bridge.winjets.refresh_state()
    assert vus == ['i-winlestia.marches']
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


def _tout(obj, racine):
    inst = dict(obj.property('inst'))
    inst['layouts'] = [{'minWidth': 0, 'root': racine}]
    obj.setProperty('inst', inst)


def _trouver(obj, test):
    """Parcours des items visuels (les contenus de Loader ne sont pas des enfants QObject) et de leurs enfants QObject."""
    vus, pile, out = [], [obj], []  # références gardées : un id() de wrapper temporaire serait réutilisé
    while pile:
        o = pile.pop()
        if any(o is v or o == v for v in vus):
            continue
        vus.append(o)
        if test(o):
            out.append(o)
        pile.extend(o.children())
        if hasattr(o, 'childItems'):
            pile.extend(o.childItems())
        item = o.property('item') if o.metaObject().className().startswith('QQuickLoader') else None
        if item is not None:
            pile.append(item)
    return out


def _taille_police(obj, texte):
    textes = _trouver(obj, lambda o: o.property('text') == texte and o.property('font') is not None)
    assert textes, texte
    return textes[0].property('font').pointSizeF()


def test_scale_grows_text_with_card_size(qapp, bridge, moteur):
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    _tout(obj, {'type': 'column', 'children': [{'type': 'text', 'text': 'Bonjour', 'style': 'title'}]})
    pump(qapp, 10)
    base = obj.property('scale')
    petit = _taille_police(obj, 'Bonjour')
    obj.setProperty('width', 704.0)
    obj.setProperty('height', 704.0)
    pump(qapp, 10)
    assert obj.property('scale') == pytest.approx(base * 2)
    assert _taille_police(obj, 'Bonjour') == pytest.approx(petit * 2, rel=0.02)
    obj.setProperty('width', 100.0)  # plancher 0,75
    obj.setProperty('height', 100.0)
    pump(qapp, 10)
    assert obj.property('scale') == pytest.approx(0.75)
    obj.setProperty('width', 4000.0)  # plafond 3
    obj.setProperty('height', 4000.0)
    pump(qapp, 10)
    assert obj.property('scale') == pytest.approx(3.0)
    obj.deleteLater()
    pump(qapp)


def test_clock_seconds_timer_follows_visibility_and_pause(qapp, bridge, moteur, journal):
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    _tout(obj, {'type': 'column', 'children': [{'type': 'clock', 'variant': 'digital', 'seconds': True}]})
    pump(qapp, 10)

    def minuteurs():
        # L'intervalle est recalé sur la seconde.
        return [o for o in _trouver(obj,
            lambda o: o.metaObject().className().startswith('QQmlTimer'))]

    assert minuteurs(), 'minuteur des secondes introuvable'
    assert all(m.property('running') for m in minuteurs())
    assert obj.property('secondsUsers') == 1
    obj.setProperty('paused', True)
    pump(qapp, 5)
    assert not any(m.property('running') for m in minuteurs())
    obj.setProperty('paused', False)
    pump(qapp, 5)
    assert all(m.property('running') for m in minuteurs())
    obj.setProperty('active', False)
    pump(qapp, 5)
    assert not any(m.property('running') for m in minuteurs())
    obj.setProperty('active', True)
    _tout(obj, {'type': 'column', 'children': [{'type': 'clock', 'variant': 'digital'}]})
    pump(qapp, 10)
    assert obj.property('secondsUsers') == 0
    assert not any(m.property('running') for m in minuteurs())
    assert not journal.alertes(), journal.alertes()
    obj.deleteLater()
    pump(qapp)


# --- Médias, visualiseur, bougies ------------------------------------------------------------------------------------------

BOUGIES = [[1700000000 + 3600 * i, 100 + i, 105 + i, 98 + i, 103 + i - (2 * i % 5)] for i in range(24)]
MARCHE = {'market': {'live': True, 'updated': '2026-01-01T00:00:00Z',
                     'symbols': {'BTC': {'price': 61234.5, 'change24h': 0.0234, 'candles': {'1h': BOUGIES, '1j': BOUGIES[:5]}}}}}
SERIE = '{{ data.market.symbols[state.pair].candles[state.tf] }}'


def _lecteur(bridge, **champs):
    """Remplace l'instantané du lecteur Windows (le vrai relais backend est absent des tests)."""
    bridge._player.data = {'available': True, 'title': 'Titre', 'artist': 'Artiste', 'app': 'Spotify', 'playing': False,
                           'position': 30.0, 'duration': 200.0, 'canNext': True, 'canPrevious': True, 'canToggle': True, **champs}
    bridge._player.changed.emit()


def _aucun_lecteur(bridge):
    bridge._player.data = {}
    bridge._player.changed.emit()


def _bloc(obj, nom_propriete):
    trouves = _trouver(obj, lambda o: o.property(nom_propriete) is not None and o.metaObject().className().startswith(('Chart', 'Media',
        'Visualizer')))
    assert trouves, nom_propriete
    return trouves[0]


def _avec_marche(obj, racine):
    inst = dict(obj.property('inst'))
    inst['data'] = MARCHE
    inst['state'] = {'pair': 'BTC', 'tf': '1h'}
    inst['layouts'] = [{'minWidth': 0, 'root': racine}]
    obj.setProperty('inst', inst)


def test_candles_render_from_indexed_binding(qapp, bridge, moteur, journal):
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    _avec_marche(obj, {'type': 'column', 'children': [
        {'type': 'chart', 'variant': 'candles', 'series': SERIE, 'valueFormat': 'currency', 'hover': True, 'grow': 1}]})
    pump(qapp, 20)
    bloc = _bloc(obj, 'hoverIndex')
    assert len(bloc.property('candles').toVariant()) == 24
    chemins = bloc.property('paths').toVariant()
    assert chemins[0] and chemins[1]  # haussières et baissières
    # Changer d'horizon dans l'état re-résout l'indice.
    inst = dict(obj.property('inst'))
    inst['state'] = {'pair': 'BTC', 'tf': '1j'}
    obj.setProperty('inst', inst)
    pump(qapp, 20)
    assert len(bloc.property('candles').toVariant()) == 5
    assert not journal.alertes(), journal.alertes()
    obj.deleteLater()
    pump(qapp)


def test_hover_writes_state_and_clears_on_exit(qapp, bridge, moteur, monkeypatch):
    calls = []
    monkeypatch.setattr(bridge, 'action', lambda name, args=None: calls.append((name, args)))
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    _avec_marche(obj, {'type': 'column', 'children': [
        {'type': 'chart', 'variant': 'candles', 'series': SERIE, 'hover': True, 'grow': 1}]})
    pump(qapp, 20)
    bloc = _bloc(obj, 'hoverIndex')
    from PySide6.QtCore import QMetaObject, Q_ARG

    QMetaObject.invokeMethod(bloc, 'publish', Q_ARG('int', 3))
    assert bloc.property('hoverIndex') == 3
    # Survol éphémère : `state._hover` vit dans le moteur QML, rien n'est envoyé au backend.
    assert obj.property('transientState').toVariant() == {'_hover': BOUGIES[3][4]}
    assert not [c for c in calls if c[0] == 'winjets.setState']
    QMetaObject.invokeMethod(bloc, 'publish', Q_ARG('int', 3))  # même bougie : aucune écriture
    QMetaObject.invokeMethod(bloc, 'publish', Q_ARG('int', -1))
    assert obj.property('transientState').toVariant() == {}
    assert not calls
    obj.deleteLater()
    pump(qapp)


def test_hover_clears_when_paused(qapp, bridge, moteur, monkeypatch):
    calls = []
    monkeypatch.setattr(bridge, 'action', lambda name, args=None: calls.append((name, args)))
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    _avec_marche(obj, {'type': 'column', 'children': [
        {'type': 'chart', 'variant': 'candles', 'series': SERIE, 'hover': True, 'grow': 1}]})
    pump(qapp, 20)
    bloc = _bloc(obj, 'hoverIndex')
    from PySide6.QtCore import QMetaObject, Q_ARG

    QMetaObject.invokeMethod(bloc, 'publish', Q_ARG('int', 2))
    obj.setProperty('paused', True)
    pump(qapp, 5)
    assert bloc.property('hoverIndex') == -1
    assert obj.property('transientState').toVariant() == {}
    assert not calls
    obj.deleteLater()
    pump(qapp)


def test_fmt_and_coalesce_in_text_blocks(qapp, bridge, moteur):
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    _avec_marche(obj, {'type': 'column', 'children': [
        {'type': 'text', 'text': "{{ fmt(state.hover ?? data.market.symbols[state.pair].price, 'currency') }}"}]})
    pump(qapp, 20)
    assert _trouver(obj, lambda o: o.property('text') == '61\xa0234\xa0$')
    obj.deleteLater()
    pump(qapp)


def test_media_block_empty_state_without_player(qapp, bridge, moteur, journal):
    _aucun_lecteur(bridge)
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    _tout(obj, {'type': 'column', 'children': [{'type': 'media'}]})
    pump(qapp, 20)
    vide = _trouver(obj, lambda o: o.property('objectName') == 'mediaEmpty')
    assert vide and vide[0].property('text') == 'Aucune lecture' and vide[0].property('visible')
    assert not journal.alertes(), journal.alertes()
    obj.deleteLater()
    pump(qapp)


def test_media_block_shows_track_and_ctx_media(qapp, bridge, moteur, journal):
    _lecteur(bridge)
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    _tout(obj, {'type': 'column', 'children': [{'type': 'media'}, {'type': 'text', 'text': '{{ media.artist }}/{{ media.app }}'}]})
    pump(qapp, 20)
    assert _trouver(obj, lambda o: o.property('text') == 'Titre')
    assert not _trouver(obj, lambda o: o.property('objectName') == 'mediaEmpty' and o.property('visible'))
    assert _trouver(obj, lambda o: o.property('text') == 'Artiste/Spotify'), ' | '.join(o.property('text') for o in _trouver(obj,
        lambda o: isinstance(o.property('text'), str)))
    assert not journal.alertes(), journal.alertes()
    obj.deleteLater()
    pump(qapp)
    _aucun_lecteur(bridge)


def test_media_actions_call_player_methods(qapp, bridge, moteur, monkeypatch):
    calls = []
    monkeypatch.setattr(bridge, 'action', lambda name, args=None: calls.append((name, args)))
    from PySide6.QtCore import QMetaObject, Q_ARG

    def lancer(obj, type_):
        QMetaObject.invokeMethod(obj, 'runAction', Q_ARG('QVariant', {'type': type_}))

    _lecteur(bridge)
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    pump(qapp)
    for type_ in ('media.playPause', 'media.next', 'media.prev'):
        lancer(obj, type_)
    assert [a[1]['action'] for a in calls if a[0] == 'media'] == ['toggle', 'next', 'previous']
    calls.clear()
    _lecteur(bridge, canNext=False, canPrevious=False, canToggle=False)  # capacités fausses : sans effet
    for type_ in ('media.playPause', 'media.next', 'media.prev'):
        lancer(obj, type_)
    assert not [a for a in calls if a[0] == 'media']
    obj.deleteLater()
    pump(qapp)
    _aucun_lecteur(bridge)


def test_visualizer_subscribes_only_when_playing_and_live(qapp, bridge, moteur, journal):
    _aucun_lecteur(bridge)
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    _tout(obj, {'type': 'column', 'children': [{'type': 'visualizer', 'bars': 16}]})
    pump(qapp, 20)
    bloc = _bloc(obj, 'subscribed')
    assert bloc.property('subscribed') is False and bloc.property('count') == 16
    assert not getattr(bridge, '_bars_want', False)
    _lecteur(bridge, playing=True)
    pump(qapp, 5)
    # Des cartes d'autres tests peuvent encore vivre : on compte les références de ce bloc, relativement.
    base = bridge._bars_refs - 1
    assert bloc.property('subscribed') is True and bridge._bars_want is True
    obj.setProperty('paused', True)
    pump(qapp, 5)
    assert bloc.property('subscribed') is False and bridge._bars_refs == base
    obj.setProperty('paused', False)
    pump(qapp, 5)
    assert bloc.property('subscribed') is True and bridge._bars_refs == base + 1
    obj.setProperty('active', False)
    pump(qapp, 5)
    assert bloc.property('subscribed') is False and bridge._bars_refs == base
    obj.setProperty('active', True)
    _lecteur(bridge, playing=False)
    pump(qapp, 5)
    assert bloc.property('subscribed') is False
    assert not [m for m in _trouver(obj, lambda o: o.metaObject().className().startswith('QQmlTimer')) if m.property('running')]
    assert not journal.alertes(), journal.alertes()
    obj.deleteLater()
    pump(qapp)
    _aucun_lecteur(bridge)


def test_normal_state_persists_once_per_change(qapp, bridge, moteur, monkeypatch):
    calls = []
    monkeypatch.setattr(bridge, 'action', lambda name, args=None: calls.append((name, args)))
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    from PySide6.QtCore import QMetaObject, Q_ARG

    for _ in range(3):
        QMetaObject.invokeMethod(obj, 'setState', Q_ARG('QString', 'mode'), Q_ARG('QVariant', 'b'))
    assert [c[0] for c in calls] == ['winjets.setState']
    obj.deleteLater()
    pump(qapp)


def test_audio_refcount_is_shared_between_consumers(qapp, bridge):
    bridge._bars_refs = 0
    bridge.audioBarsRef(True)   # tableau de bord
    bridge.audioBarsRef(True)   # visualiseur Winjet
    assert bridge._bars_want is True and bridge.client.subscription.get('bars') is True
    bridge.audioBarsRef(False)  # un seul consommateur part : l'autre garde le flux
    assert bridge._bars_want is True
    bridge.audioBarsRef(False)
    assert bridge._bars_want is False and not bridge.client.subscription.get('bars')
    bridge.audioBarsRef(False)  # rendu en trop : le compteur ne devient jamais négatif
    bridge.audioBarsRef(True)
    assert bridge._bars_want is True
    bridge.audioBarsRef(False)
    assert bridge._bars_want is False


def test_visualizer_bars_clamped(qapp, bridge, moteur):
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    _tout(obj, {'type': 'column', 'children': [{'type': 'visualizer', 'bars': 500}]})
    pump(qapp, 10)
    assert _bloc(obj, 'subscribed').property('count') == 64
    obj.deleteLater()
    pump(qapp)


def _carte(engine, bridge, editing):
    comp = QQmlComponent(engine, QUrl.fromLocalFile(str(FRONT / 'qml' / 'winjets' / 'WinjetCard.qml')))
    obj = comp.create()
    assert obj is not None, [e.toString() for e in comp.errors()]
    obj.comp = comp
    obj.setProperty('inst', bridge.winjets.instance('i-test.tout'))
    obj.setProperty('editing', editing)
    obj.setProperty('width', 300.0)
    obj.setProperty('height', 200.0)
    from PySide6.QtQuick import QQuickWindow
    fen = QQuickWindow()
    fen.resize(300, 200)
    obj.setParentItem(fen.contentItem())
    fen.show()
    return obj, fen


def test_double_click_toggles_edit_mode(qapp, bridge, moteur, monkeypatch):
    from PySide6.QtCore import QPoint, Qt
    from PySide6.QtTest import QTest
    monkeypatch.setattr(bridge, 'action', lambda *a, **k: None)
    bridge.winjets.setEditMode(False)
    obj, fen = _carte(moteur, bridge, False)
    pump(qapp, 5)
    QTest.mouseDClick(fen, Qt.LeftButton, Qt.NoModifier, QPoint(150, 100))
    pump(qapp, 5)
    assert bridge.winjets.editMode is True
    obj.setProperty('editing', True)  # la fenêtre propage l'état ; ici on le fait à la main
    pump(qapp, 5)
    QTest.mouseDClick(fen, Qt.LeftButton, Qt.NoModifier, QPoint(150, 100))
    pump(qapp, 5)
    assert bridge.winjets.editMode is False
    fen.close()


def test_single_click_and_tiny_drag_do_not_change_edit_mode_or_save(qapp, bridge, moteur, monkeypatch):
    from PySide6.QtCore import QPoint, Qt
    from PySide6.QtTest import QTest
    envoyes = []
    monkeypatch.setattr(bridge, 'action', lambda *a, **k: envoyes.append(a))
    bridge.winjets.setEditMode(False)
    envoyes.clear()
    obj, fen = _carte(moteur, bridge, False)
    pump(qapp, 5)
    QTest.mouseClick(fen, Qt.LeftButton, Qt.NoModifier, QPoint(150, 100))
    pump(qapp, 5)
    assert bridge.winjets.editMode is False and envoyes == []
    fen.close()


def test_gesture_commits_once_and_alt_is_forwarded(qapp, bridge, moteur, monkeypatch):
    envoyes = []
    monkeypatch.setattr(bridge, 'action', lambda *a, **k: envoyes.append(a))
    bridge.winjets.beginGesture('i-test.tout')
    r = bridge.winjets.snapMove('i-test.tout', 203.0, 203.0, 352.0, 168.0, False)
    assert (r['x'], r['y']) == (203, 203) and r['guides'] == []
    assert bridge.winjets.gestureRect['w'] == 352
    assert envoyes == []  # aucun envoi pendant le geste
    bridge.winjets.endGesture()
    assert bridge.winjets.gestureRect == {}


def test_gesture_state_cleared_when_instance_removed_mid_gesture(qapp, bridge, moteur, monkeypatch):
    monkeypatch.setattr(bridge, 'action', lambda *a, **k: None)
    etat = bridge._state['winjets']
    avant = etat['instances']
    bridge.winjets.beginGesture('i-test.tout')
    bridge.winjets.snapMove('i-test.tout', 203.0, 203.0, 352.0, 168.0, False)
    assert bridge.winjets.gestureRect
    try:
        bridge._state['winjets'] = {**etat, 'instances': [i for i in avant if i['id'] != 'i-test.tout']}
        bridge.winjets.refresh_state()
        assert bridge.winjets.gestureRect == {} and bridge.winjets._gesture is None
    finally:
        bridge._state['winjets'] = etat
        bridge.winjets.refresh_state()
        pump(qapp, 5)


def test_edit_exit_mid_gesture_releases_window_gesture(qapp, bridge, moteur, monkeypatch):
    from PySide6.QtCore import QMetaObject, Qt
    monkeypatch.setattr(bridge, 'action', lambda *a, **k: None)
    bridge.winjets.setEditMode(True)
    comp = QQmlComponent(moteur, QUrl.fromLocalFile(str(FRONT / 'qml' / 'winjets' / 'WinjetWindow.qml')))
    win = comp.createWithInitialProperties({'instanceId': 'i-test.tout'})
    assert win is not None, [e.toString() for e in comp.errors()]
    try:
        pump(qapp, 5)
        QMetaObject.invokeMethod(win, 'beginGesture', Qt.DirectConnection)
        assert win.property('gesture') is True
        bridge.winjets.setEditMode(False)
        pump(qapp, 5)
        assert win.property('gesture') is False
    finally:
        win.deleteLater()
        bridge.winjets.setEditMode(False)


def test_opacity_draft_clears_on_same_value_and_by_fallback_timer(qapp, bridge, moteur, monkeypatch):
    from PySide6.QtCore import QMetaObject, Qt, Q_ARG
    from PySide6.QtTest import QTest
    monkeypatch.setattr(bridge, 'action', lambda *a, **k: None)
    comp = QQmlComponent(moteur, QUrl.fromLocalFile(str(FRONT / 'qml' / 'winjets' / 'WinjetSettingsForm.qml')))
    form = comp.createWithInitialProperties({'inst': {'id': 'i1', 'opacity': 63, 'settingsSpec': []}})
    assert form is not None, [e.toString() for e in comp.errors()]
    try:
        QMetaObject.invokeMethod(form, 'commitOpacity', Qt.DirectConnection, Q_ARG(float, 37.0))
        assert form.property('draft') is None  # même valeur que stockée
        assert QMetaObject.invokeMethod(form, 'commitOpacity', Qt.DirectConnection, Q_ARG(float, 20.0))
        assert form.property('draft') == 20  # pas de retour en arrière avant l'écho
        QTest.qWait(1700)  # backend muet ou rejet : le minuteur de secours efface
        assert form.property('draft') is None
    finally:
        form.deleteLater()
