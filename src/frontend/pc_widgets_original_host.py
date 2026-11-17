"""Windows Qt Widgets process for the original PC Widgets cards."""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path
from threading import Thread

from PySide6.QtCore import QEvent, QObject, QPointF, QRectF, Qt
from PySide6.QtGui import QTransform


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


def content_layout(width, height, sizes, card_size):
    small_w, small_h = card_size("small")
    medium_w, medium_h = card_size("medium")
    if "medium" not in sizes:
        return sizes[0], min(width / small_w, height / small_h)

    # Grow the layout from a compact face to a wide one before increasing density.
    # Choosing a taller face must never reduce the scale of an already large card.
    reference_w = medium_w if "small" not in sizes else min(medium_w, max(small_w, width))
    scale = min(width / reference_w, height / medium_h)
    if "small" in sizes and width < 330:
        return "small", scale
    if "large" in sizes and height / scale >= card_size("large")[1] - 8:
        return "large", scale
    return "medium", scale


class ScaledContent:
    """Draw at the original density and scale the design to the available space."""

    content_scale = 1.0
    _logical_frame = None

    def resize_content(self, width, height, scale):
        previous = self.content_scale
        self.content_scale = scale
        self.extra_bottom = self.content_overhang()
        resize_card(self, width, height)
        if previous != self.content_scale:
            self.scale_changed()

    def content_overhang(self):
        return round(type(self).extra_bottom * self.content_scale)

    def scale_changed(self):
        pass

    def card_rect(self):
        if self._logical_frame is not None:
            return self._logical_frame
        return super().card_rect()

    def paint_content(self, painter, rect):
        scale = self.content_scale
        if scale == 1.0:
            return super().paint_content(painter, rect)

        frame = super().card_rect()
        logical = QRectF(frame.x(), frame.y(), frame.width() / scale, frame.height() / scale)
        transform = QTransform(scale, 0, 0, scale, frame.x() * (1 - scale), frame.y() * (1 - scale))
        inner = logical.adjusted(rect.left() - frame.left(), rect.top() - frame.top(),
                                 rect.right() - frame.right(), rect.bottom() - frame.bottom())
        self._logical_frame = logical
        self._physical_frame = frame
        self._frame_transform = painter.worldTransform()
        painter.save()
        painter.setWorldTransform(transform, True)
        try:
            super().paint_content(painter, inner)
        finally:
            painter.restore()
            self._logical_frame = None
        self.map_controls(transform)

    def map_controls(self, transform):
        pass

    def paint_in_frame(self, painter, paint, *args):
        logical = self._logical_frame
        if logical is None:
            return paint(painter, *args)
        self._logical_frame = None
        painter.save()
        painter.setWorldTransform(self._frame_transform)
        try:
            return paint(painter, *args)
        finally:
            painter.restore()
            self._logical_frame = logical


class ScaledWeather(ScaledContent):
    def _backdrop(self, painter, shape):
        return self.paint_in_frame(painter, super()._backdrop, shape)

    def map_controls(self, transform):
        if self._town_btn is not None:
            self._town_btn = transform.mapRect(self._town_btn)


class ScaledFiles(ScaledContent):
    def map_controls(self, transform):
        self._chrome = [(transform.mapRect(box), tag) for box, tag in self._chrome]
        self._rows = [(transform.mapRect(box), path, is_dir) for box, path, is_dir in self._rows]


class ScaledMedia(ScaledContent):
    def content_overhang(self):
        return super().content_overhang() + round(max(0, self.content_scale - 1) * 10)

    def scale_changed(self):
        self._dirty = True

    def map_controls(self, transform):
        scale = self.content_scale
        self._discs = [(name, transform.map(center), radius * scale, hit * scale)
                       for name, center, radius, hit in self._discs]
        self._buttons = [(name, center, hit) for name, center, _, hit in self._discs]
        if self._viz_rect is not None:
            self._viz_rect = transform.mapRect(self._viz_rect)

    def _paint_artwork(self, painter, card, track):
        # Preserve the frame's radius and crop the album cover at native resolution.
        if self._logical_frame is not None:
            card = self._physical_frame
        self.paint_in_frame(painter, super()._paint_artwork, card, track)

    def _draw_heart(self, painter, center, filled):
        painter.save()
        painter.translate(center)
        painter.scale(self.content_scale, self.content_scale)
        try:
            super()._draw_heart(painter, QPointF(), filled)
        finally:
            painter.restore()


class ScaledNotes(ScaledContent):
    def scale_changed(self):
        self._restyle()
        self._relayout()

    def _restyle(self):
        super()._restyle()
        font = self.edit.font()
        font.setPointSizeF(font.pointSizeF() * self.content_scale)
        self.edit.setFont(font)

    def _relayout(self):
        super()._relayout()
        frame = self.card_rect()
        original = QRectF(self.edit.geometry())
        scale = self.content_scale
        insets = (original.left() - frame.left(), original.top() - frame.top(),
                  original.right() - frame.right(), original.bottom() - frame.bottom())
        self.edit.setGeometry(frame.adjusted(*(inset * scale for inset in insets)).toAlignedRect())


def scaled_card_types(registry):
    adapters = {"weather": ScaledWeather, "media": ScaledMedia, "notes": ScaledNotes, "files": ScaledFiles}
    return {kind: type("Winlestia" + cls.__name__, (adapters.get(kind, ScaledContent), cls), {})
            for kind, cls in registry.items()}


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
    from widgets.core.card import card_size
    from widgets.core.glass import GlassBackdrop

    winapi.set_app_id("Winlestia.PCWidgetsWinjets")
    app = QApplication([sys.argv[0]])
    app.setQuitOnLastWindowClosed(False)
    load_builtin_tiles()
    typography.load_bundled()
    card_types = scaled_card_types(REGISTRY)

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
            cls = card_types.get(kind)
            if cls is None:
                return False
            card = self.cards.get(instance)
            if not target["visible"]:
                if card:
                    card.hide()
                return False
            screen = next((item for item in app.screens() if item.name() == target["screen"]), app.primaryScreen())
            if screen is None:
                return False
            geom = screen.availableGeometry()
            width, height = min(target["w"], geom.width()), min(target["h"], geom.height())
            if min(width, height) <= 0:
                return False
            size, scale = content_layout(width, height, cls.supported_sizes, card_size)
            is_new = card is None
            if is_new:
                card = self.make(instance, kind, cls, size)
            size_changed = card.size_class != size
            card.set_size_class(size)
            card.resize_content(width, height, scale)
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
