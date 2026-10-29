# Step 6 (folder handler, guard, toggles, live palette) - DONE, not deployed
- Owner: src/backend/FilesHandler.cs + FilesHandlerGuard.cs (pure .NET), linked into src/files, backend and tests/FilesHandler. CLI lives in Winlestia.Files.exe (`--register "<exe>"`, `--restore`; exit 0/1, log %LOCALAPPDATA%\Winlestia\Files\handler.log).
- Registry root is configurable (HandlerOptions). Test override: WINLESTIA_FILES_HANDLER_ROOT=Software\WinlestiaTest\<x> (refused elsewhere; no SHChangeNotify; own backup file, WINLESTIA_FILES_HANDLER_BACKUP).
- Backend: FilesDefaultHandler.cs (toggle, startup guard, FileSystemWatcher on handler-tripped.flag), Preferences.FilesDefaultHandler, bridge keys filesDefaultHandler / filesPreload (+ settings fields), FilesPreload.NotifyPalette (called by Theme.Use, 600 ms debounce, never launches).
- Files: startup crash counter (startup-crashes.txt; 3 in 60 s -> restore + handler-tripped.flag), "palette" pipe cmd -> PaletteLoader.Refresh, selftest `palette`.
- Tests: tests/FilesHandler (50/50), harness `-Mode handler` and `-Mode palette`.
- STEP 7 (installer): [UninstallRun] MUST call `{app}\Glass-v5\Winlestia.Files.exe --restore` before the files are removed.

## Review fixes (applied)
- Mutex `Local\WinlestiaFilesHandler` around Register/Restore (cross-process); Capture never records our own Default/verb as original; Register rolls back (Restore) on any failure after its first write; Restore resets (Default) first, then deletes the verb key, and keeps a foreign Default; a failed Restore keeps the backup and returns Ok=false.
- FilesDefaultHandler.Set returns (pref, notice): pref stays true when Restore fails; registry work under a lock; Store.State/Save only on the UI dispatcher (bridge continuation, Disable via Dispatcher).
- Crash guard counts only real crashes (FilesHandlerGuard.CountsAsCrash); UnhandledException in the first 15 s counts and hands the requested folder to explorer.exe.
- PathRouter.Clean: `C:"` -> `C:\`, `\srv\share"` -> `\srv\share\`; also used by LaunchExplorer.
- WINLESTIA_FILES_HANDLER_BACKUP honoured only under %TEMP%.
- installer/Winlestia.iss [UninstallRun] calls `Glass-v5\Winlestia.Files.exe --restore` (before the Taskbar rescue entry; skipifdoesntexist).
- Known limit: if Winlestia.Files.exe is deleted/moved while the backend is NOT running, nothing repairs the HKCU keys (folders would fail to open). The backend repairs it at its next start (exe missing -> restore + pref off); the uninstaller restores explicitly.
