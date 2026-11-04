# Contributing to Winlestia

Thanks for your interest. Winlestia is a GPL-3.0 project; contributions are accepted under the same licence. Please open an issue to discuss a larger change before writing it.

## Repository layout

| Path | Content |
|---|---|
| `src/backend` | .NET 10 backend (`Panneaux.Glass.csproj`, C#), native helpers (`*.cpp`) and their `build-*.cmd` |
| `src/frontend` | Qt Quick interface (QML) and Python 3.12 / PySide6 host (`frontend.py`) |
| `src/cli` | The `winlestia` command (.NET 10) |
| `src/files` | Winlestia Files (.NET 10) |
| `src/terminal` | Terminal integration (Clink theme scripts) |
| `installer` | Inno Setup script and `build.ps1` |
| `tests` | Small console test projects, one per area (`tests/WifiPolicy`, `tests/CliCommands`, ...), plus Python scripts |

## Building from source

Requirements: Windows 11 x64, .NET 10 SDK, Python 3.12 (for the Qt frontend and for `pip`), PowerShell 7, Inno Setup 6 (to build the installer), and MSVC only if you rebuild the native helpers.

**Backend**

```powershell
dotnet publish src\backend\Panneaux.Glass.csproj -c Release -r win-x64 --self-contained true -o build\dev\Glass-v5
```

**Frontend** (development environment)

```powershell
python -m venv src\frontend\.venv
src\frontend\.venv\Scripts\pip install -r src\frontend\requirements.txt
```

`dotnet publish` alone is not enough to run the shell. The native helpers (`GlassBackdrop.dll`, `SECOURS_BUREAU.exe`, `RETABLIR_BARRE_WINDOWS.exe`) are not produced by it (only `ProcessAudio.dll` is copied from `src/backend/native/`), and the backend starts the frontend from `<Glass-v5>\..\Caelestia-Windows\.venv\Scripts\pythonw.exe` unless you pass `--qt-root=<frontend folder>`; the Qt shell needs the `--qt-shell` flag. The installed layout puts `Glass-v5` (backend) and `Caelestia-Windows` (frontend, with its Python) side by side. The quickest way to try a full build is to run `installer\build.ps1` and install the result.

**Native helpers.** `src/backend/*.cpp` are built with MSVC through `build-backdrop.cmd`, `build-process-audio.cmd`, `build-rescue.cmd` and `build-taskbar-rescue.cmd` (the scripts call `vcvars64.bat` from a Visual Studio install; adjust the path if yours differs). Results go in `src/backend/native/`, which is git-ignored; see `src/backend/native/README.md`. `installer/build.ps1` stops with a message if a required native file is missing.

**Installer.** `installer\build.ps1` does the whole chain: publishes the backend, Files and CLI, copies the frontend, downloads the embedded Python 3.12, installs the pinned Python dependencies, verifies the embedded runtime, then calls Inno Setup. Output: `build\out\Winlestia-Setup-<version>.exe`.

## Tests

Each folder in `tests/` that has a `.csproj` is a console program that links the source files it checks and prints a result.

```powershell
dotnet run --project tests\WifiPolicy
```

Run the one that matches the code you changed rather than everything. Python checks live in `tests/*.py` and `src/frontend/tests`.

**Never test against your real profile.** Set `WINLESTIA_DATA_DIR` to a throwaway folder before launching the shell or a test, so preferences, logs and catalogues go there instead of `%LOCALAPPDATA%\Panneaux`:

```powershell
$env:WINLESTIA_DATA_DIR = "$env:TEMP\winlestia-test"
```

## Rules

- **Palette-only colours.** All interface colours come from the wallpaper-derived palette (`Colours.palette.*`). Do not hard-code colours in QML.
- **No new features on the legacy WPF Glass shell.** It remains for compatibility (`--legacy-shell`); new work goes to the Qt shell.
- **The restore paths are an invariant.** Anything that hides or replaces part of Windows must keep every way back working: the taskbar watchdog, Ctrl+Alt+Backspace, `Panneaux.Glass.exe --restore-taskbar`, `RETABLIR_BARRE_WINDOWS.exe`. Do not change them without testing each.
- **Naming.** "Caelestia" is spelled with an A; "Winlestia" is never abbreviated. The `Panneaux` folder and executable names are legacy and are kept deliberately; do not rename them in unrelated changes.
- **Network access.** Say in the pull request what any new network access does and why.
- Keep changes focused: one concern per pull request, no drive-by refactors.
- User-facing text is translated (French, English, Spanish); see `src/frontend/localization.py`.

## Pull requests

Describe what changed and why, how you tested it (which test project or manual steps, on which Windows build), and note anything touching restore paths, installer or autostart.

By contributing you agree your work is released under GPL-3.0. Third-party code must be listed in `THIRD_PARTY_NOTICES.md` with its licence.

## Development setup and conventions

See `docs/ARCHITECTURE.md` (module map and names that must not change), `docs/BUILD-AND-TEST.md` (exact commands), `docs/WINDOWS-INTEGRATIONS.md` and `docs/RESTORE.md`.

- **Setup.** .NET SDK 8.0.x; Python 3.12 with a PySide6 virtual environment in `src/frontend/.venv` (`pip install -r src/frontend/requirements.txt`).
- **Single test.** `dotnet run --project tests/<Name> -c Release` prints `TOTAL x/y`. Do not run the whole suite for a small change. `tests/FullscreenWatcher` currently does not terminate (known issue).
- **QML lint.** Run `pyside6-qmllint` from the venv (`-I src/frontend/qml`) on each QML file you touched and fix what you introduced.
- **Style.** Target 120 columns (advisory, see `.editorconfig`); English identifiers and comments; match the surrounding file's indentation and layout. Do not reformat code you are not changing.
- **User-visible strings.** Wrap QML text in `qsTr(...)` and add a `fr|en|es` row to `ROWS` in `src/frontend/localization.py`, keyed by the exact French text.
- **No hard-coded colours.** Use the wallpaper-derived palette through `Colours` / `Appearance` tokens (`src/frontend/qml/qs/services/Colours.qml`, `qs/config/Appearance.qml`).
- **No idle cost.** No timers, polling or animations that run while nothing happens; prefer events and stop work when hidden.
- **Do not rename** preferences JSON properties, pipe names or IPC JSON keys, `Bridge.*` members used by QML, `ROWS` keys, command-line switches, or `catalog.json` fields (`docs/ARCHITECTURE.md`).
- **Generated files.** Verification and benchmark outputs (`src/backend/verification*.json`, `performance-*.json`, `motion-*.json`, ...) are git-ignored; do not commit them.
- **Commits.** One concern per commit, imperative subject line, explain why in the body; no unrelated formatting or merge noise. Never commit personal data (`preferences-glass.json`, logs, `catalog.json`).
- **Licences.** Files derived from Caelestia keep their attribution: `src/backend/CAELESTIA-NOTICES.md`, `src/backend/LICENSE-CAELESTIA.txt`, `src/frontend/LICENSE`, `src/frontend/licenses/`, and the root `THIRD_PARTY_NOTICES.md`. Add any new third-party code there.
