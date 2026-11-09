"""Prototypes ctypes user32/kernel32/gdi32 et structures Win32 partagés par le frontend."""
from __future__ import annotations
import ctypes
import ctypes.wintypes

user32 = ctypes.windll.user32
user32.GetForegroundWindow.restype = ctypes.c_void_p
user32.GetWindowLongPtrW.restype = ctypes.c_ssize_t
user32.GetWindowLongPtrW.argtypes = [ctypes.c_void_p, ctypes.c_int]
user32.SetWindowLongPtrW.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_ssize_t]
user32.GetWindowThreadProcessId.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong)]
import ctypes.wintypes

user32.WindowFromPoint.argtypes = [ctypes.wintypes.POINT]
user32.WindowFromPoint.restype = ctypes.c_void_p
user32.GetWindowRect.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
user32.GetClientRect.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
user32.ClientToScreen.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
user32.GetDC.argtypes = [ctypes.c_void_p]
user32.GetDC.restype = ctypes.c_void_p
user32.ReleaseDC.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
ctypes.windll.gdi32.GetPixel.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
ctypes.windll.gdi32.GetPixel.restype = ctypes.c_ulong
user32.IsZoomed.argtypes = [ctypes.c_void_p]
user32.IsWindow.argtypes = [ctypes.c_void_p]
user32.IsIconic.argtypes = [ctypes.c_void_p]
user32.IsWindowVisible.argtypes = [ctypes.c_void_p]
user32.MonitorFromWindow.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
user32.MonitorFromWindow.restype = ctypes.c_void_p
user32.GetMonitorInfoW.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
user32.MonitorFromPoint.argtypes = [ctypes.wintypes.POINT, ctypes.c_ulong]
user32.MonitorFromPoint.restype = ctypes.c_void_p


class MonitorInfo(ctypes.Structure):
    _fields_ = [
        ('size', ctypes.c_ulong),
        ('monitor', ctypes.c_long * 4),
        ('work', ctypes.c_long * 4),
        ('flags', ctypes.c_ulong),
        ('name', ctypes.c_wchar * 32),
    ]


kernel32 = ctypes.windll.kernel32
kernel32.CreateEventW.argtypes = [ctypes.c_void_p, ctypes.c_bool, ctypes.c_bool, ctypes.c_wchar_p]
kernel32.CreateEventW.restype = ctypes.c_void_p
kernel32.SetEvent.argtypes = [ctypes.c_void_p]
kernel32.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
kernel32.CloseHandle.argtypes = [ctypes.c_void_p]
kernel32.OpenProcess.argtypes = [ctypes.c_ulong, ctypes.c_bool, ctypes.c_ulong]
kernel32.OpenProcess.restype = ctypes.c_void_p
