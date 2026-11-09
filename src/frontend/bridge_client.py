"""Client du canal nommé vers le backend C# (protocole JSON lignes). Seul module qui connaît le protocole."""
from __future__ import annotations
import io, json, os, queue, threading, time
from PySide6.QtCore import Signal, QThread
from win_native import kernel32


# Version du protocole (docs/BRIDGE.md), annoncée dans args.protocol de chaque requête ; le serveur journalise une divergence.
PROTOCOL_VERSION = 1


class PipeClient(QThread):
    received = Signal(dict)

    def __init__(self, name):
        super().__init__()
        self.path = '\\\\.\\pipe\\' + name
        self.commands = queue.Queue()
        self.subscription = {}
        self.stopping = False
        self.handle = None
        self.updates = {}
        self.update_lock = threading.Lock()
        self.last_state = 0
        self.bars_seq = 0
        self.bars_idle = True
        self.quiet = False
        self.wakeup = kernel32.CreateEventW(None, False, False, 'Local\\PanneauxCaelestiaWake')

    def run(self):
        while not self.stopping:
            try:
                with open(self.path, 'r+b', buffering=0) as connection:
                    self.handle = connection
                    reader = io.BufferedReader(connection, 65536)
                    while not self.stopping:
                        try:
                            request = self.commands.get_nowait()
                        except queue.Empty:
                            with self.update_lock:
                                request = self.updates.pop(next(iter(self.updates)), None) if self.updates else None
                            if request is None:
                                visual = (
                                    self.subscription.get('visible')
                                    and self.subscription.get('page') == 1
                                    and time.monotonic() - self.last_state < 0.6
                                )
                                bars = (
                                    self.subscription.get('bars')
                                    and self.subscription.get('visible')
                                    and self.subscription.get('page') == 0
                                    and time.monotonic() - self.last_state < 0.6
                                )
                                request = {
                                    'command': 'visualizer' if visual else 'audioBars' if bars else 'state',
                                    'args': {**self.subscription, 'pid': os.getpid(), 'seq': self.bars_seq},
                                }
                        if request['command'] == 'state':
                            self.last_state = time.monotonic()
                        request = {
                            'command': request['command'],
                            'args': {
                                **self.subscription,
                                **request['args'],
                                'pid': os.getpid(),
                                'epoch': self.subscription.get('epoch', 0),
                                'protocol': PROTOCOL_VERSION,
                            },
                        }
                        payload = (json.dumps(request, ensure_ascii=False) + '\n').encode('utf-8')
                        connection.write(payload)
                        data = reader.readline(8 * 1024 * 1024)
                        if not data:
                            break
                        self.received.emit(json.loads(data))
                        if request['command'] in ('state', 'visualizer', 'audioBars'):
                            kernel32.WaitForSingleObject(
                                self.wakeup,
                                55
                                if self.subscription.get('visible') and self.subscription.get('page') == 1
                                else 33
                                if request['command'] == 'audioBars' and not self.bars_idle
                                else 180
                                if self.subscription.get('visible')
                                else 1000
                                if self.quiet
                                else 500,
                            )
            except (OSError, ValueError, json.JSONDecodeError):
                self.msleep(300)
            finally:
                self.handle = None

    def stop(self):
        self.stopping = True
        kernel32.SetEvent(self.wakeup)
        if self.handle:
            try:
                self.handle.close()
            except OSError:
                # Nettoyage au mieux : le handle peut être déjà fermé.
                pass
