# Python 3.12 / lock_shot.py / Qt 6. Captures d'écran floutées de l'écran de verrouillage : mémoire seulement, jamais sur disque.
from PySide6.QtCore import Qt
from PySide6.QtGui import QImage
from PySide6.QtQuick import QQuickImageProvider

DOWNSCALE = 4


def shrink(image, factor=DOWNSCALE):
    if image.isNull():
        return QImage()
    width = max(1, image.width() // factor)
    height = max(1, image.height() // factor)
    return image.scaled(width, height, Qt.IgnoreAspectRatio, Qt.SmoothTransformation)


class LockShotProvider(QQuickImageProvider):
    """Sert image://lockshot/<indice d'écran>?<seq>. Image nulle si rien n'a été capturé (QML retombe sur un fond opaque)."""

    def __init__(self):
        super().__init__(QQuickImageProvider.Image)
        self.shots = {}
        self.seq = 0

    def capture(self, screens):
        shots = {}
        for index, screen in enumerate(screens):
            try:
                image = shrink(screen.grabWindow(0).toImage())
            except Exception:
                image = QImage()
            if not image.isNull():
                shots[str(index)] = image
        self.shots = shots
        self.seq += 1

    def clear(self):
        self.shots = {}

    def requestImage(self, name, size, requested):
        image = self.shots.get(name.split('?')[0], QImage())
        size.setWidth(image.width())
        size.setHeight(image.height())
        return image
