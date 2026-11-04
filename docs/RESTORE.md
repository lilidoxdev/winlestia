# Getting the Windows taskbar back

Winlestia hides the Windows taskbar only while it runs with "Replace the taskbar" on. A separate watchdog process
restores it when the shell stops or crashes. If it is still missing, try these in order (more detail in
`docs/TROUBLESHOOTING.md`):

1. Wait a few seconds for the watchdog.
2. Press **Ctrl+Alt+Backspace**. The independent rescue tool stops Winlestia and brings the desktop back
   (`src/backend/Tools/GlobalShortcut.cs`, `src/backend/Secours.cpp`).
3. Run `RETABLIR_BARRE_WINDOWS.exe` from `%LOCALAPPDATA%\Programs\Winlestia\Glass-v5`.
4. From a terminal:
   `"%LOCALAPPDATA%\Programs\Winlestia\Glass-v5\Panneaux.Glass.exe" --restore-taskbar`
   (reads the saved lease and shows `Shell_TrayWnd` again; `TaskbarNative.RestoreSaved`).
5. Run `SECOURS_BUREAU.exe` (or `Panneaux.Glass.exe --restore`) if desktop icons are hidden too.
6. Last resort: restart Explorer (Task Manager, Run new task, `explorer.exe`), or uninstall Winlestia; the
   uninstaller restores the taskbar.

To stop Winlestia from starting at logon, delete its scheduled task: `schtasks /delete /tn WinlestiaShell /f`.
