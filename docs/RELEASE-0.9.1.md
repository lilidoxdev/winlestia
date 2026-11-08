# Winlestia 0.9.1

Winlestia is a free, open-source desktop shell for Windows 11, bringing a Caelestia-inspired interface, dynamic themes, glass effects, a customizable dock and desktop tools. This release fixes the issues reported on 0.9.0, makes the installer available in English, French and Spanish, and moves the project to .NET 10.

## Fixes

- Media panel and visualiser (lilidoxdev/winlestia#1): browsers are shown under a readable name instead of an internal identifier, track information is re-read when it changes, the playback position advances while the dashboard is visible, and the audio visualiser follows the browser that is playing, including its child processes.
- Social rail (lilidoxdev/winlestia#2): the dashboard now moves aside by the width the rail covers and scales its panes, so the rail no longer hides the tabs. On a very small logical screen width, what still cannot fit overlays the dashboard.
- The dashboard reopens on the last tab used. This is kept for the session only.
- User data is written atomically (preferences, clipboard history, social counts, recent items, taskbar lease, tiler exclusions, Explorer appearance files, language, hidden wallpapers, weather city), so an interrupted write does not leave a truncated file.
- Preferences recovery: a corrupt preferences file is kept as `.corrupt.bak` and the settings are restored from `.bak`. Defaults are used only if both fail. A transient read error is retried once and is not treated as corruption.
- Bookmarks only open `http` and `https` addresses. The task XML is written to a private folder.
- Error handling, `async void` handlers and timers were reviewed; no crash path was found. A few swallowed errors remain and are listed in `docs/quality/MATRIX.md`.

## Installer

- The setup wizard is in English by default and detects the Windows display language. French and Spanish are kept.
- The Caelestia licence and notices are installed with the application.
- The installer is not yet code-signed. Windows may show a SmartScreen warning: choose **More info**, then **Run anyway**. Code signing is being looked into (SignPath Foundation offers free signing for open-source projects). Check the SHA-256 against the value in these release notes.

## Code quality

No behaviour change except where listed above.

- No line longer than 500 characters (before: 207 over 500, 29 over 1000). One-line loops went from 246 to 54, the remaining ones being collection initialisers.
- The backend is split into 12 area namespaces; the largest holds 20% of the files (before: one namespace held 81%).
- French text was removed from code, except data keys and patterns (about 140 sites: category ids, regular expressions, preference ids). Interface text is in `Views/Strings.cs` in French, English and Spanish; logs are in English. The language comes from `Caelestia/language.json`, then the system culture, then English.
- The application-name normaliser is an ordered rule table with a dictionary of known display names, with tests and a PowerShell parity check.
- CSharpier (print width 120) and ruff format the code. `.editorconfig` and analyzers are added, and CI runs the `StringsTable` test.
- Generated artefacts are no longer tracked. Architecture, build, Windows integration and restore documents were added under `docs/`.
- A reported SQL injection was investigated and no exploitable injection was found: bookmark queries use parameters and the Windows Search query escapes its literals. See `docs/quality/SECURITY-AUDIT.md`.

## Platform

- All projects target .NET 10 (Microsoft.Data.Sqlite 10.0.14). The installer stays self-contained, so no runtime has to be installed.

## Install

1. Download `Winlestia-Setup-0.9.1.exe` from the assets below.
2. In PowerShell: `Get-FileHash .\Winlestia-Setup-0.9.1.exe -Algorithm SHA256`. The value must match the installer SHA-256 below.

SHA-256 of `Winlestia-Setup-0.9.1.exe`: `93293e815af688fd6135e6f1cd271b5481475bd3d4ba2b90fdaebd294c208c51`
3. Run it. See the SmartScreen note above.

Full guide: [INSTALL.md](INSTALL.md).
