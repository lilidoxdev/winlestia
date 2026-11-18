# Winlestia 1.0.0

First stable release, after 0.9.2. Winlestia is a free, open-source desktop shell for Windows 11, a port of [Caelestia](https://github.com/caelestia-dots/shell) (the visual reference). It replaces the surface of Windows (taskbar, panels, launcher), not the system.

## Highlights

### Dock
- Grows with the open apps and scrolls with the mouse wheel when it overflows.
- Optional battery pill for connected Bluetooth and XInput devices (off by default).
- Pins of Store (MSIX) apps survive app updates and reboots.
- Fixed: windows stuck between the dock's glass background and its icons.

### Panels
- Control center redesigned: Wi-Fi page, Bluetooth device connect/disconnect, audio output and microphone pages, per-application mixer.
- Social rail (right edge): gathers notifications from Discord, Instagram, Snapchat, WhatsApp and Telegram.
- Command palette opens faster, including the first time after start-up.

### Winjets (desktop widgets)
- Liquid glass v2: smoked fill, highlight and a thin diagonal border; blur depends on card size.
- Ten built-in Winjets replace the PC Widgets pack: Calendar, Clock, Music, System, Markets (Yahoo Finance), Storage, Files, Notes, Agenda (ICS link) and Weather (city or Windows location). Battery is still included.
- Per-widget transparency, double-click editing with a snapping grid.
- Packages are JSON (`winjet/1`); no package code is executed. Format: `docs/winjets/FORMAT-v1.md`.

### Other
- Push-to-talk voice dictation (hold AltGr, off by default).
- Redesigned file manager (Winlestia Fichiers).
- Lock screen (Ctrl+Alt+L) and window overview (Win+Alt+Tab).
- New WPF installer and uninstaller: `WinlestiaSetup.exe`.

## Install

Requires Windows 11 (64-bit). No administrator rights needed.

1. Download `WinlestiaSetup.exe` from the assets below.
2. Compare `Get-FileHash .\WinlestiaSetup.exe -Algorithm SHA256` with the SHA-256 published here.
3. Run it.

### SmartScreen

The installer and the programs it installs are not code-signed, so Windows SmartScreen shows a warning. Choose **More info**, then **Run anyway**. A matching checksum shows the file is the published one; it does not replace a signature.

### If the taskbar is missing

Press Ctrl+Alt+Backspace, or run `Panneaux.Glass.exe --restore-taskbar` from the `Glass-v5` folder of the install.

### Uninstall

Run `WinlestiaSetup.exe` again and choose uninstall, or use Settings > Apps. The taskbar is restored first. Settings in `%LOCALAPPDATA%\Panneaux` are kept or sent to the Recycle Bin, as you choose.

Full list of changes: [CHANGELOG.md](../CHANGELOG.md).
