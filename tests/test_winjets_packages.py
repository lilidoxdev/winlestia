"""Paquets Winjets (format winjet/1) : schemas JSON, references, expressions, limites et mise en ZIP.

Valide les paquets inclus, le gabarit et l'exemple communautaire (dossier et ZIP).
Reference normative : docs/WINJETS-DESIGN.md section 5 ; spec publique : docs/winjets/FORMAT-v1.md.
"""
import io
import json
import re
import struct
import sys
import zipfile
from pathlib import Path
from urllib.parse import urlparse

import pytest

jsonschema = pytest.importorskip('jsonschema')

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'tools'))
import build_winjet_zip  # noqa: E402

SCHEMAS = ROOT / 'docs' / 'winjets' / 'schema'
BUILTIN = ROOT / 'src' / 'winjets' / 'builtin'
TEMPLATE = ROOT / 'docs' / 'winjets' / 'template'
EXAMPLE = ROOT / 'docs' / 'winjets' / 'examples' / 'com.exemple.citation'
EXAMPLE_ZIP = ROOT / 'docs' / 'winjets' / 'examples' / 'com.exemple.citation-1.0.0.zip'

ALLOWED_EXT = {'.json', '.png', '.jpg', '.jpeg', '.webp', '.gif', '.txt', '.md'}
FILTERS = {'number', 'currency', 'percent', 'date', 'upper', 'lower', 'default', 'last', 'first', 'delta'}
FILTERS_WITH_ARG = {'number', 'currency', 'percent', 'date', 'default'}
FILTERS_NO_ARG = FILTERS - FILTERS_WITH_ARG
WINDOWS_RESERVED = re.compile(r'^(con|prn|aux|nul|com[1-9]|lpt[1-9])(\..*)?$', re.IGNORECASE)
EXPR = re.compile(r'\{\{(.*?)\}\}', re.DOTALL)
PATH_ROOT = re.compile(r'^(settings|state)\.([A-Za-z][A-Za-z0-9]*)(\.[A-Za-z0-9_]+)*$|^data\.([A-Za-z][A-Za-z0-9]*)(\.[A-Za-z0-9_]+|\[[^\[\]]+\])+$'
                       r'|^now$|^instance\.(width|height)$|^media\.[a-zA-Z]+$|^system\.[a-zA-Z]+$|^time\.[a-zA-Z]+$')
COND_TOKEN = re.compile(r"\s*(?:(?P<str>'[^']*')|(?P<num>-?\d+(?:\.\d+)?)|(?P<lit>true|false|null)\b"
                        r"|(?P<path>(?:settings|state|data|instance|media|system|time)(?:\.[A-Za-z0-9_]+|\[[^\[\]]+\])+|now)"
                        r"|(?P<op>==|!=|<=|>=|&&|\|\||<|>|!|\(|\)))")
COLOR_LITERAL = re.compile(r'#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(')


