"""Dock Qt Quick validation: runs tools/dock_ui_probe.py offscreen (isolated pipe, synthetic state) and asserts its report."""
import json
import os
import subprocess
import sys
from pathlib import Path

import pytest

root = Path(__file__).resolve().parents[1]
probe = root / 'tools' / 'dock_ui_probe.py'
report_file = root / 'build' / 'dock-ui-report.json'

EXPECTED = {
    'rootLoaded': 'the QML root object must load',
    'dockCentered': 'the dock must be horizontally centered on the screen',
    'dragEnqueuesReorder': 'dragging a dock app must enqueue a dockApp/reorder command',
    'backgroundTab': 'the context menu must keep the background tab flag',
    'powerAwaitsConfirmation': 'the power menu must wait for confirmation (powerChoice empty)',
    'sliderStaleGuard': 'a stale volume update must not override the value just set by the user',
}


def test_dock_ui_report():
    pytest.importorskip('PySide6', reason='PySide6 is required (pip install -r src/frontend/requirements.txt)')
    report_file.parent.mkdir(exist_ok=True)
    report_file.unlink(missing_ok=True)
    env = {**os.environ, 'QT_QPA_PLATFORM': 'offscreen'}
    result = subprocess.run([sys.executable, str(probe)], capture_output=True, timeout=60, env=env, cwd=root)
    assert report_file.exists(), f'probe produced no report (exit {result.returncode}): {result.stderr[-500:]!r}'
    report = json.loads(report_file.read_text(encoding='utf-8'))
    assert 'missingDelegate' not in report, f'dock delegate dock-app-a not found, QML issues: {report.get("missingDelegate")}'
    for key, message in EXPECTED.items():
        assert report.get(key), f'{key}: {message}'
    assert not report.get('qmlErrors'), f'QML errors: {report.get("qmlErrors")}'
    assert report.get('passed'), 'probe summary flag "passed" is false'
