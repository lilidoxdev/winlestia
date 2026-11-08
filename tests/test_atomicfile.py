"""atomicfile.write_text : remplacement atomique (ancien contenu intact en cas d'échec, aucun .tmp résiduel)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src' / 'frontend'))
import atomicfile  # noqa: E402


def test_write_replaces_and_leaves_no_tmp(tmp_path):
    target = tmp_path / 'state.json'
    atomicfile.write_text(target, '{"é":1}')
    atomicfile.write_text(target, '{"é":2}')
    assert target.read_bytes() == '{"é":2}'.encode('utf-8')
    assert not list(tmp_path.glob('*.tmp'))


def test_failure_keeps_previous_content(tmp_path):
    target = tmp_path / 'state.json'
    atomicfile.write_text(target, 'GOOD')

    class Bad(str):
        def encode(self, *args, **kwargs):
            raise RuntimeError('boom')

    try:
        atomicfile.write_text(target, Bad('z'))
    except RuntimeError:
        pass
    assert target.read_text(encoding='utf-8') == 'GOOD'
    assert not list(tmp_path.glob('*.tmp'))
