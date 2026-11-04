# Verification matrix

Branch `quality/community-fixes`, commits `6b1ba4a..17cbda7`. Date: 2026-11-04.

## Request matrix

| Item | Cause | Fix | Files / commit | Proof | Limits |
|---|---|---|---|---|---|
| #1 media widget | Firefox AUMID hash shown as name; dead session object kept (not compared by AUMID); metadata not re-read; position not ticking (frontend `positionChanged` was a no-op, duration may be 0); audio session matching by exe stem fails for Firefox child processes; visualizer target PID 0 for Firefox | Identity compared by AUMID; readable display name; metadata re-read; position extrapolated only inside the existing visible-dashboard timer; audio sessions matched through the process tree; visualizer uses WASAPI process loopback (`PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE`, `src/backend/ProcessAudio.cpp:21`) on the root PID of the matched tree, 2048-pt Hann FFT into 48 log bands; paused: capture destroyed, bars decay to 0; Discord not captured unless a descendant. Dashboard bar (AudioBars) = default output loopback, unchanged | ebac43b | `tests/MediaIdentity` 61/61 | Not yet verified live with Firefox/YouTube. Registry DisplayName may not exist (process heuristic used). Ambiguous browsers shown as "Navigateur". Per-tab volume impossible (Windows audio sessions are per process) |
| #2 social rail overlays tabs | Rail drawn over tab content | Dashboard slides by the real overlap and scales panes (min 0.7). Below ~1000 logical px the rail overlays (documented fallback) | ef8a438 | qmllint 0 errors; arithmetic checked for 1920 and 1280 widths | Not visually checked at 100/125/150/200 %, FR/EN/ES, multi-DPI. Text scaled to >= 0.7 |
| Reopen on last tab (top-bar reveal) | Dashboard always opened on the default tab | Last tab kept in session memory only (explicit decision: not persisted). Drag-drop menu priority unchanged | ef8a438 | qmllint 0 errors | Multi-monitor, fullscreen and quiet mode not manually tested |
| Installer English + auto detect | Installer was French only | English default, language auto-detected (EN fallback, FR, ES); UTF-8 BOM added; licence files shipped | b27b22c, 17cbda7 | Installer compiled | Not executed: fresh install, upgrade with preferences kept, paths with spaces and accents, launch without SDK, uninstall (needs test account or VM) |
| Security | Audit requested | SQL injection reported as false positive, with evidence. Hardening: L1 bookmarks http/https only (user-visible change); L2 private folder for task XML; L3 `install.py` typographic quotes | 966b58e, see `SECURITY-AUDIT.md` | `tests/SecurityHardening` 20/20, `tests/FileSearch` 22/22 | No confirmed vulnerability; no external pen-test |
| Readability | Wide files, ad hoc name logic, tracked artefacts | 5f2c24a name normaliser; b7220cb docs, `.editorconfig`, 31 artefacts untracked; 659b862 QtShellBridge split; 8857ab3 Coordinator/Models/Visuals split | 5f2c24a, b7220cb, 659b862, 8857ab3 | Normaliser 43/43 + PowerShell parity; splits checked by token equivalence; Preferences JSON order kept | Behaviour-preserving only; no runtime comparison beyond tests |
| .NET 10 | Requested | 31 csproj net8.0 to net10.0 (suffixes kept); Microsoft.Data.Sqlite 10.0.14, other packages unchanged and build. Self-contained publish verified: coreclr/hostfxr present, runtimeconfig 10.0.14, `runtimes/` tree identical to the net8 payload. Native95 test made baseline-aware: on net10 the maximized titled fixture already has a window region before RoundMaximized; product restores the original region correctly; test compares with the captured baseline (type + EqualRgn) | fe48ea8 | Full build; all test projects pass | Installer not yet run on a PC without SDK (no test session available; owner chose to publish and handle bug reports) |
| Hygiene | Not requested explicitly | Duplicate ProcessAudio.dll item removed, CS9191 fixed, analyzers enabled as suggestions (`Directory.Build.props`, `.editorconfig`), local CI workflow `.github/workflows/build.yml` (not pushed) | 44d3449 | Build | Backend warning WFO0003 (app.manifest high-DPI, ApplicationHighDpiMode) left on purpose: changing it may alter DPI behaviour |
| Atomic writes | Robustness | `AtomicFile.cs` / `atomicfile.py` (unique tmp, flush, replace) on preferences, clipboard history, social counts, recents, taskbar lease, tiler exclusions, handler backup, Explorer appearance files; Python language.json, hidden wallpapers, weather city | 125dd1c | `tests/AtomicFile` 13/13 | No power-loss test |
| Preferences recovery | Robustness | Corrupt JSON: keep `.corrupt.bak`, restore from `.bak`, defaults only if both fail. Transient IO errors retried once, never treated as corruption | 89f76b2 | `tests/StoreRecovery` 10/10 | None |

## Automated tests

All 30 projects pass (tests-final run). New: AtomicFile 13, StoreRecovery 10.

With counts: AppCatalogueWatch 32, AppNameNormalizer 43, AudioBars 22, FileSearch 22, AutostartPolicy 10, FramePolicy 10, BrightnessPolicy 21, CliCommands 106, DesktopReassert 15, FilesHandler 64, FilesPinMigration 8, FilesPreloadPolicy 12, FilesRouter 27, MediaIdentity 61, SecurityHardening 20, SocialPolicy 11, WifiPolicy 41, FolderIcons 18.

PASS without counts: DataDirMigration, StaleDockTarget, TileLogicTests, FullscreenWatcher (finished within 90 s this run), NetworkKind, VirtualDesktops.

JSON flags all true: Explorer077, Native95, Tiler075. Audio06: variation=True, silence=True.

## Manual checks done

- qmllint on the changed QML: 0 errors.
- Installer compiled: build 1 min 13 s with warm caches.
- Payload inspected: self-contained runtime, embedded Python, restore tools present, no Unsplash key.

Installer artefact: build/out/Winlestia-Setup-0.9.0.exe (rebuilt on .NET 10; 196,046,165 bytes, SHA256 64c53b17f2c49b08d96f7201b111caea6c7b58857b3c7d77bba516fa02ad4ce2)

## Not executed

- Live UI checks (media widget with Firefox/YouTube, social rail at several DPI and languages, top-bar reveal).
- Installer scenarios (fresh, upgrade, special paths, no-SDK launch, uninstall).
- CPU, memory and latency measurements (visualizer capture, position timer).
- Installer run on a PC without SDK (.NET 10 payload).
- Power-loss test of atomic writes.

## Known issues found, not fixed

- Payload contains the build user path in `.pyc` and numpy config. Build from a neutral path such as `C:\wl-build`, as recommended in the `build.ps1` header.
- `build.ps1` fails if stderr is redirected inside PowerShell (a pip warning becomes fatal).
- `build.ps1` hard-codes 0.9.0 in its final message.
- `src/terminal/clink-profile` (3 files) is tracked since v0.7.6 and shipped by `build.ps1`. Owner decision pending.
- Autostart task name is `WinlestiaShell`.
- Swallowed errors left as findings: Store legacy Workspace-v4 migration `catch{}`, DataDir `CreateDirectory` `catch{}`, FilesHandler `LoadBackup` returns null, icon writes `catch{}`, SocialService `QueueSave` `catch{}`.
- Timers: Coordinator.cs 120 ms timer runs for the process lifetime (cheap; follow-up). Frontend pointer timer 500 ms when idle.
- async void audit: no crash path (global Dispatcher/AppDomain handlers exist; Files app handlers wrapped).
- Backend warning WFO0003 left on purpose (see Hygiene row).
