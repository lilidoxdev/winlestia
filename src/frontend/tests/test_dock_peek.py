# Révélation du dock en plein écran (Win seul) : activation, bascule et arrêt, uniquement sur un écran en plein écran.
import sys, unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from bridge_pointer import PointerMixin


class Screen:
    def __init__(self, name):
        self._name = name

    def name(self):
        return self._name


class Signal:
    def __init__(self):
        self.sent = []

    def emit(self, *args):
        self.sent.append(args)


class Host(PointerMixin):
    def __init__(self, fullscreen):
        self._screen = Screen('A')
        self.fullscreen_names = fullscreen
        self.frameChanged = Signal()
        self.shellEvent = Signal()


class DockPeekTests(unittest.TestCase):
    def test_ignored_outside_fullscreen(self):
        host = Host(())
        self.assertIsNone(host.dock_peek_toggle())
        self.assertEqual(host.peek_screen, '')

    def test_set_then_toggle_off(self):
        host = Host(('A',))
        self.assertEqual(host.dock_peek_toggle(), 'on')
        self.assertEqual(host.peek_screen, 'A')
        self.assertEqual(host.dock_peek_toggle(), 'off')
        self.assertEqual(host.peek_screen, '')
        self.assertIn(('dockHide',), host.shellEvent.sent)

    def test_clear_is_idempotent(self):
        host = Host(('A',))
        host.dock_peek_clear()
        self.assertEqual(host.shellEvent.sent, [])

    def test_ends_when_fullscreen_disappears(self):
        host = Host(('A',))
        host.dock_peek_toggle()
        host.dock_peek_sync()
        self.assertEqual(host.peek_screen, 'A')
        host.fullscreen_names = ()
        host.dock_peek_sync()
        self.assertEqual(host.peek_screen, '')


if __name__ == '__main__':
    unittest.main()
