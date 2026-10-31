# Composants natifs précompilés

Ces trois binaires accompagnent `Panneaux.Glass.exe` mais ne sont pas produits par
`dotnet publish`. Ils sont ignorés par git (`*.exe`, `*.dll`) et restent en local :
`installer/build.ps1` s'arrête avec un message si l'un d'eux manque. Les récupérer depuis
`%LOCALAPPDATA%\Programs\Winlestia\Glass-v5` ou les recompiler (MSVC requis).

| Fichier | Rôle | Source |
|---|---|---|
| `GlassBackdrop.dll` | Flou et matériau DWM pour les surfaces de verre | `../GlassBackdrop.cpp`, à recompiler avec `../build-backdrop.cmd` |
| `SECOURS_BUREAU.exe` | Masque ou rétablit les icônes du Bureau | `../Secours.cpp`, à recompiler avec `../build-rescue.cmd` (copier le résultat ici) |
| `RETABLIR_BARRE_WINDOWS.exe` | Réaffiche la barre des tâches Windows | `../TaskbarRescue.cpp`, à recompiler avec `../build-taskbar-rescue.cmd` (copier le résultat ici) |

`GlassBackdrop.dll` se reconstruit à partir du `.cpp` fourni :

```cmd
cd src\backend
build-backdrop.cmd
```
