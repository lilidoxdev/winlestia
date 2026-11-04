# Troubleshooting

Paths below assume the default install in `%LOCALAPPDATA%\Programs\Winlestia`. Paste them into the File Explorer address bar or the Run box (Win+R); Windows expands the variables.

## The Windows taskbar did not come back

Winlestia hides the Windows taskbar while it runs. Try these in order, stopping at the first one that works.

1. **Wait a few seconds.** A separate watchdog process restores the taskbar if the shell is stopped or crashes.
2. **Press Ctrl+Alt+Backspace.** This runs the independent rescue tool: it stops Winlestia, removes its `WinlestiaShell` start-up task (and the `Panneaux` Run value if one is still present, saving a copy first) and brings back the Desktop icons. The taskbar itself comes back through the watchdog once Winlestia has stopped.
3. **Start menu, "Restaurer la barre des tâches Windows"** (created if you left the Start menu option ticked at setup).
4. **Run `RETABLIR_BARRE_WINDOWS.exe`** from `%LOCALAPPDATA%\Programs\Winlestia\Glass-v5`.
5. **From a terminal:** `"%LOCALAPPDATA%\Programs\Winlestia\Glass-v5\Panneaux.Glass.exe" --restore-taskbar`
6. **Restart Explorer.** Task Manager (Ctrl+Shift+Esc), find "Windows Explorer", Restart.
7. **Uninstall Winlestia.** The uninstaller restores the taskbar too.

To stop Winlestia from replacing the taskbar, switch off the taskbar replacement in Settings (the option is labelled "Winlestia taskbar" in English and "Barre des tâches Panneaux" in French).

### Why the dock may not come back afterwards

- `Panneaux.Glass.exe --restore-taskbar` run while the shell is active also saves `ReplaceTaskbar=false`. The dock does not come back until you re-enable replacing the taskbar in Settings.
- Ctrl+Alt+Backspace (emergency restore) deletes the `WinlestiaShell` scheduled task, so autostart stays off until you re-enable it.
- Older installs made as administrator: the uninstaller may ask for UAC.

## The dock is not visible

The dock auto-hides. Move the pointer to the bottom edge of the screen. The Windows key opens the Start menu, not the dock.

## Desktop icons are missing or the desktop looks broken

Press Ctrl+Alt+Backspace (see step 2 above). It re-shows the Desktop icons and stops Winlestia.

## Winlestia does not start

1. Launch it from the Start menu entry. If it is already running, launching again just shows it; `Panneaux.Glass.exe --quit` closes the running copy.
2. Check the logs (next section).
3. As a fallback, start the older WPF shell: `Panneaux.Glass.exe --legacy-shell`. It is kept for compatibility and receives no new features.
4. If a shortcut such as Ctrl+F1 does nothing, another application probably owns it. Pick a different combination in Settings.
5. Try [resetting your profile](#resetting-your-profile).

## SmartScreen blocks the installer

The installer and the programs it installs are not code-signed, so SmartScreen warns on a file it does not know. Check the SHA-256 against the one in the [release notes](https://github.com/lilidoxdev/winlestia/releases/tag/v0.9.1) (`Get-FileHash .\Winlestia-Setup-0.9.1.exe`), then choose **More info**, **Run anyway**. Details in [INSTALL.md](INSTALL.md#3-smartscreen-will-probably-warn-you).

## Where the logs are

| File | Content |
|---|---|
| `%LOCALAPPDATA%\Panneaux\Caelestia\frontend.log` | Interface (Qt) log |
| `%LOCALAPPDATA%\Panneaux\glass.log` | Backend log |

`Panneaux` is the project's former name and the data folder keeps it on purpose.

**Before sharing a log, read it and remove personal data**: your Windows user name appears in file paths, and logs can contain application names, display names and similar details. Winlestia is designed not to log typed text or Wi-Fi passwords, but check anyway.

## Resetting your profile

This deletes your settings and preferences. Back the folder up first if unsure.

1. Quit Winlestia (`Panneaux.Glass.exe --quit`, or Ctrl+Alt+Backspace).
2. Rename `%LOCALAPPDATA%\Panneaux` to `Panneaux.old`.
3. Start Winlestia again; it creates a fresh profile. Bring files back from `Panneaux.old` if you want to keep some.

Uninstalling does not remove this folder.

## Privacy notes

Weather uses your IP address (through wttr.in, which therefore sees it) to find a location when no city is set; choosing a city in Settings avoids that lookup; the forecast comes from Open-Meteo. After uninstalling, `%LOCALAPPDATA%\Panneaux` and `HKCU\Software\Winlestia\DisplayName` remain; delete the folder and run `Remove-Item -Path HKCU:\Software\Winlestia -Recurse` to remove them.

## Reporting a bug

Open an issue using the [bug report form](https://github.com/lilidoxdev/winlestia/issues/new/choose). Useful information: Winlestia version, Windows build (`winver`), number and resolution of displays, what you did, what you expected, and relevant log lines with personal data removed. For a security problem, do not open a public issue; see [SECURITY.md](../SECURITY.md).
