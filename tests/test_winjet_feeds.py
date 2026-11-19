"""Sources des Winjets (disques, météo, fichiers), bloc `note`, actions `open` / `openDrive`, filtrage des paquets PC Widgets retirés.

Sans réseau ni fenêtre : le réseau est remplacé, l'ouverture shell est interceptée.
"""
import os
import sys
import time
from pathlib import Path

import pytest

os.environ.setdefault('QT_QPA_PLATFORM', 'offscreen')
pytest.importorskip('PySide6.QtQml')

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'src' / 'frontend'))
sys.path.insert(0, str(Path(__file__).resolve().parent))

import bridge_winjets as bw  # noqa: E402
import winjet_expr as ex  # noqa: E402
import winjet_feeds as wf  # noqa: E402
from PySide6.QtCore import Q_ARG, QCoreApplication, QEvent, QMetaObject  # noqa: E402

from test_qml_runtime import bridge, messages, pump, qapp  # noqa: E402,F401
from test_winjets_qml import _tout, _trouver, journal, moteur, rendu  # noqa: E402,F401


# --- Fonctions pures ----------------------------------------------------------------------------------------------------


def test_disk_feed_shape_and_main_disk():
    usage = {'C:\\': (500 * wf.GB, 200 * wf.GB, 300 * wf.GB), 'D:\\': (1000 * wf.GB, 900 * wf.GB, 100 * wf.GB)}

    def read(path):
        if path not in usage:
            raise OSError
        return usage[path]

    disks = wf.disk_feed([('D:', 'Data'), ('C:', ''), ('E:', 'Absent')], read)
    assert [d['drive'] for d in disks] == ['D:', 'C:']
    assert disks[1] == {'drive': 'C:', 'label': '', 'usedGb': 200.0, 'totalGb': 500.0, 'freeGb': 300.0, 'percent': 40, 'ratio': 0.4}
    assert wf.main_disk(disks, 'C:')['drive'] == 'C:'
    assert wf.main_disk(disks, 'Z:')['drive'] == 'D:'
    assert wf.main_disk([]) == {}


def test_fixed_drives_returns_letters():
    for drive, label in wf.fixed_drives():
        assert len(drive) == 2 and drive[1] == ':' and isinstance(label, str)


def test_recent_files_max_8_kinds_and_no_urls():
    entries = [{'name': f'f{i}', 'comment': f'C:\\Users\\x\\f{i}.txt', 'icon': 'apps'} for i in range(12)]
    entries.insert(0, {'name': 'web', 'comment': 'https://example.com/a'})
    entries.insert(1, {'name': 'unc', 'comment': '\\\\server\\share\\a.txt'})
    entries.insert(2, {'name': 'dir', 'comment': 'C:\\Users\\x\\Docs', 'icon': 'folder'})
    entries.insert(3, {'name': 'app', 'comment': 'C:\\Tools\\a.exe', 'icon': 'apps'})
    out = wf.recent_files(entries)
    assert len(out) == 8
    assert [e['kind'] for e in out[:3]] == ['folder', 'app', 'file']
    assert all(set(e) == {'name', 'path', 'kind', 'icon', 'modified'} for e in out)


def test_known_folders_four_entries_with_fallback():
    out = wf.known_folders(lambda guid: '')
    assert [f['name'] for f in out] == ['Desktop', 'Downloads', 'Documents', 'Pictures']
    assert all(f['path'] for f in out)
    assert all(f['path'] for f in wf.known_folders())


def test_open_allowed_rejects_urls_and_unlisted_paths():
    allowed = {'C:\\Users\\x\\a.txt'}
    assert wf.open_allowed('C:\\Users\\x\\a.txt', allowed)
    for bad in ('https://example.com', 'http://x', 'file:///C:/a.txt', 'javascript:alert(1)', '\\\\srv\\share', ''):
        assert not wf.open_allowed(bad, allowed | {bad})  # même « autorisée » par erreur, une URL est refusée
    assert not wf.open_allowed('C:\\Windows\\notepad.exe', allowed)


def test_drive_valid():
    assert wf.drive_valid('c:', ['C:']) and not wf.drive_valid('D:', ['C:'])
    assert not wf.drive_valid('C:\\..', ['C:']) and not wf.drive_valid('https://x', ['C:'])


