# Changelog

Earlier releases are described in `docs/RELEASE-*.md`. Release notes for each version are also published on the GitHub Releases page.

## Unreleased

### Changed

- **Settings reorganised** into eight pages (Appearance, Interface, Docks and panels, Keyboard shortcuts, Media and audio, Apps/bookmarks/links, Files and Windows, General), with the search field and a navigation column. Interface text is translated to English and Spanish.
- **Keyboard shortcuts page** lists every global shortcut with its combination; configurable ones can be reassigned (Windows-reserved and conflicting combinations are refused with an explanation, a failed change is rolled back). Fixed ones show the reason.
- **Win+Alt+S** opens Settings (reassignable). Other ways: tray menu, `winlestia panel settings`.
- **Dock, bookmarks panel and social panel** can be hidden individually. Hiding the dock while it replaces the Windows taskbar restores the Windows taskbar.
- Settings snapshot now includes the data folder and the version.
- `WINLESTIA_DATA_DIR` is now also honoured by the frontend (language, weather, thumbnails, logs) and by the backend cache and recent launches.

### Fixed

- Arrow keys drawn as empty boxes in the tiling shortcut list.
- A shortcut that failed to register at start-up was reported as "unchanged"; the registration is now retried and its real result shown.
- A Settings shortcut equal to another configured combination is replaced at load by the first free one among Win+Alt+S, Ctrl+Alt+F10, Ctrl+Alt+F11.
- Dock, bookmarks and social panels are no longer hidden before the first settings snapshot arrives.

## 0.9.0 - 2026-10-31

Changes since 0.8.0.

### Added

- **Command line.** `winlestia` drives the running shell from scripts through a named pipe reserved to the current user, with an allow-list of commands and no stop or delete command: `status`, `panel`, `palette`, `desktop`, `wallpaper`, `find`, `brightness`, `visualizer`, `quiet`, `wifi`, `help`. Optional PATH entry at setup.
- **Wi-Fi panel** in the quick controls: network list (name, strength, lock, connected, remembered), connect with password, forget. Uses Native Wifi, scans only while the list is open, never stores or logs the password. Enterprise (802.1X) and hidden networks are not handled; a button opens Windows settings instead.
- **Brightness per display**, external displays included (DDC/CI), in the quick controls and through `winlestia brightness`. Brightness and microphone mute/level also appear in the on-screen indicator (OSD), next to volume.
- **Audio bars** in the dashboard Media panel, from a loopback capture of the default output that runs only while the panel is visible. Nothing is recorded.
- **Fullscreen quiet mode**: when a window covers a whole display, the dock, OSD, audio bars, animations and edge tracking are switched off and timers slow down, then everything returns on exit. Option in Settings (on by default); `winlestia quiet` to test.
- **Wallpaper switcher** (Win+Alt+W, fallback Ctrl+Alt+W): full-screen, live preview, bundled collection of 10 images, add, open and remove images. Per-display targeting with Tab, and a notice when DesktopHut, Wallpaper Engine, Lively or Wallpaper X is running.
- **Command palette** (Ctrl+F2): open windows, about thirty Windows Settings pages, indexed file search with the `>file ` prefix, `>emoji` picker, `>wallpaper` picker, calculator with an operator (including French words and locale decimals), and session actions (shut down, restart, sign out, lock, sleep, hibernate) that ask for confirmation.
- **Settings search** across the six pages by title and description, ignoring case and accents; the result opens the page and highlights the row.
- **Virtual desktops in the dock**: indicator, `+` button to create a desktop, right-click to close one, Win+Ctrl+arrow switching.
- **Automatic tiling** controls: stable order, keyboard focus, swap, resize and float, drag-to-swap, per-application exclusions; dock button to turn it on or off. Still off by default.
- **Accent border**: optional "active window only" mode.
- **"Reduced effects"** switch in the quick controls.
- **Terminal integration** is now optional (unchecked at setup) and reversible.

### Changed

- **Data folder** moved to `%LOCALAPPDATA%\Panneaux`, migrated at first launch. The name is the project's former name and is kept on purpose.
- **Start-up** uses a per-user scheduled task, `WinlestiaShell`, instead of only the Run registry value. The installer still writes the Run value; the first shell start creates the task and then removes the Run value (if the task cannot be created, the Run value stays as a fallback). Desktop icons are re-applied after Explorer events and restarts; the Windows taskbar waits for Explorer if it is not ready.
- **Wallpaper** is applied through the Windows desktop wallpaper interface (system transition, no extra window).
- **Dock**: collapsible group of buttons behind an arrow; windows of elevated programs are listed (activate or close only).
- **Recent applications**: a Start menu shortcut appears in the palette about three seconds after being added; Store apps are re-read when the palette opens if the catalogue is stale; icon extraction is retried a limited number of times.
- **Messengers** load on first opening and are released after being hidden unless background mode is on.
- **Performance**: panels are loaded on first opening then kept, rarely used windows on demand, the terminal and interface palettes are computed in short-lived sub-processes, the clock updates once a minute, ACPI and PDH probes stop after three failures, the Explorer appearance hook is event-driven, pointer and glass sync intervals adapt, and the recycle-bin drop zone loads on the first file drag.
- **Interface**: theme tokens, rounded corners from `Appearance`, spacing tokens in secondary panels, grouped inks; translated interface strings (French, English and Spanish), localized units (GB/MB/KB or Go/Mo/Ko), empty states, display name in the dashboard.
- **Wallpaper collection** replaced by 10 images supplied by the maintainer, shipped as unmodified original files (not re-encoded or resized). They are not covered by the GPL-3.0 licence; rights remain with their owners and their licence has not been established. See `assets/wallpapers/CREDITS.txt`; removal can be requested through a GitHub issue. Existing copies of the previous bundled images are purged on upgrade.
- **Automatic theme glass transparency** now defaults to 5 %. Profiles still holding the old default value are migrated once.
- **Terminal**: translucent dark console background, text and accent from the palette; palette sync differential on restore.
- Session menu actions require confirmation, checked on the backend as well.

### Fixed

- Panel text on bright wallpapers in the Automatic theme: with a dark scheme, the glass tint is denser when the wallpaper is bright so pale text stays legible. Darker wallpapers render as before.
- Desktop indicator refresh, OSD icon and tooltips.
- Settings theme tile read after its delegate was destroyed.
- Wallpaper palette not following a static wallpaper when a live-wallpaper configuration existed.
- Binding loop in the Media panel; missing `shortcutText` slot.
- Start-up log errors.
- Non-breaking space between a value and its unit.
- Setup no longer blocked by messengers still loaded in the background.
- Upgrading no longer aborts while the shell is running.
- The dashboard now shows the display name chosen at install (it used to show the Windows account name).
- Interface strings completed in English and Spanish; the dashboard calendar follows the app language.
