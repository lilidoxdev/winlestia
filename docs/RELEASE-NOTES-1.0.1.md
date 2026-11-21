# Winlestia 1.0.1

Bug-fix release on top of 1.0.0. The installer is still unsigned: SmartScreen shows a warning; choose "More info", then "Run anyway", and check the SHA-256 below.

## Fixes

- **Start menu:** smoother opening. Glass and window-shape updates are capped at one every 33 ms, the app search runs after the menu has opened, and a palette background stays under the menu while it animates.
- **Panels:** a palette background sits under panel content, so tabs no longer appear without a background.
- **Media pill:** collapses when a maximized window is in the foreground on that screen, instead of covering its title-bar buttons.
- **Power menu:** only one panel open at a time.
- **Lock screen:** the system-info block no longer overflows.
- **Apps search:** translated categories, empty sections hidden, untitled group shown as "Other".
- **Winjets:** built-in labels translated into French and Spanish.

## Improvements

- Dashboard: tabs preloaded outside the opening animation, performance gauges drawn as vector shapes, audio visualizer started after the panel has opened.
- Panel animation profiling with `WINLESTIA_PROFILE_PANELS=1` (one line per animation in `frontend.log`).

## Known issue

- The first opening of a panel after start-up is slower than the following ones.

## Install

Download `WinlestiaSetup.exe` below and run it. Updating from 1.0.0: run the new installer over the existing install.

SHA-256 (`WinlestiaSetup.exe`):

```
5D04712FFE6FA79D99CE09B4E4FCB615FC638E990A3D65773B260332AABB0714
```

Verify in PowerShell:

```powershell
Get-FileHash .\WinlestiaSetup.exe -Algorithm SHA256
```
