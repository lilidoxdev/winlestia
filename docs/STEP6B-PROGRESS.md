# STEP 6B progress

A icon: tools/Make-FilesIcon.ps1 -> src/files/Assets/winlestia-files.ico (16..256), ApplicationIcon set. Dock icon cache key = SHA256(entry.Id+IconPath+Target), new entry => no stale icon.
B pin: FilesDefaultHandler.Migrate adds dock entry "dock:<Glass-v5>\Winlestia.Files.exe" ("Winlestia Fichiers") to TaskbarPins once (FilesDefaultsMigrated).
C defaults: Models.cs FilesDefaultHandler/FilesPreload default true + FilesDefaultsMigrated; FilesHandlerGuard.DecideDefaults (pure); migration runs after crash-guard Check, off UI thread via Set(true).
D theme: Files-only tokens (UI.Panel/Recess/Tint/Accent in BackendShim), primary-tinted hover, PrimaryContainer for active sidebar/tab, accent crumb/nav/tab icons.
   Root cause: Files read the wallpaper via SPI, but the frontend palette comes from a Desktop Hut live wallpaper override. frontend.py now writes Glass-v5/palette-shell.json (auto mode); Files reads it (FileSystemWatcher, debounced) and falls back to its own computation.
Tests: FilesHandler 64/64 (incl. 4 defaults_*), FilesRouter 27/27, FilesPreloadPolicy 12/12, Explorer077 no false, harness palette 0 fails.
Needs redeploy (frontend.py + backend + Files). Test envs: WINLESTIA_FILES_PALETTE_FILE, WINLESTIA_FILES_PALETTE_SHELL; harness modes themeshot.