def load(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def validators():
    cls = jsonschema.Draft202012Validator
    manifest, widget = load(SCHEMAS / 'manifest.schema.json'), load(SCHEMAS / 'widget.schema.json')
    cls.check_schema(manifest)
    cls.check_schema(widget)
    return cls(manifest), cls(widget)


def errors_of(validator, doc):
    return [f"{'/'.join(map(str, e.absolute_path))}: {e.message[:160]}" for e in validator.iter_errors(doc)]


def folder_packages():
    pkgs = sorted(p for p in BUILTIN.iterdir() if p.is_dir()) if BUILTIN.is_dir() else []
    return pkgs + [TEMPLATE, EXAMPLE]


def png_size(data):
    assert data[:8] == b'\x89PNG\r\n\x1a\n', 'preview.png n\'est pas un PNG'
    return struct.unpack('>II', data[16:24])


def walk(node, visit, key=None):
    """Parcourt widget.json et appelle visit(cle, valeur_chaine)."""
    if isinstance(node, dict):
        for k, v in node.items():
            walk(v, visit, k)
    elif isinstance(node, list):
        for v in node:
            walk(v, visit, key)
    elif isinstance(node, str):
        visit(key, node)


def walk_nodes(node):
    """Itere tous les noeuds de composition (dictionnaires avec une cle type)."""
    if isinstance(node, dict):
        if 'type' in node and ('children' in node or node['type'] in
                               {'text', 'image', 'clock', 'calendar', 'list', 'chart', 'stat', 'progress', 'dots',
                                'buttons', 'spacer', 'divider'}):
            yield node
        for v in node.values():
            yield from walk_nodes(v)
    elif isinstance(node, list):
        for v in node:
            yield from walk_nodes(v)


class Refs:
    def __init__(self, widget):
        self.settings = {s['id'] for s in widget.get('settings', [])}
        self.state = set(widget.get('state', {}))
        self.data = set(widget.get('data', {}))

    def check_path(self, path, where):
        # Indexation `a.b[state.x].c[state.y]` : chaque indice est lui-meme un chemin, litteral ou nombre.
        for inner in re.findall(r'\[([^\[\]]+)\]', path):
            inner = inner.strip()
            if not (inner[:1] in ('"', "'") or re.fullmatch(r'-?d+', inner)):
                self.check_path(inner, where)
        flat = re.sub(r'\[[^\[\]]+\]', '[]', path)
        assert PATH_ROOT.match(flat.replace('[]', '.x')), f'{where} : chemin non autorise "{path}"'
        head, *rest = re.split(r'[.\[]', flat)
        if head == 'settings':
            assert rest[0] in self.settings, f'{where} : reglage inconnu "{path}"'
        elif head == 'state':
            assert rest[0] in self.state, f'{where} : etat inconnu "{path}"'
        elif head == 'data':
            assert rest[0] in self.data, f'{where} : source inconnue "{path}"'

    def check_head(self, head, where):
        head = head.strip()
        call = re.fullmatch(r"fmt\((.*),\s*'(currency|percent|number)'\s*\)", head, re.S)
        if call:
            return self.check_head(call.group(1), where)
        alternatives = [a.strip() for a in head.split('??')]
        for alt in alternatives:
            if re.fullmatch(r"'[^']*'|-?\d+(\.\d+)?", alt):
                continue
            self.check_path(alt, where)

    def check_expression(self, body, where):
        parts = [p.strip() for p in body.split('|')]
        self.check_head(parts[0], where)
        for flt in parts[1:]:
            name, sep, arg = flt.partition(':')
            assert name in FILTERS, f'{where} : filtre non autorise "{name}"'
            if name in FILTERS_NO_ARG:
                assert not sep, f"{where} : le filtre {name} ne prend pas d'argument"
            elif name != 'default':
                assert sep and arg.strip(), f'{where} : le filtre {name} attend un argument'

    def check_condition(self, cond, where):
        pos = 0
        while pos < len(cond):
            m = COND_TOKEN.match(cond, pos)
            assert m, f'{where} : condition invalide pres de "{cond[pos:]}"'
            if m.group('path'):
                self.check_path(m.group('path'), where)
            pos = m.end()
            if not cond[pos:].strip():
                break


def check_widget_semantics(pkg_dir, manifest, widget):
    refs = Refs(widget)
    size = widget['size']
    assert all(a <= b <= c for a, b, c in zip(size['min'], size['default'], size['max'])), 'size : min <= default <= max'
    layouts = widget['layouts']
    assert layouts[0]['minWidth'] == 0 and layouts[0].get('minHeight', 0) == 0, 'la premiere mise en page doit couvrir tout'
    keys = [(la['minWidth'], la.get('minHeight', 0)) for la in layouts]
    assert keys == sorted(keys), 'layouts : ordre croissant attendu'
    assert len({s['id'] for s in widget.get('settings', [])}) == len(widget.get('settings', [])), 'reglages en double'
    for s in widget.get('settings', []):
        if s['type'] == 'choice':
            assert s['default'] in {o['value'] for o in s['options']}, f"reglage {s['id']} : defaut hors options"
        if s['type'] == 'number':
            assert s.get('min', s['default']) <= s['default'] <= s.get('max', s['default']), f"reglage {s['id']} : defaut hors bornes"
    network = set(manifest.get('permissions', {}).get('network', []))
    for sid, src in widget.get('data', {}).items():
        if src['type'] == 'file':
            assert (pkg_dir / src['path']).is_file(), f"source {sid} : fichier absent {src['path']}"
        elif src['type'] == 'market':
            assert {'api.binance.com', 'stream.binance.com'} <= network, f'source {sid} : hotes Binance non declares'
            if 'enabledWhen' in src:
                refs.check_condition(src['enabledWhen'], f'source {sid}.enabledWhen')
        else:
            assert urlparse(src['url']).hostname in network, f"source {sid} : hote non declare dans permissions.network"
            if 'enabledWhen' in src:
                refs.check_condition(src['enabledWhen'], f'source {sid}.enabledWhen')

    def visit(key, value):
        where = f'{key}={value[:40]!r}'
        if key in ('visibleWhen',):
            refs.check_condition(value, where)
        for body in EXPR.findall(value):
            refs.check_expression(body, where)
        assert not COLOR_LITERAL.search(value), f'{where} : couleur litterale interdite'
        if key == 'src':
            assert (pkg_dir / value).is_file(), f'image absente : {value}'

    walk(widget['layouts'], visit)
    homepage = urlparse(manifest.get('homepage', '')).hostname
    for node in walk_nodes(widget['layouts']):
        for act in [node.get('action')] if node.get('action') else []:
            if act['type'] == 'setState':
                assert act['key'] in refs.state, f"setState : cle non declaree {act['key']}"
            if act['type'] == 'openUrl':
                assert urlparse(act['url']).hostname in network | {homepage}, 'openUrl : hote non declare'
        if node['type'] == 'buttons':
            key = node['bind'].split('.', 1)[1]
            assert key in refs.state, f'buttons : cle d\'etat non declaree {key}'
        if node['type'] == 'list':
            m = re.fullmatch(r'\{\{\s*data\.(\w+)((?:\.\w+)+)\s*\}\}', node['items'])
            assert m, 'list.items doit etre une expression data.<source>.<chemin>'
            src = widget['data'][m.group(1)]
            if src['type'] == 'file':
                value = load(pkg_dir / src['path'])
                for part in m.group(2).strip('.').split('.'):
                    value = value[part]
                assert isinstance(value, list) and value, 'list.items : tableau non vide attendu'
                for field in (node['title'], node.get('subtitle'), node.get('trailing')):
                    assert field is None or field in value[0], f'list : champ absent des elements : {field}'
        if node['type'] == 'dots' and 'matrix' in node:
            assert EXPR.fullmatch(node['matrix'].strip()), 'dots.matrix doit etre une expression'


def check_package_folder(pkg_dir, strict_layout=True):
    manifest_v, widget_v = validators()
    manifest, widget = load(pkg_dir / 'manifest.json'), load(pkg_dir / 'widget.json')
    assert errors_of(manifest_v, manifest) == []
    assert errors_of(widget_v, widget) == []
    check_widget_semantics(pkg_dir, manifest, widget)
    return manifest, widget


@pytest.mark.parametrize('pkg', folder_packages(), ids=lambda p: p.name)
def test_package_validates_against_schemas(pkg):
    manifest, _ = check_package_folder(pkg)
    if pkg.parent == BUILTIN or pkg == EXAMPLE:
        assert manifest['id'] == pkg.name


@pytest.mark.parametrize('pkg', folder_packages(), ids=lambda p: p.name)
def test_package_files_and_limits(pkg):
    files = [p for p in pkg.rglob('*') if p.is_file()]
    assert len(files) <= 200
    total = 0
    for f in files:
        rel = f.relative_to(pkg)
        if rel.name in ('.gitkeep', 'README.md') and pkg == TEMPLATE:
            continue
        assert len(rel.parts) <= 4, f'profondeur > 4 : {rel}'
        assert f.suffix.lower() in ALLOWED_EXT, f'extension interdite : {rel}'
        assert not any(WINDOWS_RESERVED.match(part) for part in rel.parts), f'nom reserve : {rel}'
        total += f.stat().st_size
        if f.suffix == '.json':
            assert f.stat().st_size <= 256 * 1024, f'JSON > 256 Kio : {rel}'
    assert total <= 20 * 1024 * 1024
    names = {p.name for p in pkg.iterdir()}
    assert {'manifest.json', 'widget.json'} <= names
    allowed = {'manifest.json', 'widget.json', 'preview.png', 'assets'} | ({'README.md'} if pkg == TEMPLATE else set())
    assert names <= allowed
    preview = pkg / 'preview.png'
    if preview.exists():
        assert preview.stat().st_size <= 1024 * 1024
        w, h = png_size(preview.read_bytes())
        assert w <= 1600 and h <= 1000


def test_builtin_set_is_complete():
    ids = {p.name for p in BUILTIN.iterdir() if p.is_dir()}
    assert {'winlestia.lecteur', 'winlestia.systeme', 'winlestia.marches'} <= ids
    for name in ids:
        assert (BUILTIN / name / 'preview.png').is_file()


def test_marches_package_contract():
    pkg = BUILTIN / 'winlestia.marches'
    manifest, widget = load(pkg / 'manifest.json'), load(pkg / 'widget.json')
    assert manifest['permissions']['network'] == ['api.binance.com', 'stream.binance.com']
    assert widget['data']['market'] == {'type': 'market', 'symbols': ['BTC', 'ETH', 'SOL']}
    assert {'pair', 'tf', 'hover'} <= set(widget['state'])


def test_zip_matches_folder_and_layout():
    assert EXAMPLE_ZIP.stat().st_size <= 5 * 1024 * 1024
    with zipfile.ZipFile(EXAMPLE_ZIP) as z:
        infos = [i for i in z.infolist() if not i.is_dir()]
        names = [i.filename for i in infos]
        assert len(names) <= 200
        assert 'manifest.json' in names and 'widget.json' in names, 'fichiers attendus a la racine du ZIP'
        for n in names:
            assert not n.startswith('/') and '..' not in n.split('/') and '\\' not in n and ':' not in n
            assert not n.startswith('com.exemple.citation/'), 'le ZIP contient le dossier au lieu de son contenu'
            assert Path(n).suffix.lower() in ALLOWED_EXT and len(n.split('/')) <= 4
        assert sum(i.file_size for i in infos) <= 20 * 1024 * 1024
        assert all(i.file_size <= 256 * 1024 for i in infos if i.filename.endswith('.json'))
        manifest_v, widget_v = validators()
        assert errors_of(manifest_v, json.loads(z.read('manifest.json'))) == []
        assert errors_of(widget_v, json.loads(z.read('widget.json'))) == []
        for n in names:
            assert z.read(n) == (EXAMPLE / n).read_bytes(), f'{n} differe du dossier : relancer tools/build_winjet_zip.py'
    expected = {p.relative_to(EXAMPLE).as_posix() for p in EXAMPLE.rglob('*') if p.is_file()}
    assert expected == set(names)


def test_zip_build_is_reproducible(tmp_path):
    first = build_winjet_zip.build(EXAMPLE, tmp_path / 'a.zip').read_bytes()
    second = build_winjet_zip.build(EXAMPLE, tmp_path / 'b.zip').read_bytes()
    assert first == second == EXAMPLE_ZIP.read_bytes()
    assert zipfile.ZipFile(io.BytesIO(first)).testzip() is None


# --- les schemas refusent bien ce que le format interdit ---------------------------------------------------------

def _widget(**root):
    base = {"size": {"default": [168, 168], "min": [168, 168], "max": [352, 352]},
            "layouts": [{"minWidth": 0, "root": {"type": "text", "text": "x"}}]}
    base['layouts'][0]['root'] = root or base['layouts'][0]['root']
    return base


@pytest.mark.parametrize('root', [
    {"type": "webview", "src": "https://exemple.com"},
    {"type": "text", "text": "x", "color": "#ff0000"},
    {"type": "chart", "series": "{{ data.a.b }}", "color": "#ff0000"},
    {"type": "chart", "series": "{{ data.a.b }}", "color": "red"},
    {"type": "text", "text": "x", "style": "huge"},
    {"type": "image", "src": "../secret.png"},
    {"type": "image", "src": "assets/logo.svg"},
    {"type": "column"},
    {"type": "column", "children": [], "script": "alert(1)"},
    {"type": "text", "text": "x", "action": {"type": "run", "cmd": "calc"}},
    {"type": "text", "text": "x", "action": {"type": "openUrl", "url": "http://exemple.com"}},
])
def test_widget_schema_rejects(root):
    _, widget_v = validators()
    assert errors_of(widget_v, _widget(**root)), root


def test_manifest_schema_rejects():
    manifest_v, _ = validators()
    good = load(TEMPLATE / 'manifest.json')
    assert errors_of(manifest_v, good) == []
    for patch in ({'format': 'winjet/2'}, {'id': 'Horloge'}, {'id': 'a.b' * 40}, {'version': '1.0'}, {'extra': 1},
                  {'permissions': {'network': ['*.exemple.com']}}, {'permissions': {'network': ['https://exemple.com']}}):
        assert errors_of(manifest_v, {**good, **patch}), patch
