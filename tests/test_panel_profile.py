"""Profilage des panneaux : sans WINLESTIA_PROFILE_PANELS tout est sans effet ; activé, repères et images manquées."""
import importlib
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src' / 'frontend'))
import panel_profile


class FakeSignal:
    def __init__(self):
        self.slots = []

    def connect(self, slot):
        self.slots.append(slot)

    def disconnect(self, slot):
        self.slots.remove(slot)


class FakeWindow:
    def __init__(self):
        self.frameSwapped = FakeSignal()


def reload_with(monkeypatch, value):
    if value is None:
        monkeypatch.delenv('WINLESTIA_PROFILE_PANELS', raising=False)
    else:
        monkeypatch.setenv('WINLESTIA_PROFILE_PANELS', value)
    return importlib.reload(panel_profile)


def test_frame_stats_counts_intervals_over_20_ms():
    frames, missed, worst = panel_profile.frame_stats([0.0, 0.016, 0.033, 0.070, 0.086])
    assert (frames, missed) == (5, 1)
    assert abs(worst - 37.0) < 1e-6


def test_frame_stats_empty_and_single():
    assert panel_profile.frame_stats([]) == (0, 0, 0.0)
    assert panel_profile.frame_stats([1.0]) == (1, 0, 0.0)


def test_disabled_is_noop_and_never_connects(monkeypatch):
    module = reload_with(monkeypatch, None)
    window = FakeWindow()
    lines = []
    assert module.ENABLED is False
    module.mark('x')
    module.begin('media', window)
    assert module.tick() == 0.0
    module.tock('surfaces', 1.0)
    module.end('media', emit=lines.append)
    assert window.frameSwapped.slots == []
    assert lines == [] and module._marks == []
    reload_with(monkeypatch, None)


def test_enabled_records_marks_and_logs_one_line(monkeypatch):
    module = reload_with(monkeypatch, '1')
    window = FakeWindow()
    lines = []
    module.mark('open-request')
    module.begin('media', window)
    assert len(window.frameSwapped.slots) == 1
    module.mark('first-frame')
    window.frameSwapped.slots[0]()
    window.frameSwapped.slots[0]()
    module.tock('surfaces', module.tick())
    module.end('media', emit=lines.append)
    assert window.frameSwapped.slots == []
    assert len(lines) == 1
    line = lines[0]
    for text in ('panel=media', 'duration=', 'frames=2', 'missed=0', 'worst=', 'open-request@', 'first-frame@', 'surfaces='):
        assert text in line
    module.end('media', emit=lines.append)
    assert len(lines) == 1
    reload_with(monkeypatch, None)


def test_format_line_contains_all_fields():
    line = panel_profile.format_line('perf', 250.0, 12, 3, 41.5, [('anim-start', 1.0)], {'glass': 4.2})
    assert 'panel=perf' in line and 'frames=12' in line and 'missed=3' in line
    assert 'worst=41.5ms' in line and 'anim-start@+1.0' in line and 'glass=4.2ms' in line