def test_weather_payload_shape_and_label_rules():
    forecast = {
        'current': {'temperature_2m': 12.4, 'apparent_temperature': 10.6, 'weather_code': 61},
        'daily': {'time': [f'2026-11-{18 + i}' for i in range(7)], 'weather_code': [0] * 7,
                  'temperature_2m_max': [14.2 + i for i in range(7)], 'temperature_2m_min': [6.1] * 7},
    }
    out = wf.weather_payload(forecast, 'Paris', 'city', 'en')
    assert out['status'] == 'ok' and out['source'] == 'city' and out['city'] == 'Paris'
    assert (out['temp'], out['feelsLike'], out['icon'], out['high'], out['low']) == (12, 11, 'rainy', 14, 6)
    assert len(out['daily']) == 7 and set(out['daily'][0]) == {'day', 'icon', 'max', 'min', 'rangeFrom', 'rangeTo'}
    assert (out['weekLow'], out['weekHigh']) == (6, 20) and out['daily'][6]['rangeTo'] == 1.0 and out['daily'][0]['rangeFrom'] == 0.0
    assert wf.location_label(48.85, 2.35, (48.86, 2.34, 'Paris')) == 'Paris'
    assert wf.location_label(10.0, 10.0, (48.86, 2.34, 'Paris')) == 'Current location'
    assert wf.location_label(10.0, 10.0, None) == 'Current location'
    assert wf.location_label(10.0, 10.0, None, 'Lyon') == 'Lyon'


def test_compute_weather_by_location_and_error(monkeypatch):
    forecast = {'current': {'temperature_2m': 3, 'weather_code': 0},
                'daily': {'time': ['2026-11-18'], 'weather_code': [0], 'temperature_2m_max': [5], 'temperature_2m_min': [1]}}
    monkeypatch.setattr(wf, 'fetch_json', lambda url: forecast if 'forecast' in url else {
        'results': [{'latitude': 48.85, 'longitude': 2.35, 'name': 'Paris'}]})
    out = wf.compute_weather('', 'location', {'lat': 48.9, 'lon': 2.3}, 'Paris')
    assert out['source'] == 'location' and out['city'] == 'Paris' and out['status'] == 'ok'

    def offline(url):
        raise OSError('offline')

    monkeypatch.setattr(wf, 'fetch_json', offline)
    assert wf.compute_weather('Rome', 'city', None, '')['status'] == 'error'


def test_expr_roots_include_new_feeds():
    ctx = {'weather': {'temp': 12}, 'files': {'recent': [{'name': 'a'}]}, 'system': {'diskMain': {'percent': 40}}}
    assert ex.render('{{ weather.temp }}|{{ files.recent[0].name }}|{{ system.diskMain.percent }}', ctx) == '12|a|40'


# --- Filtrage des coquilles PC Widgets -----------------------------------------------------------------------------------


def test_walidmpcwidgets_packages_and_instances_are_dropped():
    raw = {
        'packages': [{'id': 'io.github.walidmpcwidgets.original.clock'}, {'id': 'winlestia.horloge'}],
        'instances': [{'id': 'a', 'packageId': 'io.github.walidmpcwidgets.original.clock'}, {'id': 'b', 'packageId': 'winlestia.horloge'}],
        'data': {'a': {}, 'b': {}},
    }
    out = bw.drop_retired(raw)
    assert [p['id'] for p in out['packages']] == ['winlestia.horloge']
    assert [i['id'] for i in out['instances']] == ['b'] and list(out['data']) == ['b']
    assert len(raw['packages']) == 2  # entrée intacte
    clean = {'packages': [{'id': 'x'}], 'instances': []}
    assert bw.drop_retired(clean) is clean


def test_retired_host_is_gone():
    front = ROOT / 'src' / 'frontend'
    assert not (front / 'pc_widgets_original.py').exists() and not (front / 'pc_widgets_original_host.py').exists()
    assert 'originalActive' not in (front / 'bridge_winjets.py').read_text('utf-8')
    assert 'originalActive' not in (front / 'qml' / 'winjets' / 'WinjetWindow.qml').read_text('utf-8')


# --- Pont ---------------------------------------------------------------------------------------------------------------


