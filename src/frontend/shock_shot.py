# Python 3.12 / shock_shot.py / Qt 6. Capture d'écran pour l'onde de choc : mémoire seulement, jamais sur disque, jamais journalisée.
import time

import open_trace
from PySide6.QtCore import Property, QObject, Qt, Signal, Slot
from PySide6.QtGui import QGuiApplication, QImage
from PySide6.QtQuick import QQuickImageProvider

# Demi-résolution : le shader ne montre que l'anneau en mouvement, la netteté totale ne se voit pas et coûte 4 fois la mémoire.
DOWNSCALE = 2


def shrink(image, factor=DOWNSCALE):
    if image.isNull():
        return QImage()
    width = max(1, image.width() // factor)
    height = max(1, image.height() // factor)
    return image.scaled(width, height, Qt.IgnoreAspectRatio, Qt.FastTransformation)


def clamp_region(screen_w, screen_h, x, y, w, h):
    """Rectangle borné à l'écran (x, y, w, h), ou None s'il est vide."""
    left, top = max(0, x), max(0, y)
    right, bottom = min(screen_w, x + w), min(screen_h, y + h)
    if right <= left or bottom <= top:
        return None
    return left, top, right - left, bottom - top


class ShockShotProvider(QQuickImageProvider):
    """Sert image://shockshot/<graine>. Image nulle si rien n'est capturé."""

    def __init__(self):
        super().__init__(QQuickImageProvider.Image)
        self.image = QImage()

    def grab(self, screen, x, y, w, h):
        started = time.perf_counter()
        try:
            self.image = shrink(screen.grabWindow(0, x, y, w, h).toImage())
        except Exception:
            self.image = QImage()
        open_trace.mark(f'shock-grab ms={(time.perf_counter() - started) * 1000:.1f} w={w} h={h}')
        return not self.image.isNull()

    def clear(self):
        self.image = QImage()

    def requestImage(self, name, size, requested):
        image = self.image
        size.setWidth(image.width())
        size.setHeight(image.height())
        return image


class ShockShot(QObject):
    """Exposé à QML sous le nom ShockShot : capture(nomEcran, x, y, w, h) -> bool, release()."""

    seqChanged = Signal()

    def __init__(self, provider=None):
        super().__init__()
        self.provider = provider or ShockShotProvider()
        self._seq = 0

    def _seq_get(self):
        return self._seq

    seq = Property(int, _seq_get, notify=seqChanged)

    @Slot(str, int, int, int, int, result=bool)
    def capture(self, screen_name, x, y, w, h):
        """Capture la région (x, y, w, h), en pixels logiques locaux à l'écran, bornée à l'écran."""
        screen = next((s for s in QGuiApplication.screens() if s.name() == screen_name), None)
        region = clamp_region(screen.size().width(), screen.size().height(), x, y, w, h) if screen is not None else None
        if region is None or not self.provider.grab(screen, *region):
            self.provider.clear()
            return False
        self._seq += 1
        self.seqChanged.emit()
        return True

    @Slot()
    def release(self):
        self.provider.clear()

    @Slot(QObject)
    def placeTopmost(self, wave):
        """Fenêtre d'onde au premier plan sans activation (HWND_TOPMOST, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE)."""
        try:
            import ctypes

            if wave is None or not hasattr(ctypes, 'windll'):
                return
            ctypes.windll.user32.SetWindowPos(ctypes.c_void_p(int(wave.winId())), ctypes.c_void_p(-1), 0, 0, 0, 0, 0x13)
        except Exception:
            pass

    @Slot(QObject, QObject)
    def placeUnder(self, wave, shell):
        """Place la fenêtre d'onde juste sous le shell, sans activation, seulement si le shell est lui-même topmost :
        sinon l'onde perdrait son statut topmost et passerait sous les applications (elle reste alors topmost)."""
        try:
            import ctypes

            if wave is None or shell is None or not hasattr(ctypes, 'windll'):
                return
            user32 = ctypes.windll.user32
            hwnd = int(shell.winId())
            if not user32.GetWindowLongW(ctypes.c_void_p(hwnd), -20) & 0x8:
                return self.placeTopmost(wave)
            user32.SetWindowPos(ctypes.c_void_p(int(wave.winId())), ctypes.c_void_p(hwnd), 0, 0, 0, 0, 0x13)
        except Exception:
            pass
