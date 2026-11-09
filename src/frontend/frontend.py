# Python 3.12 / Qt Quick 6.10, Windows 11. GPL-3.0-or-later ; hôte natif du QML amont Caelestia.
"""Point d'entrée mince du frontend. La logique vit dans :
app_state (chemins, journal Qt), win_native (ctypes Win32), bridge_client (canal nommé),
windows_player, bridge (état/slots QML) et app_main (démarrage)."""
from __future__ import annotations
import os as _os

for _k in ("OPENBLAS_NUM_THREADS", "OMP_NUM_THREADS", "MKL_NUM_THREADS"):
    _os.environ.setdefault(_k, "1")

from PySide6.QtGui import QCursor  # noqa: E402,F401
from PySide6.QtQml import QQmlApplicationEngine  # noqa: E402,F401
from app_state import ROOT, QML_ISSUES  # noqa: E402,F401  (réexportés pour les sondes de tools/)
from win_native import user32, MonitorInfo  # noqa: E402,F401
from bridge import Bridge  # noqa: E402,F401  (réexporté pour les sondes)
from app_main import main  # noqa: E402

if __name__ == '__main__':
    raise SystemExit(main())
