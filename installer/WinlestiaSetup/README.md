# WinlestiaSetup

Custom installer for Winlestia. One window, no Windows chrome, DWM backdrop, animated step transitions. It replaces the Inno Setup wizard (`installer/Winlestia.iss` is kept).

## Why WPF on .NET 8

- Same stack as the backend (C#, .NET 8). No new toolchain, and the project builds with the SDK already used for `src/backend`.
- Published self-contained and single file for win-x64: nothing to install first, no .NET runtime prerequisite on the target PC.
- WinUI 3 was considered. Unpackaged, it needs the Windows App SDK runtime or its bootstrapper on the target machine, which defeats a one-file installer.
- Qt or PySide would also work visually, but would add about 100 MB to an installer that is otherwise small.

## Window and motion

- Borderless window (`WindowChrome`, caption height 0). Backdrop through `DwmSetWindowAttribute`: `DWMWA_SYSTEMBACKDROP_TYPE` (38) set to acrylic, falling back to Mica, plus `DWMWA_WINDOW_CORNER_PREFERENCE` (33) for rounded corners and `DWMWA_USE_IMMERSIVE_DARK_MODE` (20).
- Palette derived from the wallpaper (dominant hue, one accent). Falls back to a fixed hue if the wallpaper cannot be read. `--wallpaper <file>` overrides it.
- Step transitions are one eased value (`CubicEase`, ease-out, 300 ms) driving a slide and a fade. They are skipped when `SystemParameters.ClientAreaAnimation` is off, or with `--no-motion`.
- The only looping animation is the progress bar while the installation runs.

## Usage

```
WinlestiaSetup.exe                         install, or update/modify/uninstall chooser if an installation exists
WinlestiaSetup.exe --uninstall [--keep-data|--remove-data] [--from-shell] [--dry-run]
WinlestiaSetup.exe --dry-run               full journey, writes nothing outside the log
WinlestiaSetup.exe --lang fr|en|es|de
WinlestiaSetup.exe --screenshots <dir>     renders every screen to PNG, then exits
Other: --no-motion, --wallpaper <file>
```

An existing installation is detected from the `Uninstall\Winlestia` key or the install folder. The welcome screen then shows the installed and new versions.

## Payload

`build-setup.ps1` runs `installer/build.ps1`, zips `build\payload` with the files the Inno script copied (license, notices, wallpapers, `Close-Winlestia.ps1`) into `payload.zip`, and publishes the setup.

- `.\build-setup.ps1` puts `payload.zip` next to the exe.
- `.\build-setup.ps1 -Embed` embeds it in the exe.

Without a payload the installer runs in demonstration mode: every screen works, nothing is installed.

## Languages

`Strings/<lang>.json` for fr, en, es, de. A missing key falls back to English. A `Strings` folder next to the exe overrides the embedded files.

## Features and config keys

| Feature | Key in `preferences-glass.json` | Default |
| --- | --- | --- |
| Windows taskbar replacement | `ReplaceTaskbar` | on |
| Dock | `DockVisible` | on |
| Panels | none | locked |
| Winjets | none | locked |
| Terminal | none (runs the terminal integration script) | off |
| Pill | `Pill` | on |
| Side rails | `SocialRailVisible`, `BookmarksRailVisible` | on |

Panels and Winjets have no on/off key in the backend, so the installer shows them locked rather than pretending to control them.

The update notification switch on the Updates screen is not wired: the shell has no release check yet, and the switch is disabled and labelled accordingly.

## Uninstall safety

- Order: restore the Windows taskbar and explorer.exe, remove the scheduled task, stop processes, undo the folder handler and terminal integration, clean the registry, remove shortcuts, move files, verify the taskbar is back.
- Nothing is deleted permanently. Folders go to the Recycle Bin (`SHFileOperation` with `FOF_ALLOWUNDO`). If that fails, the folder is left and reported.
- The program folder is removed only if it equals `%LOCALAPPDATA%\Programs\Winlestia` exactly (never a drive root). Data folders are the exact names `Panneaux` and `Winlestia` under `%LOCALAPPDATA%`, and only with `--remove-data`.
- The setup copies itself to `%TEMP%` and relaunches when started from the install folder.

## Screenshots

`--screenshots` uses `RenderTargetBitmap`, which cannot capture DWM effects, so the acrylic backdrop in those PNGs is simulated with the blurred wallpaper. The real window uses DWM. Output also includes 18 transition frames, six picked frames and a 6x slowed MP4/GIF (needs `ffmpeg` on PATH).

## Limits

- Not signed: SmartScreen warns.
- The sensors helper and PawnIO driver are not handled by the setup.
- The temporary copy of the uninstaller in `%TEMP%` remains after uninstalling.
- Inno `unins000` files of an earlier Inno install are left in place.
- Glyph icons come from Segoe Fluent Icons (Windows 11).
