"""Verre DWM natif d'une carte Winjet : même DLL (QtGlassBackdrop) que le shell, mais une silhouette par fenêtre.

Sans DLL (poste de dev non construit), `apply` ne fait rien et la carte garde son voile opaque.
"""
from __future__ import annotations

import ctypes

from native_glass import SurfaceCommand, default_library
from surface_geometry import path_commands


class CardGlass:
    def __init__(self, library=None):
        self.dll = None
        self.records = {}  # hwnd -> (clé géométrie, opacité)
        try:
            dll = ctypes.WinDLL(str(library or default_library()))
            signatures = {
                'BackdropAttach': [ctypes.c_void_p],
                'BackdropUpdate': [ctypes.c_void_p, ctypes.c_double, ctypes.c_int, ctypes.c_int],
                'BackdropSurface': [ctypes.c_void_p, ctypes.POINTER(SurfaceCommand), ctypes.c_uint32],
                'BackdropMaterial': [ctypes.c_void_p, ctypes.c_float],
                'BackdropDetach': [ctypes.c_void_p],
            }
            for name, args in signatures.items():
                getattr(dll, name).argtypes = args
            self.dll = dll
        except (OSError, AttributeError):
            self.dll = None

    def apply(self, hwnd, path, scale, opacity):
        if not self.dll or path.isEmpty():
            return
        record = self.records.get(hwnd)
        if record is None:
            if self.dll.BackdropAttach(hwnd) < 0:
                return
            record = self.records[hwnd] = [None, None, False]
        if record[1] != opacity:
            record[2] = False
            if self.dll.BackdropMaterial(hwnd, opacity) < 0:
                return
            record[1] = opacity
        bounds = path.boundingRect()
        key = (round(bounds.width(), 2), round(bounds.height(), 2), scale)
        if record[0] != key:
            commands = path_commands(path, scale)
            array = (SurfaceCommand * len(commands))()
            for index, (kind, *points) in enumerate(commands):
                array[index].kind = kind
                array[index].points[:] = points
            if self.dll.BackdropSurface(hwnd, array, len(commands)) < 0:
                self.dll.BackdropUpdate(hwnd, 0, False, True)
                return
            record[0] = key
            record[2] = False
        if record[2]:
            return  # rien n'a changé : aucun appel DWM au repos
        self.dll.BackdropUpdate(hwnd, 0, True, True)
        record[2] = True

    def detach(self, hwnd):
        if self.dll and self.records.pop(hwnd, None) is not None:
            self.dll.BackdropDetach(hwnd)

    def close(self):
        for hwnd in list(self.records):
            self.detach(hwnd)
