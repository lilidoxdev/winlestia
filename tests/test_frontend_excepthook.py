"""Les hooks d'exceptions du frontend écrivent dans frontend.log puis rendent la main (sans QApplication)."""
import sys
import threading
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src' / 'frontend'))
app_main = pytest.importorskip('app_main')


@pytest.fixture
def hooks(tmp_path):
    saved = sys.excepthook, threading.excepthook
    seen = []
    sys.excepthook = lambda *a: seen.append('sys')
    threading.excepthook = lambda a: seen.append('thread')
    log = tmp_path / 'frontend.log'
    app_main.install_exception_hooks(log)
    yield log, seen
    sys.excepthook, threading.excepthook = saved


def test_sys_hook_logs_and_defers(hooks):
    log, seen = hooks
    try:
        raise ValueError('boom')
    except ValueError:
        sys.excepthook(*sys.exc_info())
    assert 'ValueError: boom' in log.read_text(encoding='utf-8')
    assert seen == ['sys']


def test_keyboard_interrupt_not_logged(hooks):
    log, seen = hooks
    sys.excepthook(KeyboardInterrupt, KeyboardInterrupt(), None)
    assert not log.exists()
    assert seen == ['sys']


def test_thread_hook_logs_and_defers(hooks):
    log, seen = hooks
    thread = threading.Thread(target=lambda: 1 / 0, name='probe')
    thread.start()
    thread.join()
    text = log.read_text(encoding='utf-8')
    assert 'ZeroDivisionError' in text and 'probe' in text
    assert seen == ['thread']
