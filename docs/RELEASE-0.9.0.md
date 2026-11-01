# Winlestia 0.9.0

Winlestia is a free, open-source desktop shell for Windows 11, bringing a Caelestia-inspired interface, dynamic themes, glass effects, a customizable dock and powerful desktop tools. It replaces the surface of Windows, not the system: a dock instead of the taskbar, with a dashboard and glass panels on top of your normal desktop. The interface is a port of [Caelestia](https://github.com/caelestia-dots/shell). Licence: GPL-3.0.

## Highlights

- Dashboard with media (and audio bars), performance and workspaces pages. Ctrl+F1.
- Command palette (Ctrl+F2): calculator, emoji, wallpapers, open windows, Windows Settings pages, indexed file search, session actions with confirmation.
- Dock with virtual-desktop indicator, window previews and optional automatic tiling.
- Quick controls with a Wi-Fi network list and per-display brightness (DDC/CI).
- Wallpaper switcher with live preview and per-display targeting; the colour palette can follow the wallpaper (Settings, Appearance, Theme, Automatic; the default Porcelain theme is fixed and does not).
- `winlestia` command line to drive the running shell from scripts.

## Since 0.8.0

**Added:** Wi-Fi panel; brightness per display, with OSD for brightness and microphone; dashboard audio bars; fullscreen quiet mode; full-screen wallpaper switcher; palette entries for windows, Windows Settings, files and session actions; settings search; dock desktop `+` button and desktop close; tiling keyboard shortcuts and exclusions; "active window only" accent border; the `winlestia` command; optional, reversible terminal integration.

**Changed:** wallpaper collection replaced by 10 images supplied by the maintainer, shipped unmodified; Automatic theme default glass transparency is 5 %; panel text stays legible on bright wallpapers in the Automatic theme; data folder is `%LOCALAPPDATA%\Panneaux` (the former project name, kept on purpose), migrated at first launch; start-up through a per-user scheduled task; wallpaper applied through the Windows desktop wallpaper interface; messengers loaded on demand; several start-up and idle-time performance improvements; French, English and Spanish strings.

**Fixed:** desktop indicator refresh, OSD icon, a settings tile read after its delegate was destroyed, wallpaper palette over a live-wallpaper configuration, a binding loop in the Media panel, start-up log errors; the dashboard now shows the display name chosen at install (it used to show the Windows account name); upgrading no longer aborts while the shell is running; interface strings completed in English and Spanish, and the dashboard calendar follows the app language.

Full list: [CHANGELOG.md](../CHANGELOG.md).

## Requirements

- Windows 11, 64-bit (x64). Not for Windows 10.
- No administrator rights: per-user install in `%LOCALAPPDATA%\Programs\Winlestia`.
- Nothing else to install. An Internet connection is needed only if you tick the optional terminal integration (it downloads Clink 1.9.35 from its official page and checks a pinned SHA-256).

## Install

1. Download `Winlestia-Setup-0.9.0.exe` from the assets below.
2. In PowerShell: `Get-FileHash .\Winlestia-Setup-0.9.0.exe -Algorithm SHA256`. The value must match the installer SHA-256: `c9c406669bd9e7205aba2c3458fb23b727cd53b74b1991bc0456d06152442834`.
3. Run the setup (its wizard is in French only). Updating from an older version works the same way: the setup closes the running shell itself and keeps your settings.

**SmartScreen.** The binaries are not code-signed, so Windows SmartScreen will probably show "Windows protected your PC". After checking the SHA-256, choose **More info**, then **Run anyway**.

Step-by-step guide: [docs/INSTALL.md](INSTALL.md). Shortcuts: [docs/SHORTCUTS.md](SHORTCUTS.md).

## If the taskbar does not come back

Wait a few seconds (a watchdog restores it), press Ctrl+Alt+Backspace, use the Start menu shortcut "Restaurer la barre des tâches Windows", or run `RETABLIR_BARRE_WINDOWS.exe` in `Glass-v5`. All options: [docs/TROUBLESHOOTING.md](TROUBLESHOOTING.md).

## Known limitations

- The installer wizard is in French only.
- English and Spanish are translated for the shell panels; some rarer system-derived labels may still appear in French. The language follows the Windows display language and can be forced with the `language` key (`auto`/`fr`/`en`/`es`) in `%LOCALAPPDATA%\Panneaux\Caelestia\language.json`. Fallback text is French.
- Not tested on a clean machine or VM, nor after a real reboot or sign-in (the scheduled-task launch was tested manually).
- Binaries are **not signed**, so SmartScreen may warn.
- Windows 11 x64 only. Temperature is in Celsius only.
- There is **no notification centre**: only the on-screen indicator and toasts. Windows notifications can be switched off from the tools panel.
- Window preview quality in the dock depends on what each application exposes to Windows.
- Weather: with no city set, your approximate location comes from your IP address through wttr.in; the forecast comes from Open-Meteo. Temperature is in °C only.
- Bundled wallpapers: the 10 images were supplied by the maintainer and are not covered by the GPL-3.0 licence; rights remain with their owners (see `assets/wallpapers/CREDITS.txt`). A rights holder can ask for removal through a GitHub issue.
- Automatic tiling is **off by default**.
- Tested on a real Windows 11 Home machine with a single 3440x1440 display, not on a clean virtual machine. Other display layouts, scalings and Windows editions have had less testing.
- Enterprise (802.1X) and hidden Wi-Fi networks are not handled in the Wi-Fi panel.
- The optional terminal integration prints a welcome banner in French, showing your session and computer names.
- When you confirm a wallpaper in the switcher, the previous wallpaper can flash briefly before the new one appears.
- The wallpaper switcher also lists images from `Pictures\Wallpapers`, `Pictures` and the Windows wallpaper folders, not only the bundled set.
- CPU temperature can show N/A, depending on the hardware.

## What was tested

Tested by hand and by script on one Windows 11 Home machine (single 3440x1440 display), with the installer built from a clean checkout:

| Check | Result |
| --- | --- |
| Install, first start, dock replaces the taskbar | Pass |
| Upgrade from 0.8.0 with the shell running | Pass |
| Main panels open (settings, clipboard, tools, audio, mixer, capture, palette, dashboard) | Pass |
| Theme change and wallpaper-driven palette | Pass |
| Global shortcuts Ctrl+F1, Ctrl+F2, Ctrl+Alt+Space, Ctrl+Alt+W | Pass |
| Autostart task created, launched manually with the shell closed | Pass |
| Taskbar restored after normal close, after a killed process, and by `--restore-taskbar` and Ctrl+Alt+Backspace | Pass |
| Uninstall with the shell running (user profile and wallpaper kept) | Pass |
| Clean machine or VM; real reboot and sign-in | Not run |
| Other display layouts and scalings | Not run |

These tests ran from an MSIX-packaged parent process, which redirects `%LOCALAPPDATA%` writes for its child processes; autostart was exercised from Task Scheduler, outside that redirection.

## Reporting a bug

Use the [bug report form](https://github.com/lilidoxdev/winlestia/issues/new/choose) with your Windows build, display setup and log lines with personal data removed. Security issues: see [SECURITY.md](../SECURITY.md), not a public issue.

## Notes

Native helper binaries (`GlassBackdrop.dll`, `ProcessAudio.dll`, `RETABLIR_BARRE_WINDOWS.exe`, `SECOURS_BUREAU.exe`) are built from the C++ sources in `src/backend/*.cpp`. Third-party components are listed in `THIRD_PARTY_NOTICES.md`.
