"""Paquets inclus horloge, calendrier et batterie : contrat des paquets et expressions dont ils dépendent.

La validation par schéma et par références de tous les paquets inclus est faite par test_winjets_packages.py (il
énumère le dossier `src/winjets/builtin`) ; ici : les expressions réellement utilisées, évaluées par le moteur.
"""
import json
import sys
from datetime import datetime
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'src' / 'frontend'))

import winjet_expr as ex  # noqa: E402

BUILTIN = ROOT / 'src' / 'winjets' / 'builtin'
NEW = ('winlestia.horloge', 'winlestia.calendrier', 'winlestia.batterie')


def load(pid, name):
    return json.loads((BUILTIN / pid / name).read_text(encoding='utf-8'))


def nodes(node):
    yield node
    for child in node.get('children', []):
        yield from nodes(child)


def ctx(battery, charging=False, **settings):
    levels = load('winlestia.batterie', 'assets/niveaux.json')
    return {'settings': settings, 'data': {'niveaux': levels}, 'system': {'battery': battery, 'charging': charging}}


@pytest.mark.parametrize('pid', NEW)
def test_new_builtins_are_present_and_consistent(pid):
    manifest = load(pid, 'manifest.json')
    assert manifest['id'] == pid and manifest['author'] == 'Winlestia'
    assert (BUILTIN / pid / 'preview.png').is_file()
    assert 'permissions' not in manifest  # aucun accès réseau


@pytest.mark.parametrize('pid', NEW)
def test_no_literal_colour_and_layouts_start_at_zero(pid):
    widget = load(pid, 'widget.json')
    assert widget['layouts'][0]['minWidth'] == 0
    assert '#' not in json.dumps(widget) and 'rgb' not in json.dumps(widget)


def test_battery_table_is_a_0_to_1_scale():
    ratio = load('winlestia.batterie', 'assets/niveaux.json')['ratio']
    assert len(ratio) == 101 and ratio[0] == 0 and ratio[100] == 1
    assert all(ratio[i] < ratio[i + 1] for i in range(100))


@pytest.mark.parametrize('level', (0, 1, 37, 50, 99, 100))
def test_battery_index_converts_percent_to_ratio(level):
    value = ex.render('{{ data.niveaux.ratio[system.battery] }}', ctx(level))
    assert value == pytest.approx(level / 100)


def test_battery_index_accepts_integral_float():
    assert ex.render('{{ data.niveaux.ratio[system.battery] }}', ctx(42.0)) == pytest.approx(0.42)


def test_battery_index_edges():
    # La source (GetSystemPowerStatus, octet 0..100, sinon null côté QML) ne produit jamais de fraction : seuls ces cas existent.
    assert ex.render('{{ data.niveaux.ratio[system.battery] }}', ctx(0)) == 0
    assert ex.render('{{ data.niveaux.ratio[system.battery] }}', ctx(100)) == 1
    assert ex.render('{{ data.niveaux.ratio[system.battery] }}', ctx(None)) is None


def test_battery_fractional_and_out_of_range_give_no_ratio_not_a_wrong_one():
    # Limite connue du langage d'expressions (aucun filtre d'arrondi) : une fraction n'indexe rien, elle ne retombe pas sur un voisin.
    assert ex.render('{{ data.niveaux.ratio[system.battery] }}', ctx(57.5)) is None
    assert ex.render('{{ data.niveaux.ratio[system.battery] }}', ctx(101)) is None
    assert ex.render('{{ data.niveaux.ratio[system.battery] }}', ctx(150)) is None
    # Un indice négatif relirait la fin du tableau (-1 -> 1.0) : c'est la visibilité (battery >= 0) qui protège la jauge.
    assert ex.condition('system.battery >= 0', ctx(-1)) is False


def test_battery_label_and_visibility():
    assert ex.render_text('{{ system.battery }} %', ctx(80)) == '80 %'
    assert ex.condition('system.battery >= 0', ctx(0)) is True
    assert ex.condition('system.battery >= 0', ctx(None)) is False
    assert ex.condition('system.battery == null', ctx(None)) is True
    assert ex.condition('system.battery == null', ctx(0)) is False


def test_battery_without_battery_gives_no_ratio():
    assert ex.render('{{ data.niveaux.ratio[system.battery] }}', ctx(None)) is None


def test_battery_charging_conditions():
    assert ex.condition('system.charging', ctx(50, True)) is True
    assert ex.condition('!system.charging && settings.showSource', ctx(50, False, showSource=True)) is True
    assert ex.condition('!system.charging && settings.showSource', ctx(50, True, showSource=True)) is False


def test_battery_layout_has_both_states_and_both_gauges():
    widget = load('winlestia.batterie', 'widget.json')
    for layout in widget['layouts']:
        conditions = {n.get('visibleWhen') for n in nodes(layout['root'])}
        assert {'system.battery >= 0', 'system.battery == null'} <= conditions
        kinds = {n.get('variant') for n in nodes(layout['root']) if n['type'] == 'progress'}
        assert kinds == {'ring', 'bar'}


def test_clock_date_patterns():
    moment = datetime(2026, 11, 14, 15, 5, 9).astimezone()
    assert ex.format_date(moment, 'h:mm AP') == '3:05 PM'
    assert ex.format_date(moment, 'h:mm:ss AP') == '3:05:09 PM'
    assert ex.format_date(moment, 'HH:mm') == '15:05'
    assert ex.render_text('{{ now | date:dddd d MMMM yyyy }}', {'now': moment}) == 'samedi 14 novembre 2026'
    assert ex.render_text('{{ now | date:ddd d MMM }}', {'now': moment}).startswith('sam.')


def test_clock_variants_are_mutually_exclusive():
    widget = load('winlestia.horloge', 'widget.json')
    root = widget['layouts'][0]['root']
    wrappers = [n for n in nodes(root) if 'visibleWhen' in n
                and any(c['type'] == 'stack' and c['children'][0]['type'] == 'clock' for c in n.get('children', []))]
    assert len(wrappers) == 3
    for hour12 in (False, True):
        for seconds in (False, True):
            visible = [w for w in wrappers if ex.condition(w['visibleWhen'], {'settings': {'hour12': hour12, 'showSeconds': seconds}})]
            assert len(visible) == 1, (hour12, seconds)
            clock = visible[0]['children'][0]['children'][0]
            assert ('format' in clock) == hour12


def test_calendar_first_day_variants_are_mutually_exclusive():
    widget = load('winlestia.calendrier', 'widget.json')
    root = widget['layouts'][-1]['root']
    wrappers = [n for n in nodes(root) if any(c['type'] == 'calendar' for c in n.get('children', []))]
    assert len(widget['layouts']) == 2
    for choice in ('monday', 'sunday'):
        visible = [w for w in wrappers if ex.condition(w['visibleWhen'], {'settings': {'firstDay': choice}})]
        assert len(visible) == 1 and visible[0]['children'][0]['firstDay'] == choice
    assert widget['settings'][0]['default'] == 'monday'
