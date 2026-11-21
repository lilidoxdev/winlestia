"""Coalescence des synchronisations de géométrie : au plus une toutes les 33 ms, avec passe finale."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src' / 'frontend'))
from sync_throttle import delay_ms


def test_immediate_after_idle():
    assert delay_ms(0.0, now=100.0) == 0


def test_trailing_delay_inside_interval():
    assert delay_ms(10.000, now=10.010) == 23


def test_never_negative():
    assert delay_ms(10.0, now=10.5) == 0
