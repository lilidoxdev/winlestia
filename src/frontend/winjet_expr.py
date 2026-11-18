"""Expressions des Winjets (format v1, §5) : gabarits `{{ chemin | filtre:arg }}` et conditions.

Aucune évaluation de code : un analyseur dédié lit une grammaire fermée. Toute erreur de syntaxe ou de chemin donne une
valeur vide (gabarit) ou faux (condition), jamais une exception. Les paquets ne sont que des données.
"""
from __future__ import annotations

import math
import re
from datetime import datetime, timezone

MAX_EXPR = 400  # longueur maximale d'une expression : au-delà, ignorée
MAX_PATH = 12  # profondeur maximale d'un chemin

# Au-delà, le gabarit n'est plus interprété : texte brut tronqué (évite les balayages quadratiques sur un paquet hostile).
MAX_TEMPLATE = 2000
_TEMPLATE = re.compile(r'\{\{((?:(?!\{\{|\}\}).)*)\}\}', re.S)  # jamais d'accolades doubles à l'intérieur : deux gabarits restent distincts
# Un chemin : segments `.nom` ou indices `[expr]` où expr est un nombre, un texte entre guillemets ou un autre chemin
# (`data.market.symbols[state.pair].candles[state.tf]`).
_PATH = re.compile(r'^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z0-9_-]+|\[[^\[\]]{1,80}\])*$')
_SEGMENT = re.compile(r'\.([A-Za-z0-9_-]+)|\[([^\[\]]{1,80})\]|^([A-Za-z_][A-Za-z0-9_]*)')

MOIS = ['janvier', 'février', 'mars', 'avril', 'mai', 'juin', 'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre']
JOURS = ['lundi', 'mardi', 'mercredi', 'jeudi', 'vendredi', 'samedi', 'dimanche']
DEVISES = {'EUR': '€', 'USD': '$', 'GBP': '£', 'JPY': '¥', 'CHF': 'CHF', 'CAD': 'CA$', 'AUD': 'A$'}
NBSP = ' '


# --- Chemins -------------------------------------------------------------------------------------------------------


def lookup(path, ctx):
    """Valeur au bout de `path` dans `ctx`, ou None. Seuls dict et list sont parcourus."""
    if not isinstance(path, str) or not _PATH.match(path):
        return None
    parts = []
    for match in _SEGMENT.finditer(path):
        key, index, head = match.groups()
        if index is not None:
            index = index.strip()
            if index[:1] in ('"', "'"):
                key = _unquote(index)
            else:
                number = _as_number(index) if re.match(r'^-?\d', index) else None
                key = lookup(index, ctx) if number is None else int(number)
                if isinstance(key, float) and key == int(key):
                    key = int(key)
                if not isinstance(key, (str, int)) or isinstance(key, bool):
                    return None
                key = str(key)
        parts.append(key if key is not None else head)
    if len(parts) > MAX_PATH:
        return None
    node = ctx
    for part in parts:
        if isinstance(node, dict):
            if part not in node:
                return None
            node = node[part]
        elif isinstance(node, (list, tuple)):
            if part == 'length':
                node = len(node)
                continue
            try:
                node = node[int(part)]
            except (ValueError, IndexError):
                return None
        elif isinstance(node, str) and part == 'length':
            node = len(node)
        else:
            return None
    return node


def _as_number(value):
    if isinstance(value, bool):
        return float(value)
    if isinstance(value, (int, float)):
        return float(value) if math.isfinite(value) else None
    if isinstance(value, str):
        try:
            number = float(value.strip().replace(',', '.'))
        except ValueError:
            return None
        return number if math.isfinite(number) else None
    return None


def _as_datetime(value):
    if isinstance(value, datetime):
        return value
    if hasattr(value, 'toPython'):  # QDateTime
        try:
            return value.toPython()
        except Exception:
            return None
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        seconds = value / 1000 if abs(value) > 1e11 else value  # millisecondes ou secondes
        try:
            return datetime.fromtimestamp(seconds, timezone.utc).astimezone()
        except (OverflowError, OSError, ValueError):
            return None
    if isinstance(value, str):
        try:
            return datetime.fromisoformat(value.strip().replace('Z', '+00:00'))
        except ValueError:
            return None
    return None


# --- Filtres -------------------------------------------------------------------------------------------------------


def _group(number, decimals):
    text = f'{abs(number):,.{decimals}f}'.replace(',', NBSP).replace('.', ',')
    return ('-' if number < 0 and float(text.replace(NBSP, '').replace(',', '.')) != 0 else '') + text


