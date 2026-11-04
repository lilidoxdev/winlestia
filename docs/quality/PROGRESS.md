# Quality pass — progress log

Branch `quality/community-fixes`,
based on `6b1ba4a` (= tag `v0.9.0` + README-only commits; no `src/` or `installer/` difference).
The installed build reports `0.9.0+a41b13b` (tag `v0.9.0`). `winlestia-0.9.0-clean` is an older
ancestor (`27e07f6`) and is not used. Nothing is pushed.

## Baseline (2026-11-04)

| Language | Files | Lines | Lines > 120 | > 300 | > 1000 |
|---|---|---|---|---|---|
| C# | 183 | 18,234 | 4,150 | 645 | 32 |
| QML | 142 | 11,764 | 900 | 69 | 0 |
| Python | 72 | 8,339 | 1,131 | 74 | 0 |
| PowerShell | 16 | 1,071 | 144 | 5 | 0 |

- C# namespaces: `PanneauxGlass` 129 files, `WinlestiaFiles` 25, `Winlestia.Shared` 3, `WinlestiaCli` 1, none 25.
- 226 empty `catch` blocks, 65 direct `File.WriteAll*` calls, 67 timers, ~14 `async void` candidates.
- No `.editorconfig`, no analyzer configuration, no CI workflow.
- Worst files: `QtShellBridge.cs` (line of 4,775 chars, 105 command cases), `Coordinator.cs` (3,324),
  `Visuals.cs`, `Models.cs`, `frontend.py`.
- 30 generated artefacts (`performance-*.json`, `verification-*.json`, …) are tracked in `src/backend`.
- Tests (25 console projects, 90 s cap each, script `run-tests.ps1` pattern): 24 report all checks
  passing; `FullscreenWatcher` does not finish within 90 s (pre-existing, to investigate).
  Backend Release build: 8.7 s, 0 errors.
- Toolchain: SDK 8.0.407 only. Backend/Files published self-contained (`build.ps1`), so target PCs need no runtime.

## Plan (separate commits, in this order)

1. **Fix — media widget (#1)**: session identity, AUMID display name, browser audio-session matching,
   visualizer target, live position. Pure functions + tests.
2. **Fix — social rail reserves space (#2)** and **dashboard reopens on last tab** (session memory).
3. **Fix — installer in English** with automatic language detection (EN fallback, FR, ES).
4. **Security hardening** (no confirmed vulnerability; see `SECURITY-AUDIT.md`).
5. **Structure (behaviour-preserving)**: `.editorconfig`, analyzers, shortcut-name normalisation module,
   splitting the widest files, removing tracked artefacts, contributor docs.
6. **Migration .NET 8 → .NET 10** (separate commit; needs the .NET 10 SDK).
7. Build + local installer, verification matrix.

## User-visible changes

- Bookmarks with file: URIs no longer open (only http/https).

## Log

- 2026-11-04: audits done (architecture, security, media, social rail/top bar, .NET/installer).
- 2026-11-04: committed ef8a438 social rail + last tab, b27b22c installer EN/auto-detect, ebac43b media #1 (61 tests)
- 2026-11-04: lot 4 security hardening (see SECURITY-AUDIT.md)
- 2026-11-04: 5f2c24a shortcut-name normaliser (43 tests, PowerShell parity)
- 2026-11-04: b7220cb docs, .editorconfig, 31 generated artefacts untracked
- 2026-11-04: 659b862 QtShellBridge split into partial files (token equivalence)
- 2026-11-04: 8857ab3 Coordinator/Models/Visuals split (token equivalence, Preferences JSON order kept)
- 2026-11-04: 17cbda7 installer ships Caelestia licence and notices
- 2026-11-04: build + 27 test projects pass; installer Winlestia-Setup-0.9.0.exe built; see MATRIX.md (.NET 10 not done)
