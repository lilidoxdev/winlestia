# Security policy

## Supported versions

| Version | Supported |
|---|---|
| 0.9.x | Yes |
| Older | No, please update |

## Reporting a vulnerability

Please report privately and do not open a public issue. Use GitHub's private reporting: on the [repository Security tab](https://github.com/lilidoxdev/winlestia/security/advisories/new), choose "Report a vulnerability". Include the version, Windows build, steps to reproduce and the impact you see. This is a volunteer project; expect an acknowledgement but no guaranteed timeline.

## In scope

- The backend, the Qt frontend, the `winlestia` command and its named pipe (it is meant to be reachable only by the current user, with an allow-list of commands).
- The installer and uninstaller, including the scheduled task, PATH entry and terminal integration.
- Handling of sensitive data: for example the Wi-Fi password path, logs, clipboard history.
- Anything that could leave a system without its taskbar, or that bypasses the restore paths.
- Supply-chain issues with what the installer downloads (Clink, pinned by SHA-256) or bundles.

## Privacy

- Weather: with no city set, the location is found from your IP address through wttr.in (which sees the address); the forecast comes from Open-Meteo.
- After uninstalling, `HKCU\Software\Winlestia\DisplayName` and `%LOCALAPPDATA%\Panneaux` are kept for later updates. Delete the folder and run `Remove-Item -Path HKCU:\Software\Winlestia -Recurse` to remove them.

## Known and out of scope

- **The binaries are not code-signed.** SmartScreen warnings are expected. Users should verify the SHA-256 published with each release. A report that "the installer is unsigned" is not a vulnerability.
- Vulnerabilities in Windows, Qt, .NET or other third-party components themselves; report them upstream (tell us if Winlestia ships a vulnerable version).
- Issues requiring an attacker who already runs code as the same user with full access to their profile.
- Denial of service by deliberately overloading your own desktop.