def _f_number(value, arg):
    number = _as_number(value)
    if number is None:
        return None
    decimals = _as_number(arg)
    return _group(number, max(0, min(8, int(decimals))) if decimals is not None else 2 if number != int(number) else 0)


def _f_currency(value, arg):
    number = _as_number(value)
    if number is None:
        return None
    code = (arg or 'EUR').strip().upper()
    symbole = DEVISES.get(code, code)
    return f'{_group(number, 2)}{NBSP}{symbole}'


def _f_percent(value, arg):
    # Une proportion (0.123 -> « 12,3 % ») : c'est aussi la sortie du filtre `delta`.
    number = _as_number(value)
    if number is None:
        return None
    decimals = _as_number(arg)
    return f'{_group(number * 100, max(0, min(6, int(decimals))) if decimals is not None else 0)}{NBSP}%'


_DATE_TOKEN = re.compile(r'yyyy|yy|MMMM|MMM|MM|M|dddd|ddd|dd|d|HH|H|hh|h|mm|m|ss|s|AP|ap|\'[^\']*\'')


def format_date(moment, pattern):
    """Motifs de type Qt : yyyy yy MMMM MMM MM M dddd ddd dd d HH H hh h mm m ss s ; 'texte' littéral."""

    def part(match):
        token = match.group(0)
        hour12 = moment.hour % 12 or 12
        table = {
            'yyyy': f'{moment.year:04d}',
            'yy': f'{moment.year % 100:02d}',
            'MMMM': MOIS[moment.month - 1],
            'MMM': MOIS[moment.month - 1][:4] + '.' if len(MOIS[moment.month - 1]) > 4 else MOIS[moment.month - 1],
            'MM': f'{moment.month:02d}',
            'M': str(moment.month),
            'dddd': JOURS[moment.weekday()],
            'ddd': JOURS[moment.weekday()][:3] + '.',
            'dd': f'{moment.day:02d}',
            'd': str(moment.day),
            'HH': f'{moment.hour:02d}',
            'H': str(moment.hour),
            'hh': f'{hour12:02d}',
            'h': str(hour12),
            'mm': f'{moment.minute:02d}',
            'm': str(moment.minute),
            'ss': f'{moment.second:02d}',
            's': str(moment.second),
            'AP': 'AM' if moment.hour < 12 else 'PM',
            'ap': 'am' if moment.hour < 12 else 'pm',
        }
        return token[1:-1] if token.startswith("'") else table[token]

    return _DATE_TOKEN.sub(part, pattern)


def _f_date(value, arg):
    moment = _as_datetime(value)
    if moment is None:
        return None
    return format_date(moment, arg or 'dd/MM/yyyy')


def _f_upper(value, arg):
    return None if value is None else str(value).upper()


def _f_lower(value, arg):
    return None if value is None else str(value).lower()


def _f_default(value, arg):
    return arg if value is None or value == '' else value


def _point_value(point):
    # Une série contient des nombres ou des paires [t, v].
    if isinstance(point, (list, tuple)) and len(point) >= 2:
        point = point[-1]
    return _as_number(point)


def _f_last(value, arg):
    if isinstance(value, (list, tuple)):
        return value[-1] if value else None
    return value


def _f_first(value, arg):
    if isinstance(value, (list, tuple)):
        return value[0] if value else None
    return value


def _f_delta(value, arg):
    if not isinstance(value, (list, tuple)) or len(value) < 2:
        return None
    first, last = _point_value(value[0]), _point_value(value[-1])
    if first is None or last is None or first == 0:
        return None
    return (last - first) / abs(first)


def fmt(value, kind):
    """Format d'affichage commun aux gabarits (`fmt(x, 'currency')`) et aux graphiques (`valueFormat`).

    currency : regroupement fr-FR et « $ », 0 décimale au-dessus de 1000, 2 en dessous ; percent : proportion signée,
    1 décimale ; number : nombre groupé. Valeur illisible : None.
    """
    number = _as_number(value)
    if number is None:
        return None
    kind = (kind or 'number').strip().lower() if isinstance(kind, str) else 'number'
    if kind == 'currency':
        return f'{_group(number, 0 if abs(number) >= 1000 else 2)}{NBSP}$'
    if kind == 'percent':
        text = _group(number * 100, 1)
        return f'{"+" if number > 0 and text[0] != "-" and float(text.replace(NBSP, "").replace(",", ".")) != 0 else ""}{text}{NBSP}%'
    return _f_number(number, None)


