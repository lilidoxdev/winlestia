# Build and test

Requirements: Windows 11 x64, .NET SDK 8.0.x, Python 3.12, PowerShell 7, Inno Setup 6 (installer only), MSVC only
to rebuild the native helpers. Commands below come from `installer/build.ps1` and the `.csproj` files.

## Backend

`src/backend/Panneaux.Glass.csproj`: `net8.0-windows10.0.19041.0`, WPF + WinForms, `win-x64`, version in `<Version>`.

```powershell
dotnet build src\backend\Panneaux.Glass.csproj -c Release
dotnet publish src\backend\Panneaux.Glass.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -o build\dev\Glass-v5
```

Other .NET projects (the installer publishes all three into the same `Glass-v5` folder):

```powershell
dotnet publish src\files\Winlestia.Files.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o build\dev\Glass-v5
dotnet publish src\cli\winlestia.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o build\dev\Glass-v5
```

## Native helpers

`GlassBackdrop.dll`, `ProcessAudio.dll`, `SECOURS_BUREAU.exe`, `RETABLIR_BARRE_WINDOWS.exe` are not produced by
`dotnet publish`. Rebuild with MSVC from `src\backend`: `build-backdrop.cmd`, `build-process-audio.cmd`,
`build-rescue.cmd`, `build-taskbar-rescue.cmd`; results live in `src\backend\native\` (git-ignored, see its
`README.md`). `installer\build.ps1` stops if `RETABLIR_BARRE_WINDOWS.exe`, `SECOURS_BUREAU.exe` or
`GlassBackdrop.dll` is missing there.

## Frontend (development)

```powershell
python -m venv src\frontend\.venv
src\frontend\.venv\Scripts\pip install -r src\frontend\requirements.txt
```

Pinned in `src/frontend/requirements.txt`: PySide6 6.10.3, material-color-utilities, numpy, Pillow. The shipped
build uses embedded Python 3.12.10 (`$pythonVersion` in `installer/build.ps1`).

## Installer

```powershell
pwsh installer\build.ps1
```

Steps: clean `build\payload`, publish backend/Files/CLI into `build\payload\Glass-v5`, copy native files and
`Inventory.ps1`, copy `src\frontend` to `build\payload\Caelestia-Windows`, unpack embedded Python into
`.venv\Scripts`, `pip install --target` the requirements, prune unused Qt modules, verify the runtime and
frontend import, copy `src\terminal`, then run `ISCC.exe installer\Winlestia.iss`.

Output: `build\out\Winlestia-Setup-<version>.exe` (`OutputDir=..\build\out` in `installer/Winlestia.iss`; the
version is `AppVersion` there and `<Version>` in the csproj; the final `Write-Host` in `build.ps1` also hard-codes
it). For a neutral release build follow the comment header of `installer/build.ps1` (build from a path without a
user name).

## Tests

Each `tests/<Name>` folder is a console project that links the source files it checks and prints `TOTAL x/y`.

```powershell
dotnet run --project tests\WifiPolicy -c Release
```

Run only the project matching your change. Known issue: `tests/FullscreenWatcher` does not terminate.
Set `WINLESTIA_DATA_DIR` to a throwaway folder first so nothing touches `%LOCALAPPDATA%\Panneaux`:

```powershell
$env:WINLESTIA_DATA_DIR = "$env:TEMP\winlestia-test"
```

Python checks: `tests\*.py` and `src\frontend\tests\*.py` (some need a running shell or a Qt display; read the
script header first). QML lint: `qmllint` from the PySide6 install, for example
`src\frontend\.venv\Scripts\pyside6-qmllint.exe -I src\frontend\qml <file.qml>`.
