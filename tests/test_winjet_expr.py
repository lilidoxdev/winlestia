"""Expressions des Winjets (§5) : gabarits, filtres, conditions. Aucune évaluation de code."""
import sys
from datetime import datetime, timezone
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src' / 'frontend'))

import winjet_expr as ex  # noqa: E402

NBSP = '\u00a0'
NOW = datetime(2026, 3, 5, 14, 7, 9, tzinfo=timezone.utc)
CTX = {
    'settings': {'city': 'Paris', 'showSeconds': False, 'decimals': 2, 'source': 'demo', 'empty': ''},
    'state': {'mode': 'face'},
    'data': {'btc': {'price': 1234.567, 'series': [100, 110, 120], 'pairs': [[1, 50], [2, 75]], 'items': [{'t': 'a'}]}},
    'instance': {'width': 352, 'height': 168},
    'now': NOW,
}


def test_plain_text_is_untouched():
    assert ex.render('bonjour', CTX) == 'bonjour'
    assert ex.render_text('a {{ settings.city }} b', CTX) == 'a Paris b'


def test_missing_path_is_empty_not_error():
    assert ex.render_text('[{{ settings.nope }}] [{{ data.x.y.z }}]', CTX) == '[] []'


def test_single_expression_keeps_raw_value():
    assert ex.render('{{ data.btc.series }}', CTX) == [100, 110, 120]
    assert ex.render('{{ instance.width }}', CTX) == 352


def test_index_and_length():
    assert ex.render('{{ data.btc.items.0.t }}', CTX) == 'a'
    assert ex.render('{{ data.btc.series.length }}', CTX) == 3
    assert ex.render('{{ data.btc.series.9 }}', CTX) is None


@pytest.mark.parametrize(
    'template,expected',
    [
        ('{{ data.btc.price | number:2 }}', f'1{NBSP}234,57'),
        ('{{ data.btc.price | number:0 }}', f'1{NBSP}235'),
        ('{{ data.btc.price | currency:EUR }}', f'1{NBSP}234,57{NBSP}€'),
        ('{{ data.btc.price | currency:USD }}', f'1{NBSP}234,57{NBSP}$'),
        ('{{ data.btc.series | delta | percent:1 }}', f'20,0{NBSP}%'),
        ('{{ data.btc.pairs | delta | percent:0 }}', f'50{NBSP}%'),
        ('{{ settings.city | upper }}', 'PARIS'),
        ('{{ settings.city | lower }}', 'paris'),
        ('{{ settings.empty | default:aucun }}', 'aucun'),
        ('{{ settings.missing | default:"x y" }}', 'x y'),
        ('{{ data.btc.series | last }}', '120'),
        ('{{ data.btc.series | first }}', '100'),
        ('{{ now | date:HH:mm }}', '14:07'),
        ('{{ now | date:"dddd d MMMM yyyy" }}', 'jeudi 5 mars 2026'),
        ("{{ now | date:dd/MM/yy }}", '05/03/26'),
    ],
)
def test_filters(template, expected):
    assert ex.render_text(template, CTX) == expected


def test_unknown_filter_gives_empty():
    assert ex.render_text('{{ settings.city | eval }}', CTX) == ''


def test_delta_edge_cases():
    assert ex.render('{{ data.btc.series | first | delta }}', CTX) is None
    assert ex.evaluate_pipeline('data.nope | delta', CTX) is None
    assert ex.evaluate_pipeline('s | delta', {'s': [0, 5]}) is None  # division par zéro évitée


def test_negative_number_formatting():
    assert ex.evaluate_pipeline('v | number:1', {'v': -3.5}) == '-3,5'
    assert ex.evaluate_pipeline('v | number:1', {'v': -0.04}) == '0,0'


def test_date_from_timestamp_and_iso():
    assert ex.evaluate_pipeline('t | date:yyyy', {'t': '2026-01-02T03:04:05Z'}) == '2026'
    assert ex.evaluate_pipeline('t | date:yyyy', {'t': 1782864000}) == '2026'  # 2026-07-01, sûr quel que soit le fuseau
    assert ex.evaluate_pipeline('t | date:yyyy', {'t': 'pas une date'}) is None


@pytest.mark.parametrize(
    'expr,expected',
    [
        ('', True),
        (None, True),
        ("settings.source == 'demo'", True),
        ("settings.source != 'demo'", False),
        ('settings.decimals >= 2', True),
        ('settings.decimals > 2', False),
        ('settings.decimals < 3 && settings.showSeconds', False),
        ('settings.decimals < 3 && !settings.showSeconds', True),
        ("settings.showSeconds || state.mode == 'face'", True),
        ('(settings.showSeconds || true) && instance.width >= 340', True),
        ('instance.width >= 520', False),
        ('data.btc.price > 1000', True),
        ('settings.missing', False),
        ('!settings.missing', True),
        ('true', True),
        ('false', False),
        ('3 == 3.0', True),
        ("'a' < 'b'", True),
        ('{{ instance.height == 168 }}', True),
    ],
)
def test_conditions(expr, expected):
    assert ex.condition(expr, CTX) is expected


@pytest.mark.parametrize(
    'bad',
    ['settings.decimals >', '((', 'a b', '__import__("os")', 'x = 1', 'settings.decimals + 1', '1 ==', ')', 'a.b..c', '&& true'],
)
def test_invalid_conditions_are_false_never_raise(bad):
    assert ex.condition(bad, CTX) is False


def test_no_code_execution():
    marker = []
    ctx = {'settings': {'f': lambda: marker.append(1)}}
    assert ex.condition('settings.f()', ctx) is False
    assert ex.render_text('{{ settings.f }}', ctx) != ''  # l'objet n'est pas appelé
    assert marker == []
    assert ex.condition('().__class__', CTX) is False
    assert ex.render_text('{{ settings.city.__class__ }}', CTX) == ''


def test_long_expressions_are_rejected():
    assert ex.condition('true' + ' && true' * 200, CTX) is False
    assert ex.evaluate_pipeline('a' * 500, CTX) is None


def test_filter_argument_may_be_a_path():
    ctx = {**CTX, 'settings': {**CTX['settings'], 'currency': 'EUR'}}
    assert ex.render_text('{{ data.btc.price | currency:settings.currency }}', ctx) == ex.render_text(
        '{{ data.btc.price | currency:EUR }}', ctx
    )
    # Chemin inconnu : l'argument reste le texte littéral, sans exception.
    assert ex.render_text('{{ data.btc.price | currency:settings.nope }}', ctx) != ''
