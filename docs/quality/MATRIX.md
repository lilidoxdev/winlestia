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
| .NET 10 | Requested | NOT done: SDK 10 not installed, awaiting user permission to install. Plan: separate commit, retarget all csproj, Microsoft.Data.Sqlite 10.x, check Whisper.net and LibreHardwareMonitorLib, self-contained publish, test installer on a PC without SDK | none | none | Not started |

## Automated tests

All 27 projects pass.

With counts: AppCatalogueWatch 32, AppNameNormalizer 43, AudioBars 22, FileSearch 22, AutostartPolicy 10, FramePolicy 10, BrightnessPolicy 21, CliCommands 106, DesktopReassert 15, FilesHandler 64, FilesPinMigration 8, FilesPreloadPolicy 12, FilesRouter 27, MediaIdentity 61, SecurityHardening 20, SocialPolicy 11, WifiPolicy 41, FolderIcons 18.

PASS without counts: DataDirMigration, StaleDockTarget, TileLogicTests, FullscreenWatcher (finished within 90 s this run), NetworkKind, VirtualDesktops.

JSON flags all true: Explorer077, Native95, Tiler075. Audio06: variation=True, silence=True.

## Manual checks done

- qmllint on the changed QML: 0 errors.
- Installer compiled: build 1 min 13 s with warm caches.
- Payload inspected: self-contained runtime, embedded Python, restore tools present, no Unsplash key.

Installer artefact: `build\out\Winlestia-Setup-0.9.0.exe`, 193,033,943 bytes, SHA256 `a7cd9094f65497b79c2b95eb52e6e3ef600ba9dce06e3e673b672c0101bcca41`.

## Not executed

- Live UI checks (media widget with Firefox/YouTube, social rail at several DPI and languages, top-bar reveal).
- Installer scenarios (fresh, upgrade, special paths, no-SDK launch, uninstall).
- CPU, memory and latency measurements (visualizer capture, position timer).
- .NET 10 migration.

## Known issues found, not fixed

- Payload contains the build user path in `.pyc` and numpy config. Build from a neutral path such as `C:\wl-build`, as recommended in the `build.ps1` header.
- `build.ps1` fails if stderr is redirected inside PowerShell (a pip warning becomes fatal).
- `build.ps1` hard-codes 0.9.0 in its final message.
- `src/terminal/clink-profile` (3 files) is tracked since v0.7.6 and shipped by `build.ps1`. Owner decision pending.
- Autostart task name is `WinlestiaShell`.
