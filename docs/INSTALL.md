# Installing Winlestia

This guide is for people who just want Winlestia running, with no technical background required.

## What you need

- Windows 11, 64-bit (x64). The installer refuses to run on anything older than Windows 11.
- Nothing else to install. The setup brings its own runtimes.
- About a minute. No administrator rights are needed: Winlestia is installed for your user account only, in `%LOCALAPPDATA%\Programs\Winlestia`.
- An Internet connection only if you tick the optional terminal integration (see below).

## 1. Download

Go to the [Releases page](https://github.com/lilidoxdev/winlestia/releases/latest) and download `Winlestia-Setup-0.9.1.exe`, the installer. The SHA-256 used to verify it is in the [v0.9.1 release notes](https://github.com/lilidoxdev/winlestia/releases/tag/v0.9.1).

## 2. Verify the download (recommended)

The installer is not code-signed (see the next section). A checksum lets you confirm the file you received is the one that was published and was not altered or truncated.

1. Open PowerShell (Start menu, type `PowerShell`).
2. Run, adjusting the folder if you did not save to Downloads:

   ```powershell
   Get-FileHash "$env:USERPROFILE\Downloads\Winlestia-Setup-0.9.1.exe" -Algorithm SHA256
   ```

3. Compare the `Hash` shown by PowerShell with the SHA-256 in the [release notes](https://github.com/lilidoxdev/winlestia/releases/tag/v0.9.1). It must match exactly (letter case does not matter).

If the values differ, do not run the file. Download it again, and open an issue if it still differs.

## 3. SmartScreen will probably warn you

The Winlestia binaries are **not signed** with a code-signing certificate. Windows SmartScreen therefore shows "Windows protected your PC" for a file it has not seen widely. This is expected, and it does not by itself mean the file is harmful. It does mean Windows cannot tell you who published it, which is why the checksum step above matters.

If the checksum matched and you want to continue:

1. Click **More info**.
2. Click **Run anyway**.

## 4. Run the setup

The setup wizard is in English by default and picks French or Spanish when Windows uses one of them. The steps are:

1. **Welcome** and licence (GPL-3.0).
2. **Tasks** (checked on first install unless noted):

   | Option | What it does |
   |---|---|
   | Start Winlestia at sign-in | Writes an `HKCU` Run value named `Panneaux`. On the first shell start it is migrated to a per-user scheduled task, `WinlestiaShell`, and the Run value is removed. No admin needed. |
   | Desktop shortcut | Adds a Winlestia icon to the Desktop. |
   | Start menu entry | Adds Winlestia, "Restaurer la barre des tâches Windows" and an uninstall entry. |
   | `winlestia` command in PATH | Lets you use the command line tool (`winlestia status`, and so on). |
   | Terminal integration | **Unchecked by default.** Applies the Winlestia colours to Command Prompt through Clink and adds PowerShell profile entries. Reversible. Needs an Internet connection: Clink 1.9.35 is downloaded from its official GitHub page and checked against a pinned SHA-256 before use. |

3. **Profile**: a display name shown inside Winlestia. It stays on your computer. It is pre-filled with your Windows user name.
4. **Install**, then optionally **launch Winlestia now**.

## 5. First launch

When Winlestia starts, the Windows taskbar is replaced by the Winlestia dock (this can be turned off in Settings). A few things to try:

- Move the pointer to the **bottom edge of the screen** to reveal the dock. The dock auto-hides: it slides away about half a second after the pointer leaves it. The Windows key still opens the normal Start menu.
- The default theme is **Porcelain** (light, warm); it is fixed and does not follow the wallpaper. The **Automatic** theme does: open Settings (Customize), then Appearance, then Theme, and select **Automatic**.
- **Ctrl+F2** opens the command palette.
- **Ctrl+F1** opens the dashboard.
- The full list is in [SHORTCUTS.md](SHORTCUTS.md).

If something looks wrong, jump to [TROUBLESHOOTING.md](TROUBLESHOOTING.md). The most important thing to know: **Ctrl+Alt+Backspace** is an emergency exit, and the Start menu has a "Restaurer la barre des tâches Windows" shortcut.

## Updating from an older version

Run the new `Winlestia-Setup-0.9.1.exe`. The setup detects the existing install and offers to update (or reinstall, or uninstall). It closes the running shell itself before replacing files, so you do not need to quit Winlestia first. Your settings in `%LOCALAPPDATA%\Panneaux` are kept. If terminal integration was already enabled, it stays checked.

## Uninstalling

Either run the setup again and choose the uninstall option, or use **Settings, Apps, Installed apps, Winlestia**, or the "Désinstaller Winlestia" Start menu entry. The uninstaller stops the shell, removes the `WinlestiaShell` scheduled task, undoes terminal integration and the optional folder-handler registration, and restores the Windows taskbar before deleting the program files.

### What is kept

- Your profile and settings: `%LOCALAPPDATA%\Panneaux`. The folder keeps this name for historical reasons (it is the project's former name). Delete it yourself if you want a clean slate.
- The registry value `HKCU\Software\Winlestia\DisplayName`, also kept for later updates.
- Nothing else: the program folder `%LOCALAPPDATA%\Programs\Winlestia` is removed.

To remove both by hand after uninstalling, delete the profile folder and run `Remove-Item -Path HKCU:\Software\Winlestia -Recurse` in PowerShell.

If an older install was made as administrator, the uninstaller may ask for UAC confirmation.

To reset without uninstalling, see [TROUBLESHOOTING.md](TROUBLESHOOTING.md#resetting-your-profile).
