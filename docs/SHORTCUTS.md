# Shortcuts and command line

Everything below was checked against the source (`src/backend/GlobalShortcut.cs`, `src/backend/Models.cs`, `src/frontend/qml`, `src/backend/CliCommands.cs`). Global shortcuts can be reassigned in Settings, on the Keyboard shortcuts page (Modify, then press the new combination); Windows-reserved combinations and ones already used by another action are refused; the values shown are the defaults. If another application already owns a combination, Windows refuses it and Winlestia reports the conflict.

## Global shortcuts

| Shortcut | Action | Reassignable |
|---|---|---|
| Ctrl+F1 | Toggle the dashboard (pressing again closes it) | Yes |
| Ctrl+Alt+Space | Also opens the dashboard; always registered as a second way in | No |
| Win+Alt+S | Open Settings | Yes |
| Ctrl+F2 | Command palette | Yes |
| Ctrl+F3 | Screen capture | Yes |
| Ctrl+F8 | Keep the active window on top (pin) | Yes |
| Win+Alt+W | Wallpaper switcher. If Windows refuses the combination, Ctrl+Alt+W is the fallback | Yes |
| Ctrl+Alt+Backspace | Emergency rescue (see [TROUBLESHOOTING.md](TROUBLESHOOTING.md)) | No, reserved |
| Hold AltGr | Push-to-talk dictation. Off by default, enable it in Settings; the key is chosen in Settings, Media and audio, not on the Keyboard shortcuts page | Set elsewhere |

## The dock

The Win key opens the normal Windows Start menu; it does not reveal the dock. The dock auto-hides: it slides away about 0.45 s after the pointer leaves it, and appears when the pointer reaches the bottom edge of the screen.

Reassignable shortcuts must combine Ctrl and/or Alt (the wallpaper switcher and Settings also accept Win) with a letter, digit, Space or a function key (F12 excluded).

## Automatic tiling

Tiling is **off by default**. These keys are registered only while it is turned on (Settings, or the dock button). They are fixed for now and listed, read-only, on the Keyboard shortcuts page.

| Shortcut | Action |
|---|---|
| Ctrl+Alt+Arrows | Move to the neighbouring tile |
| Ctrl+Alt+Shift+Arrows | Swap with the neighbouring tile, across displays too |
| Ctrl+Alt+Shift+F | Take the window out of tiling, or put it back |
| Ctrl+Alt+Shift++ / - | Grow or shrink the active tile (20 to 80 %); the numeric keypad + and - also work |
| Ctrl+Alt+Shift+X | Exclude the active window's application, or include it again |

Dropping a window onto another tile with the mouse swaps them. Switching virtual desktops uses the standard Windows Win+Ctrl+Arrows.

## While the dashboard window is open

| Shortcut | Action |
|---|---|
| Ctrl+H | Return to the dashboard |
| Ctrl+1 / 2 / 3 / 4 | Open the workspace browser on Applications / Games / Projects / Documents (these are not the dashboard tabs) |
| Ctrl+F | Focus the search field in a workspace |
| Esc | Close the panel, clear a search, or go back |

Ctrl+H and Ctrl+1 to 4 work only while the dashboard window is open, and not on the Settings page. On AZERTY layouts press **Ctrl+Shift+digit** instead (Ctrl+digit alone does nothing there; Ctrl+Shift+2 opens Games).

## Command palette (Ctrl+F2)

| Type | Result |
|---|---|
| `25/2`, `25:2` | Calculator. Needs an operator; French words such as "divisé par" and a decimal comma are understood |
| `>emoji cat` | Emoji picker; Enter copies |
| `>wallpaper` | Wallpaper picker |
| `>file name` | Search indexed files (Windows Search index) |
| plain text | Applications, open windows (up to 5), Windows Settings pages, session actions (shut down, restart, sign out, lock, sleep, hibernate; each asks for confirmation) |

## Wallpaper switcher

| Key | Action |
|---|---|
| Left / Right | Preview the previous or next image |
| Enter | Apply |
| Tab | With several displays, cycle between all displays and each one |
| Ctrl+O | Add images |
| Delete | Remove the selected image from the list |
| Esc | Close |

## Command line: `winlestia`

Available if you ticked the PATH option at setup (otherwise run `winlestia.exe` from `%LOCALAPPDATA%\Programs\Winlestia\Glass-v5`). It talks to the running shell through a local named pipe reserved to your user account. `help` works even when the shell is not running.

| Command | Does |
|---|---|
| `winlestia status` | Print the shell state as JSON |
| `winlestia panel <name>` | Open `settings`, `clipboard`, `tools`, `audio`, `mixer`, `capture`, `palette` or `dashboard` |
| `winlestia palette` | Open the command palette |
| `winlestia desktop next` / `prev` / `new` / `<1-20>` | Switch or create a virtual desktop |
| `winlestia wallpaper list` | List displays and any animated-wallpaper software running |
| `winlestia wallpaper <abs path> [--display <id>]` | Set an existing image, on all displays or one |
| `winlestia find windows` | Count open windows (no titles) |
| `winlestia find files <term>` | Count matches in the Windows index (no paths) |
| `winlestia brightness` | List displays and brightness as JSON |
| `winlestia brightness <id> <0-100>` | Set one display (DDC/CI) |
| `winlestia visualizer` | State of the dashboard audio bars |
| `winlestia quiet [on\|off\|auto]` | Show or force fullscreen quiet mode (not saved) |
| `winlestia wifi` | Wi-Fi state, read-only, no network names |
| `winlestia help` | Show usage |

Exit codes: `0` success, `1` refused by the shell, `2` unknown command or bad argument, `3` shell not running.
