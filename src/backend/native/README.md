# Composants natifs précompilés

Ces trois binaires accompagnent `Panneaux.Glass.exe` mais ne sont pas produits par
`dotnet publish`. Ils sont versionnés tels quels pour que `installer/build.ps1` donne un
setup identique sans dépendre d'une chaîne MSVC.

| Fichier | Rôle | Source |
|---|---|---|
| `GlassBackdrop.dll` | Flou et matériau DWM pour les surfaces de verre | `../GlassBackdrop.cpp`, à recompiler avec `../build-backdrop.cmd` |
| `SECOURS_BUREAU.exe` | Masque ou rétablit les icônes du Bureau | binaire autonome, pas de source dans ce dépôt |
| `RETABLIR_BARRE_WINDOWS.exe` | Réaffiche la barre des tâches Windows | binaire autonome, pas de source dans ce dépôt |

`GlassBackdrop.dll` se reconstruit à partir du `.cpp` fourni :

```cmd
cd src\backend
build-backdrop.cmd
```
