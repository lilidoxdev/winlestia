# App display-name normalisation

Single C# source of truth: `src/backend/AppNameNormalizer.cs` (`Normalize`). `CatalogueWatchRules.CleanName` is a thin wrapper.
PowerShell twin: `Clean-Name` in `src/backend/Inventory.ps1` (keep in sync; `tests/AppNameNormalizer/parity.ps1` compares both).

Rules apply in this order (all case-insensitive, culture-invariant), then the result is trimmed. Null gives "".

| # | Rule | Regex | Example |
|---|------|-------|---------|
| 1 | ExeShortcutSuffix | `\.exe\s*-\s*Raccourci$` | `Foo.exe - Raccourci` -> `Foo` |
| 2 | ShortcutSuffix | `\s*-\s*Raccourci$` | `Foo - Raccourci` -> `Foo` |
| 3 | LaunchPrefix | `^Lancer\s+` | `Lancer Foo` -> `Foo` |
| 4 | VlcExe | `^VLC Media Player\.exe$` | `VLC Media Player.exe` -> `VLC Media Player` |
| 5 | TrademarkSigns | `Â?®\|™` | `Foo™` -> `Foo` |
| 6 | Windows10Suffix | `\s*-\s*Windows10$` | `Foo - Windows10` -> `Foo` |
| 7 | CrimsonDesert | `^CrimsonDesert$` | `CrimsonDesert` -> `Crimson Desert` |
| 8 | HogwartsLegacy | `^HogwartsLegacy$` | `HogwartsLegacy` -> `Hogwarts Legacy` |

## Where used
- `AppCatalogueWatcher.cs` (via `CatalogueWatchRules.CleanName`): shortcut events and catalogue scans.
- `Inventory.ps1` `Clean-Name`: inventory of shortcuts and Epic pending installs.

## Adding a rule
1. Add a named `static readonly Regex` with an XML doc example in `AppNameNormalizer.cs` and apply it in `Normalize` (order matters).
2. Add the same `-replace` to `Clean-Name` in `Inventory.ps1` at the same position.
3. Add inputs to `Inputs` and recorded outputs to `Expected` in `tests/AppNameNormalizer/Program.cs` (same index).
4. Run `tests/AppNameNormalizer` (expects `TOTAL n/n`; parity prints `SKIP` if PowerShell is unavailable) and `tests/AppCatalogueWatch`.
