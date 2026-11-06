"""Point d'entrée applicatif : arguments, QApplication, chargement du QML et modes de vérification."""
from __future__ import annotations
import argparse, ctypes, json, os, sys, threading, time
from PySide6.QtCore import QMetaObject, QObject, QTimer, QUrl, Qt, qInstallMessageHandler
from PySide6.QtGui import QFontDatabase
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlApplicationEngine
from localization import Translator
from native_glass import NativeGlass
from surface_geometry import SurfaceScene
from win_native import user32
from app_state import ROOT, CONFIG, QML_ISSUES
import app_state
from bridge import Bridge


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--pipe', default='PanneauxCaelestia')
    parser.add_argument('--backend-pid', type=int, default=0)
    parser.add_argument('--preview', action='store_true')
    parser.add_argument('--verify', action='store_true')
    parser.add_argument('--verify-tools82', action='store_true')
    parser.add_argument('--page', type=int, default=0)
    parser.add_argument('--capture')
    parser.add_argument('--exit-after', type=int, default=0)
    args = parser.parse_args()
    if args.verify:
        args.preview = True
    os.environ.setdefault('QSG_RHI_BACKEND', 'd3d11')
    app = QApplication(sys.argv)
    app_state.TRANSLATOR = Translator(CONFIG)
    app.installTranslator(app_state.TRANSLATOR)
    app.setApplicationName('Panneaux · Caelestia Windows')
    app.setQuitOnLastWindowClosed(False)
    qInstallMessageHandler(app_state.message_handler)
    if args.backend_pid:

        def watch_backend():
            k32 = ctypes.WinDLL('kernel32', use_last_error=True)
            k32.OpenProcess.argtypes = [ctypes.c_ulong, ctypes.c_bool, ctypes.c_ulong]
            k32.OpenProcess.restype = ctypes.c_void_p
            k32.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
            k32.WaitForSingleObject.restype = ctypes.c_ulong
            handle = k32.OpenProcess(0x00100000, False, args.backend_pid)
            if handle:
                k32.WaitForSingleObject(handle, 0xFFFFFFFF)
            QMetaObject.invokeMethod(app, 'quit', Qt.QueuedConnection)
            time.sleep(3)
            os._exit(0)

        threading.Thread(target=watch_backend, name='backend-watch', daemon=True).start()
    for font in (ROOT / 'assets/fonts').glob('*.ttf'):
        QFontDatabase.addApplicationFont(str(font))
    bridge = Bridge(args)
    engine = QQmlApplicationEngine()
    bridge.engine = engine
    engine.addImportPath(str(ROOT / 'qml'))
    engine.addImageProvider('capture', bridge.capture.provider)
    engine.rootContext().setContextProperty('Bridge', bridge)
    bridge.changed.connect(bridge.theme_inputs_changed)
    QTimer.singleShot(0, bridge.theme_inputs_changed)
    engine.load(QUrl.fromLocalFile(str(ROOT / 'qml/Main.qml')))
    if not engine.rootObjects():
        bridge.stop()
        return 1
    root = engine.rootObjects()[0]
    bridge.top = root
    bridge._window = root
    bridge.dock = root.findChild(QObject, 'bottomDock')
    bridge.social_win = root.findChild(QObject, 'socialRail')
    bridge.bookmarks_win = root.findChild(QObject, 'bookmarksRail')
    bridge.dictation_win = root.findChild(QObject, 'dictationOverlay')
    bridge.windowChanged.emit()
    bridge.surface_scene = SurfaceScene(bridge)
    bridge.changed.connect(bridge.apply_surface_visibility)
    bridge.apply_surface_visibility()
    bridge.glass = NativeGlass(bridge)
    if os.environ.get('PANNEAUX_VERIFY_THICKNESS85') == '1':
        from validation85 import run

        run(bridge, root, app, QML_ISSUES, ROOT / 'validation-8.5')
    if os.environ.get('PANNEAUX_VERIFY_MATERIAL84') == '1':
        from validation84 import run

        run(bridge, root, app, QML_ISSUES, ROOT / 'validation-8.4')
    if os.environ.get('PANNEAUX_PROOF91') == '1':

        def proof91():
            native = bridge.glass
            rectangles = []
            for handle in native.windows:
                for index in range(256):
                    part = native.dll.BackdropPart(handle, index)
                    if not part:
                        break
                    rect = (ctypes.c_long * 4)()
                    user32.GetWindowRect(part, ctypes.byref(rect))
                    rectangles.append(list(rect))
            report = {
                'mode': bridge.themeMode,
                'opacity': bridge.glassOpacity,
                'tintOpacity': bridge.glassTintOpacity,
                'frameThickness': bridge.frameThickness,
                'surfaceRevision': bridge.surface_scene.revision,
                'nativeErrors': native.errors,
                'qmlIssues': QML_ISSUES,
                'helpers': rectangles,
                'wallpaper': bridge.current_theme().get('source', ''),
                'favorites': len(bridge.favorites),
                'dockEnabled': bridge.taskbarEnabled,
            }
            report['allPassed'] = (
                not native.errors
                and not QML_ISSUES
                and bool(rectangles)
                and all(r[2] - r[0] <= 592 and r[3] - r[1] <= 592 for r in rectangles)
            )
            (ROOT / 'validation-9.1/live-startup-report.json').write_text(
                json.dumps(report, indent=2), encoding='utf-8'
            )

        QTimer.singleShot(6500, proof91)
    if args.preview:
        QTimer.singleShot(1200, lambda: bridge.openTab(args.page))
    if args.capture:

        def capture():
            root.grabWindow().save(args.capture)

        QTimer.singleShot(3500, capture)
    if args.exit_after:
        QTimer.singleShot(args.exit_after, app.quit)
    if args.verify_tools82:
        from validation82 import run

        run(bridge, root, app, QML_ISSUES, ROOT / 'validation-8.3')
    if args.verify:
        report = {'started': time.time(), 'views': [], 'frames': []}
        proof = ROOT / 'validation-8.1'
        proof.mkdir(exist_ok=True)
        sequence = [
            ('dashboard', None),
            ('media', None),
            ('performance', None),
            ('access', None),
            ('workspace:apps', None),
            ('workspace:games', None),
            ('workspace:projects', None),
            ('workspace:documents', None),
            ('palette', ''),
            ('palette', 'chrome'),
            ('palette', '24*7'),
            ('palette', '> volume'),
            ('controls', None),
            ('settings', None),
            ('clipboard', None),
        ]

        def check(mode, query, index):
            bridge.navigate(mode)
            if query is not None:
                QTimer.singleShot(250, lambda: bridge.prefillPalette(query))

            def save():
                root.grabWindow().save(str(proof / f'{index:02}-{mode.replace(":", "-")}.png'))
                report['views'].append(
                    {
                        'mode': mode,
                        'actualMode': root.property('pageMode'),
                        'visible': root.isVisible(),
                        'width': root.width(),
                        'height': root.height(),
                        'favorites': len(bridge._state.get('favorites', [])),
                        'results': [{k: v for k, v in r.items() if k in ('name', 'kind')} for r in bridge._results],
                        'performance': bridge._state.get('performance', {}),
                        'diagnostics': bridge._state.get('diagnostics', {}),
                    }
                )

            QTimer.singleShot(2200, save)

        for i, (mode, query) in enumerate(sequence):
            QTimer.singleShot(2000 + i * 3000, lambda m=mode, q=query, i=i: check(m, q, i))
        QTimer.singleShot(43300, lambda: bridge.shellEvent.emit('verifyRecorder'))

        def recorder_proof():
            root.grabWindow().save(str(proof / 'shortcut-recorder.png'))
            report['shortcutRecorder'] = bridge._state.get('diagnostics', {})

        QTimer.singleShot(43800, recorder_proof)
        QTimer.singleShot(43900, lambda: bridge.shellEvent.emit('verifyRecorderClose'))
        QTimer.singleShot(47500, bridge.closePanel)

        def idle_start():
            report['idleStart'] = {
                'qtCpu': time.process_time(),
                'time': time.perf_counter(),
                'backend': bridge._state.get('diagnostics', {}),
            }

        QTimer.singleShot(49500, idle_start)
        root.frameSwapped.connect(lambda: report['frames'].append(time.perf_counter()))
        QTimer.singleShot(46000, lambda: bridge.dock.grabWindow().save(str(proof / 'dock.png')))

        def finish():
            report['idleEnd'] = {
                'qtCpu': time.process_time(),
                'time': time.perf_counter(),
                'backend': bridge._state.get('diagnostics', {}),
            }
            report['closed'] = {'visible': root.isVisible(), 'performance': bridge._state.get('performance', {})}
            report['qmlIssues'] = list(dict.fromkeys(QML_ISSUES))
            (proof / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
            app.quit()

        QTimer.singleShot(55500, finish)

    def shutting_down():
        app_state.mark_shutting_down()
        bridge.stop()

    app.aboutToQuit.connect(shutting_down)
    return app.exec()
