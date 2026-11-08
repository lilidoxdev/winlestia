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

### Couverture

La CI collecte la couverture avec coverlet (`coverlet.collector`) et verifie un plancher (etape « Resume de couverture » de `.github/workflows/ci.yml`). Valeur mesuree le 2026-11-09 : 17,20 % des lignes, plancher 17 %. Le backend etant compile dans l'assembly de test, `tests/coverage.runsettings` active `IncludeTestAssembly` et exclut les fichiers de tests.

```powershell
dotnet test tests/Winlestia.Tests -c Release --filter "Category!=Desktop" --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings --results-directory TestResults
```

Le rapport est `TestResults/<guid>/coverage.cobertura.xml`. Le code WPF/shell non testable sans bureau pese sur le chiffre.

Regle du cliquet : le plancher vaut toujours `floor(couverture mesuree)` (partie entiere, variable `$floor` de l'etape CI). Il ne baisse jamais ; une PR qui fait monter la couverture d'un point entier releve le plancher dans la meme PR, avec la valeur mesuree datee ici et dans le commentaire de `ci.yml`. Une baisse sous le plancher exige des tests, pas un plancher plus bas.

### Style et fumee de l'installateur

Le job `lint` de la CI echoue sur toute ligne de plus de 140 colonnes (`python tools/check_line_length.py`, perimetre : tous les `*.cs`, `*.py`, `*.qml`, `*.ps1` suivis par git, hors `src/terminal/clink`, `VsCodeTheme*` et fichiers generes) et sur `dotnet format whitespace --verify-no-changes` pour les trois projets. Le job `installer-smoke` execute `installer/build.ps1 -CI` (composants natifs MSVC remplaces par des fichiers vides) et publie le setup en artefact `winlestia-setup-smoke`.

### Winlestia Fichiers (harnais)

`tests/FilesHarness/Run-Files.ps1 -Mode <mode>` est le seul lanceur autorise des auto-tests de Winlestia Fichiers (processus plafonne par un Job Object : 800 Mo, 4 processus, delai <= 120 s, 4 Go de RAM libre requis). Les auto-tests (`ops`, `dnd`, `refresh`, `states`, `palette`, `handler`, `tabs`, `idle`...) sont absents des builds Release : il faut d'abord compiler avec `dotnet build src/files/Winlestia.Files.csproj -p:SelfTest=true` (constante `WINLESTIA_SELFTEST`), puis passer `-Exe` si l'executable n'est pas au chemin par defaut. Hors CI, a lancer a la main uniquement.

## Python (pytest)

```powershell
pip install -r src/frontend/requirements.txt pytest
pytest -q
```

`pytest.ini` collecte `tests/`, `src/frontend/tests/` et `src/terminal/tests/` (fichiers `test_*.py`). `conftest.py` force `QT_QPA_PLATFORM=offscreen`. Les scripts `check_*.py` / `render_*.py` restent des outils manuels ; `tools/prefs_shots.py` prend des captures et n'est pas un test.

Les deux suites tournent en CI (`.github/workflows/ci.yml`). `Winlestia.sln` regroupe les projets `src/` et les tests.

## Avant une release

Etape manuelle, a faire par un humain sur un poste de bureau Windows 11 interactif, avant de tagger une version :

```powershell
dotnet test tests/Winlestia.Tests --filter "Category=Desktop"
```

Ces tests (audio, explorateur, icones de dossiers, tiling, bureaux virtuels, interop native, plein ecran) exigent une session de bureau reelle : fenetres visibles, shell Explorer, peripherique audio, plusieurs bureaux virtuels. Les runners GitHub Actions n'ont pas de session interactive, donc la CI les exclut (`Category!=Desktop`) et ne peut pas les remplacer. Fermer le shell Winlestia avant de lancer, puis consigner le resultat dans la PR de release.

Dernier passage : 2026-11-08, Windows 11 26200, shell Winlestia lance, barre des taches en masquage automatique : 7/7 reussis.