FILTERS = {
    'number': _f_number,
    'currency': _f_currency,
    'percent': _f_percent,
    'date': _f_date,
    'upper': _f_upper,
    'lower': _f_lower,
    'default': _f_default,
    'last': _f_last,
    'first': _f_first,
    'delta': _f_delta,
}


def _split_pipes(source):
    """Découpe sur `|` hors guillemets."""
    out, current, quote = [], '', ''
    for char in source:
        if quote:
            current += char
            if char == quote:
                quote = ''
        elif char in '\'"':
            quote = char
            current += char
        elif char == '|':
            out.append(current)
            current = ''
        else:
            current += char
    out.append(current)
    return [part.strip() for part in out]


def _unquote(text):
    text = text.strip()
    if len(text) >= 2 and text[0] == text[-1] and text[0] in '\'"':
        return text[1:-1]
    return text


ROOTS = ('settings', 'state', 'data', 'instance', 'now', 'media', 'system', 'weather', 'files')


def _filter_arg(arg, ctx):
    """Argument de filtre : littéral (`EUR`, `"a b"`) ou chemin du contexte (`settings.currency`) s'il se résout."""
    arg = arg.strip() if arg else ''
    if not arg:
        return None
    if arg[0] in '\'"':
        return _unquote(arg)
    if _PATH.match(arg) and arg.split('.')[0] in ROOTS and '.' in arg:
        value = lookup(arg, ctx)
        if value is not None and not isinstance(value, (dict, list, tuple)):
            return value if isinstance(value, str) else to_text(value)
    return arg


def _split_top(source, separator):
    """Découpe sur `separator` (un ou deux caractères) hors guillemets, crochets et parenthèses."""
    out, current, quote, depth, i = [], '', '', 0, 0
    while i < len(source):
        char = source[i]
        if quote:
            current += char
            if char == quote:
                quote = ''
        elif char in '\'"':
            quote = char
            current += char
        elif char in '[(':
            depth += 1
            current += char
        elif char in '])':
            depth -= 1
            current += char
        elif depth == 0 and source.startswith(separator, i):
            out.append(current)
            current = ''
            i += len(separator)
            continue
        else:
            current += char
        i += 1
    out.append(current)
    return [part.strip() for part in out]


_FMT_CALL = re.compile(r'^fmt\((.*)\)$', re.S)


def _eval_head(head, ctx, depth=0):
    """Tête d'expression : `a ?? b ?? 0`, `fmt(x, 'currency')`, littéral ou chemin."""
    if depth > 6:
        return None
    alternatives = _split_top(head, '??')
    if len(alternatives) > 1:
        for alternative in alternatives:
            value = _eval_head(alternative, ctx, depth + 1)
            if value is not None and value != '':
                return value
        return None
    call = _FMT_CALL.match(head)
    if call:
        args = _split_top(call.group(1), ',')
        if len(args) != 2:
            return None
        return fmt(_eval_head(args[0], ctx, depth + 1), _unquote(args[1]))
    if head[:1] in ('"', "'"):
        return _unquote(head)
    number = _as_number(head) if re.match(r'^-?\d', head) else None
    if number is not None:
        return number
    if head == 'null':
        return None
    return lookup(head, ctx) if _is_path_head(head) else None


def _is_path_head(text):
    return text in ('now',) or bool(_PATH.match(text))


def evaluate_pipeline(source, ctx):
    """Évalue `chemin | filtre:arg | ...` ; renvoie la valeur brute (liste, nombre, texte...) ou None."""
    if len(source) > MAX_EXPR:
        return None
    parts = _split_pipes(source)
    value = _eval_head(parts[0], ctx)
    for stage in parts[1:]:
        name, _, arg = stage.partition(':')
        name = name.strip()
        function = FILTERS.get(name)
        if function is None:
            return None
        value = function(value, _filter_arg(arg, ctx))
    return value


def to_text(value):
    if value is None:
        return ''
    if isinstance(value, bool):
        return 'oui' if value else 'non'
    if isinstance(value, float):
        return str(int(value)) if value == int(value) else f'{value:g}'.replace('.', ',')
    if isinstance(value, datetime):
        return format_date(value, 'dd/MM/yyyy HH:mm')
    if isinstance(value, (dict, list, tuple)):
        return ''
    return str(value)