def test_open_path_and_drive_actions(bridge, monkeypatch, tmp_path):
    opened = []
    monkeypatch.setattr(wf, '_shell_open', lambda path: opened.append(path) or True)
    feeds = bridge.winjets.feeds
    target = tmp_path / 'a.txt'
    target.write_text('x')
    feeds._allowed = {str(target)}
    assert feeds.openPath('https://example.com/x') is False
    assert feeds.openPath('http://example.com/x') is False
    assert feeds.openPath(str(tmp_path / 'inconnu.txt')) is False
    assert not opened
    assert feeds.openPath(str(target)) is True and opened == [str(target)]
    assert feeds.openDrive('https:') is False and feeds.openDrive('') is False
    drives = [d for d, _ in wf.fixed_drives()]
    if drives:
        assert feeds.openDrive(drives[0]) is True and opened[-1] == drives[0] + '\\'


def test_open_and_files_reserved_to_builtin_packages(bridge, monkeypatch, tmp_path):
    opened = []
    monkeypatch.setattr(wf, '_shell_open', lambda path: opened.append(path) or True)
    feeds = bridge.winjets.feeds
    target = tmp_path / 'a.txt'
    target.write_text('x')
    feeds._allowed = {str(target)}
    assert wf.is_builtin_package('winlestia.fichiers') and not wf.is_builtin_package('com.evil.x')
    assert not wf.is_builtin_package('') and not wf.is_builtin_package('winlestiax.a')
    assert feeds.openPathFor('com.evil.x', str(target)) is False and not opened
    assert feeds.openPathFor('winlestia.fichiers', str(target)) is True and opened == [str(target)]


def test_disk_feed_polls_only_while_held(bridge, monkeypatch):
    monkeypatch.setattr(wf, 'disk_feed',
        lambda: [{'drive': 'C:', 'label': '', 'usedGb': 1.0, 'totalGb': 2.0, 'freeGb': 1.0, 'percent': 50}])
    feeds = bridge.winjets.feeds
    assert not feeds._disk_timer.isActive()
    feeds.hold('disk', '')
    assert feeds._disk_timer.isActive() and feeds.property('diskMain')['percent'] == 50
    feeds.release('disk', '')
    assert not feeds._disk_timer.isActive()


def test_weather_auto_asks_backend_location_then_never_london(bridge, monkeypatch):
    calls = []
    monkeypatch.setattr(bridge, 'action', lambda name, args=None: calls.append(name))
    monkeypatch.setattr(wf.WinjetFeeds, '_spawn', lambda self, key, mode, location: calls.append((mode, key)))
    feeds = bridge.winjets.feeds
    bridge._state.pop('location', None)
    bridge.city = ''
    feeds.hold('weather', '')
    assert calls == ['locate'] and feeds.weatherFor('')['status'] == 'locating'
    bridge._state['location'] = {'ok': False, 'requested': True}
    feeds._start_weather('')
    assert feeds.weatherFor('')['status'] == 'unconfigured'
    bridge.city = 'Lyon'
    feeds._weather.pop('', None)
    feeds._start_weather('')
    assert calls[-1] == ('city', '')
    bridge._state['location'] = {'ok': True, 'lat': 1.0, 'lon': 2.0}
    feeds._busy.discard('')
    feeds._start_weather('', force=True)
    assert calls[-1] == ('location', '')
    feeds.release('weather', '')
    assert not feeds._weather_timer.isActive()
    bridge._state.pop('location', None)
    bridge.city = ''


def test_weather_initial_unrequested_location_sends_locate(bridge, monkeypatch):
    calls = []
    monkeypatch.setattr(bridge, 'action', lambda name, args=None: calls.append(name))
    monkeypatch.setattr(wf.WinjetFeeds, '_spawn', lambda self, key, mode, location: calls.append((mode, key)))
    feeds = bridge.winjets.feeds
    bridge._state['location'] = {'ok': False, 'lat': 0, 'lon': 0, 'accuracyM': 0, 'requested': False}
    bridge.city = 'Lyon'
    feeds.hold('weather', '')
    assert calls == ['locate'] and feeds.weatherFor('')['status'] == 'locating'
    feeds.release('weather', '')
    bridge._state.pop('location', None)
    bridge.city = ''


def test_weather_requested_failed_location_falls_back_to_city(bridge, monkeypatch):
    calls = []
    monkeypatch.setattr(bridge, 'action', lambda name, args=None: calls.append(name))
    monkeypatch.setattr(wf.WinjetFeeds, '_spawn', lambda self, key, mode, location: calls.append((mode, key)))
    feeds = bridge.winjets.feeds
    bridge._state['location'] = {'ok': False, 'lat': 0, 'lon': 0, 'accuracyM': 0, 'requested': True}
    bridge.city = 'Lyon'
    feeds._start_weather('')
    assert calls == [('city', '')]
    bridge._state.pop('location', None)
    bridge.city = ''


