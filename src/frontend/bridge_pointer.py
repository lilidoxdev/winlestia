"""Sondage du pointeur : bords d’écran, plein écran, mode silencieux et glisser de fichiers."""
from __future__ import annotations
import ctypes
import ctypes.wintypes
import os
import time
from win_native import MonitorInfo, user32
from PySide6.QtWidgets import QApplication
from PySide6.QtGui import QCursor, QRegion
from PySide6.QtCore import QRect
from interaction import is_fullscreen


class PointerMixin:
    """Sondage du pointeur : bords d’écran, plein écran, mode silencieux et glisser de fichiers."""

    def poll_file_drag(self):
        down = bool(user32.GetAsyncKeyState(1) & 0x8000)
        point = QCursor.pos()
        now = time.monotonic()
        if down and not getattr(self, '_file_drag_down', False):
            self._file_drag_origin = point
            self._file_drag_start = now
            self._file_drag_foreign = False
            try:
                cursor = ctypes.wintypes.POINT()
                if user32.GetCursorPos(ctypes.byref(cursor)):
                    owner = ctypes.c_ulong(0)
                    user32.GetWindowThreadProcessId(user32.WindowFromPoint(cursor), ctypes.byref(owner))
                    self._file_drag_foreign = owner.value != os.getpid()
            except Exception:
                # Position du curseur illisible pendant un glisser : on garde l'état précédent.
                pass
        active = (
            down
            and getattr(self, '_file_drag_foreign', False)
            and now - getattr(self, '_file_drag_start', now) > 0.25
            and (point - getattr(self, '_file_drag_origin', point)).manhattanLength() > 12
        )
        self._file_drag_down = down
        screen = QApplication.screenAt(point)
        near = bool(active and screen and point.y() <= screen.geometry().top() + 150)
        changed = active != getattr(self, '_file_drag_active', False) or near != getattr(self, '_file_drag_near', False)
        self._file_drag_active = active
        self._file_drag_near = near
        if changed:
            self.fileDragChanged.emit()

    def dock_quiet(self, screen):
        # Dock et chrome de bord sont coupés uniquement sur un écran en plein écran (par écran), ou partout quand le
        # mode silencieux est forcé.
        return self.quiet_on and (self.quiet_forced == 'on' or screen.name() in self.fullscreen_names)

    def quiet_slow(self):
        # Les minuteurs ne passent à leur rythme le plus lent que si tous les écrans sont silencieux ; le raccourci du
        # shell et la détection de sortie sont pilotés par événements dans le backend.
        return self.quiet_on and (
            self.quiet_forced == 'on' or all(s.name() in self.fullscreen_names for s in QApplication.screens())
        )

    def quiet_apply(self):
        slow = self.quiet_slow()
        if slow != self.client.quiet:
            self.client.quiet = slow
            self.pointer_pace()
        self.setAudioBars(self._bars_want)

    def fullscreen(self, screen=None):
        screen = screen or self._screen
        hwnd = user32.GetForegroundWindow()
        pid = ctypes.c_ulong()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        remembered = getattr(self, '_full_candidates', {})
        if pid.value not in (os.getpid(), self.args.backend_pid) and hwnd:
            info = MonitorInfo()
            info.size = ctypes.sizeof(info)
            if user32.GetMonitorInfoW(user32.MonitorFromWindow(hwnd, 2), ctypes.byref(info)):
                remembered[info.name] = hwnd
                self._full_candidates = remembered
        hwnd = remembered.get(screen.name(), 0)
        if not hwnd or not user32.IsWindow(hwnd) or user32.IsIconic(hwnd) or not user32.IsWindowVisible(hwnd):
            return False
        info = MonitorInfo()
        info.size = ctypes.sizeof(info)
        if (
            not user32.GetMonitorInfoW(user32.MonitorFromWindow(hwnd, 2), ctypes.byref(info))
            or info.name != screen.name()
        ):
            return False
        name = ctypes.create_unicode_buffer(128)
        user32.GetClassNameW(hwnd, name, 128)
        if name.value in ('Progman', 'WorkerW', 'Shell_TrayWnd'):
            return False
        rect = (ctypes.c_long * 4)()
        origin = (ctypes.c_long * 2)(0, 0)
        if not user32.GetClientRect(hwnd, ctypes.byref(rect)) or not user32.ClientToScreen(hwnd, ctypes.byref(origin)):
            return False
        return is_fullscreen(
            user32.GetWindowLongPtrW(hwnd, -16),
            bool(user32.IsZoomed(hwnd)),
            (origin[0], origin[1], origin[0] + rect[2], origin[1] + rect[3]),
            tuple(info.monitor),
        )

    def edge_zone(self, rail, edge, side='right'):
        # Zone de survol = bosse affichée (contenu + tiroir + chevron, alignée sur son bord d'écran dans la fenêtre de
        # taille fixe), +-16 px en vertical, jusqu'au bord de l'écran.
        rg = rail.geometry()
        bw = float(rail.property('bumpWidth') or rg.width())
        bh = float(rail.property('bumpHeight') or rg.height())
        offset = float(rail.property('offset') or 0)
        by = rg.y() + (rg.height() - bh) / 2
        if side == 'left':
            bx = rg.x() - offset + bw
            return QRect(int(edge), int(by) - 16, int(bx - edge) + 17, int(bh) + 32)
        bx = rg.x() + rg.width() - bw + offset
        return QRect(int(bx) - 16, int(by) - 16, int(edge - bx) + 17, int(bh) + 32)

    def social_zone(self, screen_right):
        return self.edge_zone(self.social_win, screen_right, 'right')

    def edge_rail(self, rail, side, point, geometry, full, now):
        # Logique commune de déclenchement et de masquage des rails de bord cachés : apparition au bord de l'écran,
        # masquage 0,5 s après que le pointeur a quitté la bosse affichée.
        key = 'social' if side == 'right' else 'bookmarks'
        attr = 'away_' + key
        event = (key + 'Peek', key + 'Hide')
        margin = int(
            self._state.get("settings", {}).get(
                "rightActivationMargin" if side == 'right' else "leftActivationMargin", 2
            )
        )
        rail_h = float(rail.property('bumpHeight') or rail.height() or 300)
        at_edge = point.x() >= geometry.right() - margin if side == 'right' else point.x() <= geometry.left() + margin
        # Un écran voisin de ce côté fait du bord partagé un simple passage, pas un déclencheur.
        edge_x = geometry.right() if side == 'right' else geometry.left()
        adjacent = any(
            o.geometry() != geometry
            and (o.geometry().left() <= edge_x + 1 if side == 'right' else o.geometry().right() >= edge_x - 1)
            and (o.geometry().left() >= edge_x - 1 if side == 'right' else o.geometry().right() <= edge_x + 1)
            and o.geometry().top() < geometry.bottom()
            and o.geometry().bottom() > geometry.top()
            for o in QApplication.screens()
        )
        trigger = at_edge and not adjacent and abs(point.y() - geometry.center().y()) < rail_h / 2 + 60
        inside = rail.isVisible() and self.edge_zone(
            rail, geometry.right() if side == 'right' else geometry.left(), side
        ).contains(point)
        if trigger and not full:
            self.shellEvent.emit(event[0])
            setattr(self, attr, 0)
        elif inside or not rail.property('shown'):
            setattr(self, attr, 0)
        else:
            if not getattr(self, attr):
                setattr(self, attr, now)
            if now - getattr(self, attr) > 0.5 and not self.args.preview:
                self.shellEvent.emit(event[1])
                setattr(self, attr, 0)

    def poll_pointer(self):
        try:
            self._poll_pointer()
        finally:
            self.pointer_pace()

    def _poll_pointer(self):
        self.poll_file_drag()
        if not self.top:
            return
        if self.modal:
            return
        if self.args.backend_pid and time.monotonic() > self.health_at:
            self.health_at = time.monotonic() + 2
            handle = ctypes.windll.kernel32.OpenProcess(0x1000, False, self.args.backend_pid)
            if not handle:
                QApplication.quit()
                return
            ctypes.windll.kernel32.CloseHandle(handle)
        point = QCursor.pos()
        screen = QApplication.screenAt(point) or QApplication.primaryScreen()
        if screen != self._screen:
            self._screen = screen
            self.geometryChanged.emit()
            self.frameChanged.emit()
            self.changed.emit()
        now = time.monotonic()
        geometry = screen.geometry()
        if self.quiet_slow() and not (
            self.top and self.top.isVisible()
        ):  # silencieux partout et aucun panneau ouvert : pas de suivi de bord, pas d'apparition, pas de rail
            self.edge_policy.away_since = 0
            self.away_top = self.away_social = self.away_bookmarks = 0
            return
        backend_full = getattr(self, '_backend_fullscreen', False)
        full = screen.name() in self.fullscreen_names if backend_full else self.fullscreen()
        fullscreen_names = (
            self.fullscreen_names
            if backend_full
            else tuple(s.name() for s in QApplication.screens() if self.fullscreen(s))
        )
        if fullscreen_names != self.fullscreen_names:
            self.fullscreen_names = fullscreen_names
            self.frameChanged.emit()
        dock_full = (
            self.dock_quiet(screen) if backend_full else full
        )  # préférence désactivée : le dock garde son comportement de bord normal en plein écran
        full = full or (self.quiet_on and self.quiet_forced == 'on')
        panel_width = float(self.top.property('panelWidth') or self.top.width())
        panel_height = float(self.top.property('panelHeight') or self.top.height())
        panel_rect = QRect(
            round(self.top.x() + (self.top.width() - panel_width) / 2),
            self.top.y(),
            round(panel_width),
            round(panel_height),
        )
        top_inside = self.top.isVisible() and panel_rect.adjusted(-12, -2, 12, 16).contains(point)
        top_edge = (
            geometry.top()
            <= point.y()
            <= geometry.top() + int(self._state.get("settings", {}).get("topActivationMargin", 2))
            and abs(point.x() - geometry.center().x()) < 460
        )
        if self.fileDragNearTop:
            if self.top.isVisible():
                self.navigate('close')
            self.away_top = 0
        elif top_edge and not full:
            self.shellEvent.emit('peek')
            self.away_top = 0
        elif top_inside:
            self.away_top = 0
            self.top_seen = True
        elif self.top.isVisible() and panel_height < 95 and now > self.opening_until:
            if not self.away_top:
                self.away_top = now
            if now - self.away_top > 0.55 and not self.args.preview:
                self.navigate('close')
        if self.social_win and self.socialRailVisible:
            self.edge_rail(self.social_win, 'right', point, geometry, full, now)
        else:
            self.away_social = 0
        if self.bookmarks_win and self.bookmarksRailVisible:
            self.edge_rail(self.bookmarks_win, 'left', point, geometry, full, now)
        else:
            self.away_bookmarks = 0
        if self.dock and not self.dockVisible:
            self.edge_policy.away_since = 0
            self.client.subscription['dock'] = False  # dock désactivé : pas de suivi du bord inférieur, rien d'abonné
        elif self.dock and self.dockVisible and dock_full:
            self.edge_policy.away_since = (
                # plein écran : le chrome reste masqué, ni révélation ni minuteur ; l'état de repos reprend à la
                # sortie
                0
            )
        elif self.dock and self.dockVisible:
            self.client.subscription['dock'] = bool(self.dock.property('shown'))
            dock_inside = self.dock.isVisible() and self.dock.geometry().adjusted(-8, -10, 8, 4).contains(point)
            menu_open = bool(self.dock.property('menuOpen'))
            if menu_open:
                dock_inside = True
            bottom = point.y() >= geometry.bottom() - 7
            decision = self.edge_policy.update(now, bottom, dock_inside, dock_full, now < self.force_dock)
            if decision is True:
                self.shellEvent.emit('dock')
            elif decision is False and not self.args.preview:
                self.shellEvent.emit('dockHide')

    def apply_surface_visibility(self, *args):
        # Une surface désactivée dans les réglages est vraiment masquée (pas seulement transparente) et son masque de
        # saisie en cache est supprimé, pour qu'une réactivation le réapplique.
        if not self._state.get('settings'):
            return  # pas encore d'instantané : ne jamais masquer une surface sur les valeurs par défaut
        flags = (self.dockVisible, self.socialRailVisible, self.bookmarksRailVisible)
        if flags == getattr(self, '_surface_flags', None):
            return
        self._surface_flags = flags
        for win, on in zip((self.dock, self.social_win, self.bookmarks_win), flags):
            if win is None or on:
                continue
            if win.isVisible():
                win.setVisible(False)
            win.setMask(QRegion())
            if self.surface_scene:
                self.surface_scene.input_masks.pop(int(win.winId()), None)

    def pointer_pace(self):
        if self.quiet_slow() and not (self.top and self.top.isVisible()):
            if self.pointer.interval() != 500:
                self.pointer.setInterval(500)
            return
        point = QCursor.pos()
        geometry = (QApplication.screenAt(point) or QApplication.primaryScreen()).geometry()
        now = time.monotonic()
        # Rythme adaptatif : 90 ms pendant une révélation, bouton enfoncé (glisser de fichier) ou pointeur à moins de
        # 48 px d'un bord d'écran ; 250 ms sinon.
        # Seuls comptent les bords qui portent une surface visible : le déclencheur du panneau haut toujours, le dock
        # du bas et les rails latéraux seulement s'ils sont activés.
        edges = [point.y() - geometry.top()]
        if self.dockVisible:
            edges.append(geometry.bottom() - point.y())
        if self.bookmarksRailVisible:
            edges.append(point.x() - geometry.left())
        if self.socialRailVisible:
            edges.append(geometry.right() - point.x())
        near = min(edges) <= 48
        active = (
            near
            or self.away_top
            or self.away_social
            or self.away_bookmarks
            or now < self.force_dock
            or getattr(self, '_file_drag_down', False)
            or bool(user32.GetAsyncKeyState(1) & 0x8000)
        )
        if self.top and self.top.isVisible() and float(self.top.property('panelHeight') or 0) >= 95:
            active = True  # panneau ouvert ; la barre repliée est couverte par away_top et la proximité du bord
        for win, on in (
            (self.dock, self.dockVisible),
            (self.social_win, self.socialRailVisible),
            (self.bookmarks_win, self.bookmarksRailVisible),
        ):
            if win and on and (win.property('shown') or win.property('menuOpen')):
                active = True
        interval = 90 if active else 250
        if self.pointer.interval() != interval:
            self.pointer.setInterval(interval)
