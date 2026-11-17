"""Run the trusted PC Widgets renderer for its twelve curated Winjet IDs."""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

from PySide6.QtCore import QObject, QProcess, QTimer, Signal
from PySide6.QtGui import QGuiApplication


PREFIX = "io.github.walidmpcwidgets.original."
KINDS = frozenset({
    "weather", "stocks", "media", "clock", "calendar", "notes",
    "system", "workspace", "files", "disk", "outlook", "comms",
})


def kind_for(package_id):
    kind = package_id.removeprefix(PREFIX) if isinstance(package_id, str) else ""
    return kind if kind in KINDS and package_id == PREFIX + kind else ""


def runtime_path():
    root = os.environ.get("LOCALAPPDATA")
    return Path(root, "Winlestia", "PCWidgetsWinjets", "1.1.0") if root and sys.platform == "win32" else None


class PcWidgetsOriginal(QObject):
    active_changed = Signal(object)
    remove_requested = Signal(str)
    edit_requested = Signal(str)

    def __init__(self, parent=None, *, root=None):
        super().__init__(parent)
        self.root = root if root is not None else runtime_path()
        self.process = None
        self.desired = {}
        self.active = set()
        self.retries = 0
        self.restart = QTimer(self, singleShot=True, interval=2500)
        self.restart.timeout.connect(self._start)
        app = QGuiApplication.instance()
        if app:
            app.aboutToQuit.connect(self.close)

    def sync(self, cards):
        if cards == self.desired:
            # A runtime can be made available after the Winlestia session starts.
            self._start()
            return
        self.desired = cards
        self.retries = 0
        if self.process and self.process.state() == QProcess.Running:
            self._write()
        self._start()

    def _start(self):
        if self.process or not self.desired or self.root is None or self.retries >= 3:
            return
        python = self.root / ".venv" / "Scripts" / "python.exe"
        if not python.is_file() or not (self.root / "PC-Widgets-1.1.0" / "main.py").is_file():
            return
        self.retries += 1
        process = self.process = QProcess(self)
        process.setProcessChannelMode(QProcess.ForwardedErrorChannel)
        process.setWorkingDirectory(str(self.root))
        process.started.connect(self._write)
        process.readyReadStandardOutput.connect(self._read)
        process.finished.connect(lambda *_: self._ended(process))
        process.errorOccurred.connect(
            lambda error: self._ended(process) if error == QProcess.FailedToStart else None
        )
        host = Path(__file__).with_name("pc_widgets_original_host.py")
        process.start(str(python), ["-u", str(host), str(self.root)])

    def _write(self):
        if self.process:
            self.process.write(json.dumps(self.desired, ensure_ascii=True).encode("ascii") + b"\n")

    def _read(self):
        process = self.process
        while process and process.canReadLine():
            try:
                message = json.loads(bytes(process.readLine()))
            except (ValueError, UnicodeError):
                continue
            if not isinstance(message, dict):
                continue
            if isinstance(message.get("active"), list):
                self._set_active({key for key in message["active"] if isinstance(key, str) and key in self.desired})
            instance = message.get("remove")
            if isinstance(instance, str) and instance in self.desired:
                self.remove_requested.emit(instance)
            instance = message.get("edit")
            if isinstance(instance, str) and instance in self.active and self.desired.get(instance, {}).get("visible"):
                self.edit_requested.emit(instance)

    def _set_active(self, value):
        changed = self.active ^ value
        self.active = value
        if changed:
            self.active_changed.emit(changed)

    def _ended(self, process):
        if self.process is not process:
            return
        self.process = None
        self._set_active(set())
        process.deleteLater()
        if self.desired and self.retries < 3:
            self.restart.start()

    def close(self):
        self.restart.stop()
        self.desired = {}
        process = self.process
        if process:
            process.closeWriteChannel()
            if not process.waitForFinished(800):
                process.kill()
                process.waitForFinished(800)
