# Windows integrations

What Winlestia touches on the system, and where. Paths are under `src/backend` unless noted. To undo the
taskbar change see `docs/RESTORE.md`.

| Feature | What it does | Files |
|---|---|---|
| Taskbar hide / restore | Snapshots the `Shell_TrayWnd` / `Shell_SecondaryTrayWnd` state (`SHAppBarMessage`), sets auto-hide, hides the windows, watches with `SetWinEventHook`. Restores the saved state on exit, crash (watchdog) or `--restore-taskbar`. | `ShellTaskbar.cs`, `TaskbarNative.cs`, `TaskbarRescue.cpp` (-> `RETABLIR_BARRE_WINDOWS.exe`), `Coordinator.cs` |
| Windows key / dock reveal | Low-level handling of the Windows key to reveal the dock. | `ShellTaskbar.cs` (`WindowsDockKey`) |
| Autostart | Per-user scheduled task `WinlestiaShell` (logon trigger, no admin) created from an XML file through `schtasks.exe /create /xml`; the XML is built by `AutostartPolicy.TaskXml`. | `AutostartTask.cs`, `AutostartPolicy.cs`, `tests/AutostartPolicy` |
| Audio sessions | Per-app volume/mute mixer and process audio (`ProcessAudio.dll` built from `ProcessAudio.cpp`). | `AudioMixer.cs`, `AudioMixerHost.cs`, `AudioTools.cs`, `ProcessAudio.cpp` |
| Audio visualiser | WASAPI loopback capture (NAudio `WasapiLoopbackCapture`). | `AudioBars.cs`, `AudioBarsHost.cs` |
| Media sessions | Windows media transport controls (GSMTC `GlobalSystemMediaTransportControls*`) for the now-playing card and transport buttons. | `MediaController.cs`, `MediaSessionMatch.cs`, `MediaSourceIdentity.cs`, `MediaTimeline.cs` |
| Windows Search | Queries the indexer through OLE DB `Provider=Search.CollatorDSO` (`SystemIndex`). | `FileSearch.cs` |
| Bookmarks | Reads Chromium `Bookmarks` JSON and Firefox `places.sqlite` (Microsoft.Data.Sqlite) on a working copy; the browser file is never opened in SQLite. | `BookmarksCore.cs`, `BookmarksService.cs`, `BookmarkUri.cs` |
| Global hotkeys | `RegisterHotKey` bindings: launcher, palette, pin, capture, wallpaper, and the rescue hotkey Ctrl+Alt+Backspace. | `GlobalShortcut.cs`, `Native.cs` |
| Glass backdrop | DWM blur/material from `GlassBackdrop.dll` (`GlassBackdrop.cpp`, `build-backdrop.cmd`). | `GlassLevel.cs`, `Native.cs` |
| Desktop / wallpaper | Desktop icon guard, wallpaper set and probe, virtual desktops. | `DesktopIconsGuard.cs`, `DesktopReassertPolicy.cs`, `Secours.cpp` (`SECOURS_BUREAU.exe`) |
| Files default handler | Optional Explorer association for folders. | `FilesDefaultHandler.cs`, `FilesHandler.cs`, `FilesHandlerGuard.cs` |
| Telemetry | CPU/GPU/PC readings (LibreHardwareMonitor, NVIDIA). | `PcTelemetry.cs`, `NvidiaTelemetry.cs`, `CpuTempChain.cs` |
| Voice dictation | Local Whisper.net model. | `DictationService.cs` |
| IPC | Named pipes: `PanneauxCaelestia` (Qt frontend), `WinlestiaCli`, Files pipe. All restricted to the current user. | `QtShellBridge.cs`, `CliServer.cs`, `FilesPipe.cs` |

Everything stored on disk goes to `%LOCALAPPDATA%\Panneaux` (see `docs/ARCHITECTURE.md`). Say in pull requests what
any new system change or network access does (`CONTRIBUTING.md`).
