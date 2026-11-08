# Python 3.12 / wallpaper_thumbs.py / vignettes JPEG de 480 px en cache pour le sélecteur de fonds.
import hashlib, os
from pathlib import Path
from PySide6.QtCore import QSize
from PySide6.QtGui import QImage, QImageReader

THUMB_WIDTH = 480


def thumb_dir():
    return (
        Path(
            os.environ.get('WINLESTIA_DATA_DIR')
            or (Path(os.environ.get('LOCALAPPDATA', str(Path.home() / 'AppData' / 'Local'))) / 'Panneaux')
        )
        / 'Caelestia'
        / 'thumbs'
    )


def thumb_for(path):
    # Renvoie le fichier de vignette de `path` (clé chemin+mtime), en le générant s'il manque ; '' en cas d'échec.
    # Seule la taille réduite est décodée.
    try:
        stat = os.stat(path)
        key = hashlib.sha1(f'{path.lower()}|{stat.st_mtime_ns}|{stat.st_size}'.encode('utf-8')).hexdigest()[:24]
        target = thumb_dir() / f'{key}.jpg'
        if target.exists():
            return str(target)
        reader = QImageReader(path)
        reader.setAutoTransform(True)
        size = reader.size()
        if not size.isValid() or size.width() <= 0:
            return ''
        width = min(THUMB_WIDTH, size.width())
        reader.setScaledSize(QSize(width, max(1, round(size.height() * width / size.width()))))
        image = reader.read()
        if image.isNull():
            return ''
        target.parent.mkdir(parents=True, exist_ok=True)
        part = target.with_suffix('.tmp')
        if not image.save(str(part), 'JPEG', 82):
            return ''
        os.replace(part, target)
        return str(target)
    except OSError:
        return ''
