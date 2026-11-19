# Contributing to Winlestia

Thanks for your interest. Winlestia is a GPL-3.0 project; contributions are accepted under the same licence. Please open an issue to discuss a larger change before writing it.

## Repository layout

| Path | Content |
|---|---|
| `src/backend` | .NET 8 backend (`Panneaux.Glass.csproj`, C#), native helpers (`*.cpp`) and their `build-*.cmd` |
| `src/frontend` | Qt Quick interface (QML) and Python 3.12 / PySide6 host (`frontend.py`) |
| `src/cli` | The `winlestia` command (.NET 8) |
| `src/files` | Winlestia Files (.NET 8) |
| `src/terminal` | Terminal integration (Clink theme scripts) |
| `installer` | Inno Setup script and `build.ps1` |
| `tests` | Small console test projects, one per area (`tests/WifiPolicy`, `tests/CliCommands`, ...), plus Python scripts |

## Building from source

Requirements: Windows 11 x64, .NET 8 SDK, Python 3.12 (for the Qt frontend and for `pip`), PowerShell 7, Inno Setup 6 (to build the installer), and MSVC only if you rebuild the native helpers.

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
