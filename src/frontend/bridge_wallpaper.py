"""Fonds d’écran : choix, import, vignettes et application."""
from __future__ import annotations
import ctypes
import os
import threading
from pathlib import Path
from PySide6.QtCore import QCoreApplication, Slot
from PySide6.QtWidgets import QFileDialog
from win_native import kernel32
from wallpaper_thumbs import thumb_for
from search import (
    user_wallpaper_dir,
    wallpaper_choices,
    wallpaper_hidden,
    wallpaper_hide,
    wallpaper_import,
    wallpaper_known,
    wallpaper_refresh,
    wallpaper_remove,
    wallpaper_restore_hidden,
)


class WallpaperMixin:
    """Fonds d’écran : choix, import, vignettes et application."""

    @Slot(str, result='QVariantList')
    def wallpaperChoices(self, query):
        return wallpaper_choices(query)

    @Slot()
    def refreshWallpapers(self):
        wallpaper_refresh(self.wallpapersReady.emit)

    @Slot()
    def refreshWallpapersNow(self):
        wallpaper_refresh(self.wallpapersReady.emit, True)

    @Slot(result='QVariantList')
    def addWallpaperImages(self):
        # Boîte de dialogue native à sélection multiple (le sélecteur est masqué entre-temps par l'appelant), puis
        # copie dans le dossier utilisateur ; renvoie les nouveaux chemins.
        files, _ = QFileDialog.getOpenFileNames(
            None,
            QCoreApplication.translate('', 'Ajouter des images'),
            str(Path.home() / 'Pictures'),
            'Images (*.jpg *.jpeg *.png *.bmp *.webp)',
        )
        return wallpaper_import(files) if files else []

    @Slot()
    def openWallpaperFolder(self):
        folder = user_wallpaper_dir()
        try:
            folder.mkdir(parents=True, exist_ok=True)
            os.startfile(str(folder))
        except OSError:
            # Ouverture du dossier facultative : le bouton reste sans effet.
            pass

    @Slot(str, result=bool)
    def removeWallpaper(self, path):
        return wallpaper_remove(path)

    @Slot(str, result=bool)
    def hideWallpaper(self, path):
        return wallpaper_hide(path)

    @Slot(result=int)
    def hiddenWallpaperCount(self):
        return len(wallpaper_hidden())

    @Slot(result=bool)
    def restoreHiddenWallpapers(self):
        return wallpaper_restore_hidden()

    @Slot('QVariantList')
    def requestWallpaperThumbs(self, paths):
        # Vignettes du sélecteur : un thread par demande (une demande plus récente arrête l'ancienne), chaque fichier
        # émis dès que son JPEG en cache existe.
        self._thumb_job = getattr(self, '_thumb_job', 0) + 1
        job = self._thumb_job
        paths = [str(p) for p in paths]

        def run():
            for path in paths:
                if job != self._thumb_job:
                    return
                made = thumb_for(path)
                if made:
                    try:
                        self.wallpaperThumbReady.emit(path, Path(made).as_uri())
                    except RuntimeError:
                        # Objet Qt déjà détruit à la fermeture : plus rien à notifier.
                        return

        threading.Thread(target=run, daemon=True).start()

    def stopWallpaperThumbs(self):
        # À la fermeture : toute demande en cours est périmée, le thread de vignettes s'arrête avant d'émettre.
        self._thumb_job = getattr(self, '_thumb_job', 0) + 1

    @Slot()
    def requestWallpaperPicker(self):
        # Écrans et logiciels de fond animé en cours, demandés une fois à l'ouverture du sélecteur (le backend ne lit
        # que des noms de processus ; aucun minuteur).
        self._wpicker = {}
        # rien de la session précédente (avertissement, écrans débranchés) tant que la réponse est en attente
        self.wallpaperPickerChanged.emit()
        if self.client is not None:
            self.client.commands.put({'command': 'wallpaperPicker', 'args': {}})
            kernel32.SetEvent(self.client.wakeup)

    @Slot(str)
    def setWallpaper(self, path):
        self.applyWallpaper(path, 0)

    @Slot(str, int)
    def applyWallpaper(self, path, display):
        # Uniquement les fichiers du dernier balayage. Le backend l'applique (IDesktopWallpaper, écran 0 = tous les
        # écrans), incrémente sa propre révision et répond avec la liste des logiciels animés pour la ligne du
        # sélecteur.
        if not wallpaper_known(path):
            return
        if self.client is None or self.client.handle is None:
            return self.applyWallpaperLocally(path, int(display))
        self.client.commands.put({'command': 'wallpaperApply', 'args': {'path': path, 'display': int(display)}})
        kernel32.SetEvent(self.client.wakeup)

    def applyWallpaperLocally(self, path, display):
        # Backend absent ou pipe non connecté : ancien appel direct à SPI (tous les écrans seulement). Le
        # wallpaperChanged mis en file atteint le backend à la reconnexion et recalcule la palette et les couleurs de
        # la console.
        def apply():
            ok = display <= 0 and bool(ctypes.windll.user32.SystemParametersInfoW(0x14, 0, path, 3))
            if ok:
                if self.client is not None:
                    self.client.commands.put({'command': 'wallpaperChanged', 'args': {}})
                    kernel32.SetEvent(self.client.wakeup)
            else:
                self._notice = (
                    'Le fond d’écran n’a pas pu être appliqué.'
                    if display <= 0
                    else 'Fond d’écran par écran indisponible pour le moment.'
                )
                self.noticeChanged.emit()

        threading.Thread(target=apply, daemon=True).start()
