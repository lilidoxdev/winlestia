# Security audit - lot 4 (2026-11-04)

## Scope
C# backend (`src/backend`), file manager IPC (`src/files/SingleInstance.cs`, `src/backend/Shared/FilesPipe.cs`), Python frontend IPC client (`src/frontend/frontend.py`), terminal installer (`src/terminal/install.py`), backend installer script (`src/backend/Install.ps1`), and the community claim of "SQL injection".

## Method
Manual reading of the cited code (line numbers re-located with `rg`), targeted pure-function tests (`tests/SecurityHardening`), Release build of the backend. No dynamic testing, no fuzzing, no scheduled task or schtasks call was run. This is a point-in-time review; it does not prove the absence of other issues.

## SQL injection claim: false positive
| Claim | Evidence read |
|---|---|
| Bookmark favicon lookup | `BookmarksCore.cs` `Query()`: the only `string.Format` argument is a constant clause (`=$u` or ` LIKE $u`); values go through `Parameters.AddWithValue("$u", ...)`. No user text reaches the SQL text. |
| Windows Search query | `FileSearch.cs` `TryBuild`: term is normalised, `NeutraliseContains` replaces `" * % _ [ ]` with blanks, `EscapeLiteral` doubles `'`; the profile path is fully-qualified-checked, NUL-checked and `EscapeLiteral`-ed. Characterised by tests (see S5). |
| WMI/WQL | `CpuTempChain.cs`, `OsdWatcher.cs`: query strings are constants. |
Result: not confirmed; no injectable sink found in the code read.

## Findings
| ID | File:line | Scenario | Impact | Status | Fix | Test |
|---|---|---|---|---|---|---|
| S1 | `BookmarksService.cs` Open (~149) | Imported/edited bookmark with `file://server/share/...` or `file:///...` passed to the browser | file: was accepted; a UNC host makes Windows authenticate to a remote SMB server, local files open in browser. Requires a malicious bookmark already in the user's browser profile. Low | Hardening | New `BookmarkUri.IsOpenable` (http/https only). file: was not documented as a feature; the only caller is `QtShellBridge.cs:205` (`bookmarksOpen`). User-visible behaviour change: `file:` bookmarks previously opened and are now refused. about:/edge: were never accepted, left refused | `tests/SecurityHardening` (javascript:, file:, UNC, ms-settings:, data:, malformed, http, https) |
| S2 | `AutostartTask.cs` Create (~29) | Task XML written to shared `%TEMP%` then read by `schtasks /Create /XML` | Predictable-window swap by another local process of the same user (a different user cannot write into this user's `%TEMP%` by default ACL, but the pattern is fragile). Low | Hardening | XML now written to `%LOCALAPPDATA%\Panneaux\tmp\winlestia-task-<guid>.xml` (created if missing), deleted in `finally`. Task name and XML content unchanged | `AutostartPolicy.PrivateTaskFile` tests (location, unique, not under `%TEMP%`). `schtasks` itself NOT run |
| S3 | `src/terminal/install.py:58-65` | Script path taken from the Run registry value is embedded in a single-quoted PowerShell literal for `-EncodedCommand`; only `'` was doubled, but PowerShell also treats U+2018/2019/201A/201B as quote delimiters | A path containing a typographic quote would break out of the literal (also a functional bug for user names like O'Brien with a curly quote). The value is in HKCU, so exploiting it already needs same-user write access. Low | Hardening | All four typographic quotes are now doubled too | Not covered by an automated test (no Python test harness for install.py); regex checked by hand (`'` and U+2019 are doubled) |
| S4 | `src/backend/Install.ps1` | Search for `Invoke-Expression`, `-EncodedCommand`, user-controlled strings | Only fixed paths and `Start-Process` with constant arguments found in lines matched | Not confirmed | none | n/a |
| S5 | `FileSearch.cs:39-58` | Inputs `a' OR 1=1 --`, `"x" OR "y"`, `100%_[a]*`, profile path with `'` | Stay literals inside the CONTAINS string / SCOPE string | False positive | none (behaviour unchanged; helpers already `internal`) | `tests/SecurityHardening` characterisation (8 checks) |
| S6 | `CliServer.cs:23-26`, `CliCommands.cs:44-86` | Other local users or processes driving the CLI pipe | DACL holds one entry (current user SID); bounded line read; command whitelist | Not confirmed | none | existing `tests/CliCommands` |
| S7 | `SingleInstance.cs:27-29,75-100`, `FilesPipe.cs` | Foreign client on the Files pipe | `CurrentUserOnly`, 64 KiB buffer, 2 s limit, 16 handlers, JsonException swallowed; request only opens folders/args via the app's own handler | Not confirmed | none | n/a |
| S8 | `QtShellBridge.cs:124` | Qt bridge pipe `PanneauxCaelestia` | `CurrentUserOnly`; `JsonDocument.Parse` in try/catch; `switch(command)` dispatch. Observation: the 131072-char limit is applied after `ReadLineAsync` returns, so a same-user peer could send one huge line and consume memory. Same-user only, so no privilege boundary crossed | Hardening | none made | n/a |
| S9 | `src/frontend/frontend.py:78-90` | Python client of the bridge | Client reads with `readline(8 MiB)` and `json.loads` inside `except (OSError, ValueError)`. The client does not verify the server's identity (pipe name squatting by a same-user process) | Not confirmed | none | n/a |
| S10 | repo-wide | `BinaryFormatter`, `shell=True` | none found by search | Not confirmed | none | n/a |

## Not reviewed
- `Install.ps1` was only searched for dangerous constructs (`Invoke-Expression`, `-EncodedCommand`, `$env:`, `Start-Process`), not read in full; `installer/*.ps1`, `Winlestia.iss` likewise not audited.
- The body of `QtShellBridge.Handle` (each command's argument handling), `Social*`, `WebIcons`, WebView2 pages (`assets/web`), the Whisper/voice code, the native DLLs, `src/files` beyond the pipe, `src/cli`.
- Python frontend beyond the IPC client; QML.
- Third-party dependencies and the Clink download path (hash pinned in `install.py`; not re-verified).
- Browser profiles and cookies were deliberately not read; no secrets were opened.
- No dynamic, fuzz or penetration testing.
