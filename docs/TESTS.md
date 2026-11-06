# Tests

## .NET (xUnit)

Un seul projet : `tests/Winlestia.Tests`. Les sources du backend y sont compilees directement, donc les types `internal` sont testables.

```powershell
# Tests sans bureau (ceux de la CI)
dotnet test tests/Winlestia.Tests --filter "Category!=Desktop"

# Un seul test
dotnet test tests/Winlestia.Tests --filter "FullyQualifiedName~CliCommandsTests"
```

Les tests de categorie `Desktop` (`[Trait("Category","Desktop")]` : audio, explorateur, icones de dossiers, tiling, bureaux virtuels, interop native, plein ecran) ouvrent de vraies fenetres ou touchent au shell. Ne les lancez pas pendant que le shell Winlestia tourne : `dotnet test tests/Winlestia.Tests --filter "Category=Desktop"`, a la main uniquement.

`SettingsMigrationAndShortcutsTests` genere un fichier de preferences pre-refonte ; `WINLESTIA_LEGACY_PREFS` peut pointer vers une vraie sauvegarde.

## Python (pytest)

```powershell
pip install -r src/frontend/requirements.txt pytest
pytest -q
```

`pytest.ini` collecte `tests/`, `src/frontend/tests/` et `src/terminal/tests/` (fichiers `test_*.py`). `conftest.py` force `QT_QPA_PLATFORM=offscreen`. Les scripts `check_*.py` / `render_*.py` restent des outils manuels ; `tools/prefs_shots.py` prend des captures et n'est pas un test.

Les deux suites tournent en CI (`.github/workflows/ci.yml`). `Winlestia.sln` regroupe les projets `src/` et les tests.
