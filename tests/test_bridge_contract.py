"""Contrat du pont : tout message émis ou lu par le frontend figure dans docs/bridge-schema.json (extraction statique)."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FRONT = ROOT / 'src' / 'frontend'
SCHEMA = json.loads((ROOT / 'docs' / 'bridge-schema.json').read_text(encoding='utf-8'))


def sources(pattern):
    # Harnais de test et caches exclus : seuls les sources livrées comptent.
    return [p for p in FRONT.rglob(pattern) if 'tests' not in p.relative_to(FRONT).parts and '__pycache__' not in p.parts]


def sent_commands():
    names = set()
    for path in sources('*.py'):
        text = path.read_text(encoding='utf-8')
        names.update(re.findall(r"\.action\(\s*['\"]([\w-]+)['\"]", text))
        names.update(re.findall(r"'command':\s*'([\w-]+)'", text))
    for path in sources('*.qml') + sources('*.py'):
        text = path.read_text(encoding='utf-8')
        for call in re.findall(r"Bridge\.action\(([^{)]*)", text):
            names.update(re.findall(r'"([\w-]+)"', call))
    return names


def test_protocol_version_matches_schema():
    text = (FRONT / 'bridge_client.py').read_text(encoding='utf-8')
    version = int(re.search(r'^PROTOCOL_VERSION\s*=\s*(\d+)', text, re.M).group(1))
    assert version == SCHEMA['protocol']
    assert "'protocol': PROTOCOL_VERSION" in text


def test_every_sent_command_is_in_schema():
    sent = sent_commands()
    assert len(sent) > 50
    # 'navigate' et les modes d'écran locaux ne transitent pas ; seuls les noms envoyés au pipe comptent.
    missing = sorted(sent - set(SCHEMA['commands']))
    assert not missing, 'commandes absentes de docs/bridge-schema.json : %s' % missing


def test_every_read_reply_key_is_in_schema():
    text = (FRONT / 'bridge_state.py').read_text(encoding='utf-8')
    start = text.index('def update_state')
    body = text[start:start + 6000] + text[text.index("message.get('events'") - 80:][:200]
    keys = set(re.findall(r"message\.get\('(\w+)'", body)) | set(re.findall(r"message\['(\w+)'\]", body))
    keys |= set(re.findall(r"'(\w+)' in message", body))
    assert {'state', 'events', 'notice'} <= keys
    missing = sorted(keys - set(SCHEMA['replyKeys']))
    assert not missing, 'clés de réponse absentes du schéma : %s' % missing


def test_every_handled_event_is_in_schema():
    text = (FRONT / 'bridge_state.py').read_text(encoding='utf-8')
    start = text.index("or event.startswith('workspace:')")
    block = text[text.rindex('if event in (', 0, start):start]
    names = set(re.findall(r"'([\w:-]+)'", block))
    known = set(SCHEMA['events']) | set(SCHEMA['clientOnlyEvents'])
    prefixes = SCHEMA['eventPrefixes']
    missing = sorted(n for n in names if n not in known and not any(n.startswith(p) for p in prefixes))
    assert not missing, 'évènements absents du schéma : %s' % missing