def render(text, ctx):
    """Gabarit complet. Une chaîne qui n'est qu'une seule expression renvoie la valeur brute (tableaux compris)."""
    if not isinstance(text, str) or '{{' not in text:
        return text
    if len(text) > MAX_TEMPLATE:
        return text[:MAX_TEMPLATE]
    whole = _TEMPLATE.fullmatch(text.strip())
    if whole:
        return evaluate_pipeline(whole.group(1).strip(), ctx)
    return _TEMPLATE.sub(lambda m: to_text(evaluate_pipeline(m.group(1).strip(), ctx)), text)


def render_text(text, ctx):
    """Comme render mais toujours une chaîne affichable."""
    value = render(text, ctx)
    return value if isinstance(value, str) else to_text(value)


# --- Conditions ----------------------------------------------------------------------------------------------------

_TOKEN = re.compile(
    r"""\s*(?:(?P<num>-?\d+(?:\.\d+)?)|(?P<str>'[^']*'|"[^"]*")|(?P<op>==|!=|<=|>=|&&|\|\||\?\?|<|>|!|\(|\))"""
    r"""|(?P<id>[A-Za-z_][A-Za-z0-9_.\-]*(?:\[[^\[\]]*\][A-Za-z0-9_.\-]*)*))"""
)


class _Parse(Exception):
    pass


def _tokens(source):
    out, pos = [], 0
    source = source.rstrip()
    while pos < len(source):
        match = _TOKEN.match(source, pos)
        if not match or match.end() == pos:
            raise _Parse(source[pos:])
        pos = match.end()
        kind = match.lastgroup
        text = match.group(kind)
        if kind == 'num':
            out.append(('v', float(text)))
        elif kind == 'str':
            out.append(('v', text[1:-1]))
        elif kind == 'op':
            out.append(('op', text))
        else:
            out.append(('id', text))
    return out


class _Condition:
    def __init__(self, tokens, ctx):
        self.tokens, self.ctx, self.i = tokens, ctx, 0

    def peek(self):
        return self.tokens[self.i] if self.i < len(self.tokens) else (None, None)

    def take(self):
        token = self.peek()
        self.i += 1
        return token

    def accept(self, op):
        if self.peek() == ('op', op):
            self.i += 1
            return True
        return False

    def parse(self):
        value = self.coalesce()
        if self.i != len(self.tokens):
            raise _Parse('reste')
        return value

    def coalesce(self):
        value = self.or_()
        while self.accept('??'):
            right = self.or_()
            if value is None or value == '':
                value = right
        return value

    def or_(self):
        value = self.and_()
        while self.accept('||'):
            right = self.and_()
            value = bool(value) or bool(right)
        return value

    def and_(self):
        value = self.not_()
        while self.accept('&&'):
            right = self.not_()
            value = bool(value) and bool(right)
        return value

    def not_(self):
        if self.accept('!'):
            return not self.not_()
        return self.compare()

    def compare(self):
        left = self.atom()
        kind, op = self.peek()
        if kind == 'op' and op in ('==', '!=', '<', '<=', '>', '>='):
            self.i += 1
            return _compare(left, op, self.atom())
        return left

    def atom(self):
        kind, value = self.take()
        if kind == 'v':
            return value
        if kind == 'op' and value == '(':
            inner = self.coalesce()
            if not self.accept(')'):
                raise _Parse('parenthèse')
            return inner
        if kind == 'id':
            if value == 'true':
                return True
            if value == 'false':
                return False
            if value == 'null':
                return None
            return lookup(value, self.ctx)
        raise _Parse('atome')


def _compare(left, op, right):
    if op in ('==', '!='):
        a, b = _as_number(left) if _numeric(left) else left, _as_number(right) if _numeric(right) else right
        equal = a == b
        return equal if op == '==' else not equal
    a, b = _as_number(left), _as_number(right)
    if a is None or b is None:
        if isinstance(left, str) and isinstance(right, str):
            a, b = left, right
        else:
            return False
    return {'<': a < b, '<=': a <= b, '>': a > b, '>=': a >= b}[op]


def _numeric(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def condition(source, ctx):
    """Vrai si l'expression est vraie. Une expression absente ou vide est vraie ; invalide, elle est fausse."""
    if source is None or source is True or source == '':
        return True
    if source is False:
        return False
    if not isinstance(source, str) or len(source) > MAX_EXPR:
        return False
    text = source.strip()
    whole = _TEMPLATE.fullmatch(text)
    if whole:
        text = whole.group(1).strip()
    try:
        return bool(_Condition(_tokens(text), ctx).parse())
    except _Parse:
        return False
    except RecursionError:
        return False
