"""Windows Qt Widgets process for the original PC Widgets cards."""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path
from threading import Thread

from PySide6.QtCore import QEvent, QObject, Qt


def send(message):
    print(json.dumps(message, ensure_ascii=True), flush=True)


class EditOnDoubleClick(QObject):
    def __init__(self, card, kind, request):
        super().__init__(card)
        self.kind = kind
        self.request = request

    def eventFilter(self, card, event):
        if event.type() != QEvent.MouseButtonDblClick or event.button() != Qt.LeftButton:
            return False
        pos = event.position()
        if not card.card_rect().contains(pos):
            return False
        if self.kind == "weather" and card.town_button_at(pos):
            return False
        if self.kind == "media" and card._hit(pos):
            return False
        if self.kind == "files" and (card._hit(pos)[1] or card._in_grip(pos)):
            return False
        self.request()
        return True


def resize_card(card, width, height):
    rect = card.card_rect()
    chrome_w = card.width() - round(rect.width())
    chrome_h = card.height() - round(rect.height())
    card.resize(width + chrome_w, height + chrome_h)


def main(runtime):
    source = runtime / "PC-Widgets-1.1.0"
    sys.path.insert(0, str(source))
    os.chdir(source)
    # The original entry point sets Qt's DPI policy before the application starts.
    import main as original_main  # noqa: F401
    from PySide6.QtCore import QTimer, Signal
    from PySide6.QtWidgets import QApplication
    from widgets.core import palette, typography, winapi
    from widgets.core.board import REGISTRY, load_builtin_tiles
    from widgets.core.glass import GlassBackdrop

    winapi.set_app_id("Winlestia.PCWidgetsWinjets")
    app = QApplication([sys.argv[0]])
    app.setQuitOnLastWindowClosed(False)
    load_builtin_tiles()
    typography.load_bundled()

    class Host(QObject):
        incoming = Signal(object)
        disconnected = Signal()

        def __init__(self):
            super().__init__()
            self.cards = {}
            self.state_file = runtime / "winjet-settings.json"
            self.settings = self.load_settings()
            self.glass = GlassBackdrop()
            self.glass.refresh()
            self.glass.notifier.ready.connect(self.repaint)
            self.look = palette.build("dark", "blue")
            self.incoming.connect(self.apply)
            self.disconnected.connect(app.quit)
            self.refresh = QTimer(self, interval=20_000)
            self.refresh.timeout.connect(self.glass.refresh)
            self.refresh.start()
            self.layer = QTimer(self, interval=1500)
            self.layer.timeout.connect(self.park)
            self.layer.start()

        def load_settings(self):
            try:
                saved = json.loads(self.state_file.read_text(encoding="utf-8"))
            except (OSError, ValueError):
                return {}
            return saved if isinstance(saved, dict) else {}

        def save(self, instance):
            card = self.cards.get(instance)
            if not card:
                return
            self.settings[instance] = dict(card.config)
            pending = self.state_file.with_suffix(".tmp")
            try:
                pending.write_text(json.dumps(self.settings, ensure_ascii=False), encoding="utf-8")
                pending.replace(self.state_file)
            except (OSError, TypeError, ValueError) as exc:
                print(f"PC Widgets: settings: {exc}", file=sys.stderr)

        def make(self, instance, kind, cls, size):
            config = dict(cls.new_config())
            if kind == "weather":
                config.update(lat=51.5072, lon=-0.1276, place="London, GB", faces={"medium": "forecast"})
            saved = self.settings.get(instance)
            if isinstance(saved, dict):
                config.update(saved)
            card = cls(self.look, size, config)
            card.glass = self.glass
            card.locked = True
            card._winlestia_edit_filter = EditOnDoubleClick(card, kind, lambda: send({"edit": instance}))
            card.installEventFilter(card._winlestia_edit_filter)
            card.changed.connect(lambda: self.save(instance))
            card.removed.connect(lambda *_: send({"remove": instance}))
            self.cards[instance] = card
            return card

        def apply(self, desired):
            if not isinstance(desired, dict):
                return
            for instance in set(self.cards) - desired.keys():
                card = self.cards.pop(instance)
                card._timer.stop()
                card.close()
                # The original HTTP tiles still emit their Qt signal from in-flight requests.
                QTimer.singleShot(200, lambda card=card: self.dispose(card))
            active = []
            for instance, target in desired.items():
                try:
                    if self.apply_card(instance, target):
                        active.append(instance)
                except (ValueError, TypeError, KeyError, RuntimeError) as exc:
                    print(f"PC Widgets: card {instance}: {exc}", file=sys.stderr)
            send({"active": active})

        def dispose(self, card):
            if getattr(card, "_busy", False) or getattr(card, "_fetching", False):
                QTimer.singleShot(500, lambda: self.dispose(card))
            else:
                card.deleteLater()

        def apply_card(self, instance, target):
            kind = target["kind"]
            cls = REGISTRY.get(kind)
            if cls is None:
                return False
            width, height = target["w"], target["h"]
            sizes = cls.supported_sizes
            size = "large" if width >= 330 and height >= 330 and "large" in sizes else (
                "medium" if width >= 330 and "medium" in sizes else sizes[0]
            )
            card = self.cards.get(instance)
            is_new = card is None
            if not target["visible"]:
                if card:
                    card.hide()
                return False
            if is_new:
                card = self.make(instance, kind, cls, size)
            size_changed = card.size_class != size
            card.set_size_class(size)
            screen = next((item for item in app.screens() if item.name() == target["screen"]), app.primaryScreen())
            if screen is None:
                return False
            geom = screen.availableGeometry()
            resize_card(card, min(width, geom.width()), min(height, geom.height()))
            rect = card.card_rect()
            left = max(0, min(round(geom.width() * target["nx"]), geom.width() - round(rect.width())))
            top = max(0, min(round(geom.height() * target["ny"]), geom.height() - round(rect.height())))
            card.move(geom.x() + left - round(rect.x()), geom.y() + top - round(rect.y()))
            opacity = target.get("opacity")
            card.opacity_ = 1.0 if opacity is None else max(0, min(1, float(opacity) / 100))
            if is_new:
                card.start()
            else:
                card.show()
                if size_changed:
                    card._tick()
            winapi.send_to_bottom(int(card.winId()))
            card.update()
            return card.isVisible()

        def repaint(self):
            for card in self.cards.values():
                if card.isVisible():
                    card.update()

        def park(self):
            for card in self.cards.values():
                if card.isVisible():
                    winapi.send_to_bottom(int(card.winId()))

    host = Host()

    def listen():
        try:
            for line in sys.stdin:
                try:
                    host.incoming.emit(json.loads(line))
                except ValueError:
                    continue
        finally:
            host.disconnected.emit()

    Thread(target=listen, daemon=True, name="winlestia-control").start()
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main(Path(sys.argv[1]).resolve()))
