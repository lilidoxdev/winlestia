"""Géométrie des Winjets : clé d'écran, rescale, récupération, placement, aimantage (fonctions pures)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src' / 'frontend'))

import pytest  # noqa: E402

pytest.importorskip('PySide6.QtCore')
import bridge_winjets as g  # noqa: E402

D1 = '\\\\.\\DISPLAY1'
D2 = '\\\\.\\DISPLAY2'


def screen(name, pw, ph, w, h, primary=False, x=0, y=0):
    return {'key': g.screen_key(name, pw, ph), 'name': name, 'x': x, 'y': y, 'w': w, 'h': h, 'primary': primary}


def geo(key, x, y, w=352, h=168, **extra):
    return {'screen': key, 'x': x, 'y': y, 'w': w, 'h': h, **extra}


def test_screen_key_roundtrip():
    key = g.screen_key(D1, 2560, 1440)
    assert key == D1 + '|2560x1440'
    assert g.parse_key(key) == (D1, (2560, 1440))
    assert g.parse_key('n/a') == ('n/a', None)


def test_size_limits_follow_the_engine():
    assert g.clamp_size(50, 50) == (120, 120)
    assert g.clamp_size(5000, 5000) == (1600, 1000)
    assert g.clamp_size(300, 300, (200, 200), (250, 400)) == (250, 300)
    assert g.clamp_size(100, 100, (10, 10), (50, 50)) == (120, 120)  # min moteur 120, max jamais sous le min


def test_same_screen_is_only_clamped():
    s = [screen(D1, 1920, 1080, 1920, 1040, True)]
    target, out = g.reconcile_geometry(geo(s[0]['key'], 1800, 1000), s)
    assert target is s[0]
    assert (out['x'], out['y']) == (1920 - 352, 1040 - 168)
    assert out['recovered'] is False


def test_resolution_change_rescales_proportionally_then_clamps():
    old = g.screen_key(D1, 2560, 1440)
    s = [screen(D1, 1920, 1080, 1920, 1040, True)]
    _, out = g.reconcile_geometry(geo(old, 1280, 700, nx=0.5, ny=0.5), s)
    assert out['screen'] == s[0]['key']
    assert (out['x'], out['y']) == (960, 520)
    _, out = g.reconcile_geometry(geo(old, 2500, 1400, nx=0.99, ny=0.99), s)
    assert out['x'] + out['w'] <= 1920 and out['y'] + out['h'] <= 1040
    assert out['nx'] == round(out['x'] / 1920, 6)


def test_missing_screen_goes_to_primary_and_is_marked_recovered():
    s = [screen(D1, 1920, 1080, 1920, 1040, True)]
    target, out = g.reconcile_geometry(geo(D2 + '|1920x1080', 100, 100, nx=0.25, ny=0.25), s)
    assert target['name'] == D1
    assert out['recovered'] is True
    assert out['screen'] == s[0]['key']
    assert (out['x'], out['y']) == (480, 260)


def test_recovered_flag_is_kept_while_screen_matches_exactly():
    s = [screen(D1, 1920, 1080, 1920, 1040, True)]
    _, out = g.reconcile_geometry(geo(s[0]['key'], 10, 10, recovered=True), s)
    assert out['recovered'] is True


def test_secondary_screen_is_preferred_when_present():
    s = [screen(D1, 1920, 1080, 1920, 1040, True), screen(D2, 1280, 720, 1280, 680, x=1920)]
    target, out = g.reconcile_geometry(geo(D2 + '|1280x720', 100, 50), s)
    assert target['name'] == D2 and out['x'] == 100


def test_unplaced_instance_returns_none():
    s = [screen(D1, 1920, 1080, 1920, 1040, True)]
    assert g.reconcile_geometry({}, s) is None
    assert g.reconcile_geometry(geo('', 0, 0), s) is None
    assert g.reconcile_geometry(geo(s[0]['key'], 0, 0, w=0), s) is None
    assert g.reconcile_geometry(geo(s[0]['key'], 0, 0), []) is None


def test_clamp_shrinks_card_larger_than_work_area():
    assert g.clamp_rect(10, 10, 500, 400, 300, 200) == (0, 0, 300, 200)


def test_first_free_slot_starts_at_margin_and_flows_right():
    assert g.first_free_slot(1920, 1040, 352, 168, []) == (16, 16)
    x, y = g.first_free_slot(1920, 1040, 352, 168, [(16, 16, 352, 168)])
    assert (x, y) == (16 + 352 + 16, 16)  # gouttière 16 respectée


def test_first_free_slot_wraps_to_next_row():
    row = [(16, 16, 352, 168), (384, 16, 352, 168), (752, 16, 352, 168)]
    x, y = g.first_free_slot(1140, 700, 352, 168, row)
    assert x == 16 and y == 16 + 168 + 16
    for rect in row:
        assert not g._overlaps((x, y, 352, 168), rect, 16)


def test_first_free_slot_falls_back_to_cascade_inside_the_work_area():
    x, y = g.first_free_slot(400, 300, 352, 168, [(16, 16, 368, 268)])
    assert 0 <= x <= 400 - 352 and 0 <= y <= 300 - 168


def test_snap_move_aligns_edges_of_other_cards():
    other = (400, 100, 300, 168)
    x, y, guides = g.snap_move((403, 105, 352, 168), [other], (1920, 1040))
    assert x == 400 and y == 100
    assert {gd['axis'] for gd in guides} == {'x', 'y'}
    assert next(gd for gd in guides if gd['axis'] == 'x')['pos'] == 400


def test_snap_move_centers_align():
    other = (500, 0, 200, 200)  # centre x = 600
    x, _, guides = g.snap_move((600 - 176 + 5, 700, 352, 168), [other], (1920, 1040))
    assert x == 600 - 176
    assert any(gd['pos'] == 600 for gd in guides)


def test_snap_move_work_area_edges_use_margin_16():
    x, y, _ = g.snap_move((21, 12, 352, 168), [], (1920, 1040))
    assert (x, y) == (16, 16)
    x, _, _ = g.snap_move((1920 - 352 - 16 + 6, 600, 352, 168), [], (1920, 1040))
    assert x == 1920 - 16 - 352


def test_snap_move_threshold_is_8():
    x, _, _ = g.snap_move((16 + 9, 500, 352, 168), [], (1920, 1040))
    assert x != 16
    x, _, _ = g.snap_move((16 + 8, 500, 352, 168), [], (1920, 1040))
    assert x == 16


def test_snap_move_grid_8_without_guides():
    x, y, guides = g.snap_move((203, 333, 352, 168), [], (1920, 1040))
    assert (x, y) == (200, 336)
    assert guides == []


def test_snap_resize_east_edge_to_other_left_edge():
    other = (800, 0, 300, 300)
    x, y, w, h, guides = g.snap_resize((100, 100, 700 - 3, 168), 'e', [other], (1920, 1040))
    assert x == 100 and x + w == 800
    assert guides and guides[0]['pos'] == 800


def test_snap_resize_respects_min_and_max():
    _, _, w, _, _ = g.snap_resize((100, 100, 200, 200), 'e', [], (1920, 1040), (168, 168), (720, 352))
    assert w >= 168
    _, _, w, h, _ = g.snap_resize((100, 100, 900, 200), 'se', [], (1920, 1040), (168, 168), (720, 352))
    assert w <= 720 and h <= 352
    _, _, w, h, _ = g.snap_resize((300, 300, 40, 40), 'w', [], (1920, 1040))
    assert w >= 120 and h == 40  # min moteur 120 en largeur ; la hauteur n'est pas touchée par la poignée 'w'


def test_snap_resize_west_keeps_right_edge():
    x, _, w, _, _ = g.snap_resize((203, 100, 400, 200), 'w', [], (1920, 1040))
    assert x + w == 603


def test_layout_selection_uses_last_matching():
    layouts = [{'minWidth': 0}, {'minWidth': 340}, {'minWidth': 520, 'minHeight': 300}]
    assert g.select_layout(layouts, 168, 168) == 0
    assert g.select_layout(layouts, 339, 400) == 0
    assert g.select_layout(layouts, 340, 168) == 1
    assert g.select_layout(layouts, 600, 299) == 1
    assert g.select_layout(layouts, 520, 300) == 2
    assert g.select_layout([], 100, 100) == 0


def test_flatten_data_merges_value_and_meta():
    data = {'btc': {'value': {'price': 3}, 'updated': 'x', 'error': None, 'stale': True}, 'list': {'value': [1, 2]}}
    out = g.flatten_data(data)
    assert out['btc']['price'] == 3 and out['btc']['stale'] is True
    assert out['list'] == [1, 2]


def test_default_settings_fill_missing():
    definition = {'settings': [{'id': 'a', 'default': 1}, {'id': 'b', 'default': 'x'}]}
    assert g.default_settings(definition, {'b': 'y'}) == {'a': 1, 'b': 'y'}


def test_open_url_requires_https_and_declared_host():
    assert g.host_allowed('https://api.example.com/x', ['api.example.com'], '')
    assert g.host_allowed('https://exemple.com/', [], 'https://exemple.com')
    assert not g.host_allowed('http://api.example.com/x', ['api.example.com'], '')
    assert not g.host_allowed('https://evil.com/', ['api.example.com'], '')
    assert not g.host_allowed('file:///c:/windows', ['api.example.com'], '')


def test_diff_snapshots_reports_only_changed_ids():
    from bridge_winjets import diff_snapshots

    old = {'a': (1, 'x'), 'b': (2, 'y'), 'c': (3, 'z')}
    assert diff_snapshots(old, dict(old)) == []
    assert diff_snapshots(old, {**old, 'b': (2, 'changed')}) == ['b']
    assert diff_snapshots(old, {'a': old['a'], 'b': old['b'], 'd': (4, 'w')}) == ['c', 'd']


# --- Grille de cellules, repères de symétrie, Alt ------------------------------------------------------------------


def test_cell_pitch_matches_package_sizes():
    assert (g.CELL, g.GUTTER, g.PITCH) == (168, 16, 184)
    assert 4 * g.CELL + 3 * g.GUTTER == 720 and 2 * g.CELL + g.GUTTER == 352


def test_grid_line_starts_and_ends():
    assert g.grid_line(20) == 16 and g.grid_line(205) == 200
    assert g.grid_line(16 + 168 + 3, end=True) == 16 + 168  # fin de la première cellule
    assert g.grid_line(16 + 2 * 184 - 16 - 2, end=True) == 16 + 2 * 184 - 16


def test_snap_move_snaps_to_cell_line_without_guide():
    x, y, guides = g.snap_move((200 + 5, 16 + 184 - 6, 352, 168), [], (1920, 1040))
    assert (x, y) == (200, 16 + 184) and guides == []


def test_snap_move_end_edge_snaps_to_cell_end():
    x, _, _ = g.snap_move((16 + 184 + 4 - 0.0, 500, 352, 168), [], (1920, 1040))
    # le bord droit (16 + 184 + 4 + 352 = 556) n'est pas proche d'une fin de cellule : on reste sur le début (200)
    assert x == 200
    x, _, _ = g.snap_move((16 + 3, 500, 352 + 4, 168), [], (1920, 1040))
    assert x == 16


def test_snap_move_screen_center_guide_beats_grid():
    x, _, guides = g.snap_move((960 - 176 + 4, 500, 352, 168), [], (1920, 1040))
    assert x == 960 - 176 and any(a['axis'] == 'x' and a['pos'] == 960 for a in guides)


def test_snap_move_alt_disables_snapping_and_guides():
    rect = (960 - 176 + 4, 503, 352, 168)
    x, y, guides = g.snap_move(rect, [(100, 100, 352, 168)], (1920, 1040), snap=False)
    assert (x, y, guides) == (rect[0], rect[1], [])


def test_snap_resize_size_follows_cells():
    # 4 × 2 cellules : largeur 720, hauteur 352, à partir de (16, 16)
    _, _, w, h, guides = g.snap_resize((16, 16, 718, 355), 'se', [], (1920, 1040))
    assert (w, h) == (720, 352) and guides == []


def test_snap_resize_alt_keeps_raw_size():
    _, _, w, h, guides = g.snap_resize((16, 16, 718, 355), 'se', [], (1920, 1040), snap=False)
    assert (w, h, guides) == (718, 355, [])


def test_snap_resize_still_clamps_with_alt():
    _, _, w, _, _ = g.snap_resize((100, 100, 200, 200), 'e', [], (1920, 1040), (168, 168), (720, 352), snap=False)
    assert w == 200
    _, _, w, _, _ = g.snap_resize((100, 100, 900, 200), 'e', [], (1920, 1040), (168, 168), (720, 352), snap=False)
    assert w == 720