# --- QML ----------------------------------------------------------------------------------------------------------------


def _timers_running(obj):
    return [t for t in _trouver(obj, lambda o: o.metaObject().className().startswith('QQmlTimer')) if t.property('running')]


def test_note_block_loads_persists_debounced_and_idles(qapp, bridge, moteur, journal, monkeypatch):
    calls = []
    monkeypatch.setattr(bridge, 'action', lambda name, args=None: calls.append((name, args)))
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    inst = dict(bridge.winjets.instance('i-test.tout'))
    inst['state'] = {'text': 'bonjour'}
    inst['layouts'] = [{'minWidth': 0, 'root': {'type': 'column', 'children': [
        {'type': 'note', 'key': 'text', 'placeholder': 'Écrire'}]}}]
    obj.setProperty('inst', inst)
    pump(qapp, 20)
    areas = _trouver(obj, lambda o: o.metaObject().className().startswith(('QQuickTextArea', 'TextArea')))
    assert areas and areas[0].property('text') == 'bonjour'
    assert not _timers_running(obj)  # aucun minuteur au repos
    areas[0].setProperty('text', 'x' * 5000)
    assert len(areas[0].property('text')) == 4000
    areas[0].setProperty('text', 'nouveau')
    assert any(t.property('interval') == 600 for t in _timers_running(obj))
    assert not [c for c in calls if c[0] == 'winjets.setState']  # rien avant l'échéance
    deadline = time.monotonic() + 1.5
    while time.monotonic() < deadline and not [c for c in calls if c[0] == 'winjets.setState']:
        pump(qapp, 2)
        time.sleep(0.02)
    saves = [c for c in calls if c[0] == 'winjets.setState']
    assert len(saves) == 1 and saves[0][1]['key'] == 'text' and saves[0][1]['value'] == 'nouveau'
    assert not _timers_running(obj)
    assert not journal.alertes(), journal.alertes()
    obj.deleteLater()
    pump(qapp)


def test_renderer_holds_feeds_only_while_live(qapp, bridge, moteur, monkeypatch):
    monkeypatch.setattr(wf, 'disk_feed', lambda: [])
    feeds = bridge.winjets.feeds
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    _tout(obj, {'type': 'text', 'text': '{{ system.diskMain.percent }} {{ system.cpuHistory | length }}'})
    pump(qapp, 10)
    assert feeds._holds['disk'] == 1 and feeds._disk_timer.isActive()
    obj.setProperty('paused', True)
    pump(qapp, 5)
    assert feeds._holds['disk'] == 0 and not feeds._disk_timer.isActive()
    obj.setProperty('paused', False)
    pump(qapp, 5)
    assert feeds._holds['disk'] == 1
    obj.deleteLater()
    QCoreApplication.sendPostedEvents(None, QEvent.DeferredDelete)
    pump(qapp)
    assert feeds._holds['disk'] == 0


def test_open_actions_in_renderer_reject_urls(qapp, bridge, moteur, monkeypatch):
    opened = []
    monkeypatch.setattr(wf, '_shell_open', lambda path: opened.append(path) or True)
    obj = rendu(moteur, bridge, 'i-test.tout', 352, 352)
    for action in ({'type': 'open', 'path': 'https://example.com'}, {'type': 'open', 'path': 'file:///C:/Windows/notepad.exe'},
                   {'type': 'openDrive', 'drive': 'https://x'}):
        QMetaObject.invokeMethod(obj, 'runAction', Q_ARG('QVariant', action))
    assert not opened
    obj.deleteLater()
    pump(qapp)


def test_system_usage_demande_l_echantillonnage_aux_transitions():
    """SystemUsage.refCount doit relayer Bridge.usageRef(true/false) (sinon `performance` reste vide) ; le générateur aussi."""
    qml = (ROOT / 'src' / 'frontend' / 'qml' / 'qs' / 'services' / 'SystemUsage.qml').read_text(encoding='utf-8')
    gen = (ROOT / 'src' / 'frontend' / 'prepare_port.py').read_text(encoding='utf-8')
    for source in (qml, gen):
        assert 'onRefCountChanged' in source
        assert 'Bridge.usageRef(wanted)' in source
        assert 'refCount > 0' in source
