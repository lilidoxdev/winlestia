# Restauration de la barre des tâches Windows

Winlestia masque la barre Windows quand le dock Qt la remplace. Ce document fixe les invariants qui garantissent qu'elle revient, y compris après un arrêt brutal. Analyse faite par lecture du code ; rien n'a été exécuté.

## Acteurs

| Acteur | Fichier | Rôle |
|---|---|---|
| Bail `taskbar-session.json` | `Taskbar/TaskbarNative.cs` (`Snapshot`, `LeasePath`) | État d'origine de la barre (`State` : 1 = masquage auto, 2 = toujours visible) et barres visibles. Écrit dans `Store.Root` **avant** tout masquage (`ShellTaskbar.Enable`). |
| Gardien natif | `TaskbarRescue.cpp` -> `RETABLIR_BARRE_WINDOWS.exe --watch-parent <pid> <ready> <release>` | Processus séparé lancé par `Enable`. Attend la fin du parent (handle `SYNCHRONIZE`) ou l'événement `release`, puis restaure. |
| Gardien de repli | `Panneaux.Glass.exe --taskbar-guard <pid> <bail>` (`TaskbarNative.Guard`) | Même rôle si `RETABLIR_BARRE_WINDOWS.exe` est absent. |
| Restauration manuelle | `Panneaux.Glass.exe --restore-taskbar`, ou `RETABLIR_BARRE_WINDOWS.exe` lancé seul (raccourci du menu Démarrer, désinstallation) | Rétablit sans shell ni pipe. |

## Invariants

1. **Bail avant masquage.** `Enable` écrit le bail, démarre le gardien, attend son événement `ready` (4 s) et seulement ensuite appelle `HideExplorer`. Si le gardien ne démarre pas, la barre n'est jamais masquée (`Disable` + message).
2. **Arrêt brutal.** Le gardien attend le *handle du processus* parent : `TerminateProcess`, plantage ou arrêt par le gestionnaire des tâches le réveillent sans qu'aucun `finally` du parent ne s'exécute. Il relit `taskbar-session.json` (dossier de son exe, identique à `Store.Root`), applique `ABM_SETSTATE` puis `ShowWindow(SW_SHOWNA)` sur `Shell_TrayWnd` et `Shell_SecondaryTrayWnd`.
3. **Pas de dépendance au pipe ni au shell.** Ni le gardien ni `--restore-taskbar` n'utilisent le canal `PanneauxCaelestia` ni Qt. `--restore-taskbar` est traité dans `Program.Main` avant le mutex d'instance unique et avant tout démarrage de shell : il signale l'événement `Local\PanneauxGlassTaskbarRestore` (si une instance vit, elle bascule `SetTaskbar(false)`), puis applique `RestoreSaved` directement.
4. **Repli sans bail.** Bail absent ou illisible : le gardien natif et `--restore-taskbar` appliquent tous deux « toujours visible » (état 2) et réaffichent les barres. (Corrigé : `RestoreSaved` ne faisait rien dans ce cas, le gardien natif si.)
5. **Arrêt propre.** `Disable` signale `release`, attend le gardien (2,2 s) puis restaure lui-même ; l'arrêt du frontend seul (`SuspendTaskbar`) restaure sans modifier la préférence `ReplaceTaskbar`.
6. **Restauration idempotente.** Restaurer une barre déjà visible est sans effet ; le gardien peut donc s'exécuter après une restauration manuelle.

## Ctrl+Alt+Retour arrière

Ce raccourci global (`GlobalShortcut`, id 2) appelle `Coordinator.Rescue()` qui lance `SECOURS_BUREAU.exe` : il rétablit les **icônes du Bureau**, pas la barre des tâches. Il est enregistré par le processus shell : s'il est mort, le raccourci n'existe plus. Dans ce cas la barre est rétablie par le gardien (invariant 2) ; pour la forcer à la main : `RETABLIR_BARRE_WINDOWS.exe` ou `Panneaux.Glass.exe --restore-taskbar`.

## Limites connues

- Si le gardien lui-même est tué avant le parent, personne ne restaure : relancer `RETABLIR_BARRE_WINDOWS.exe`.
- Le gardien de repli (`--taskbar-guard`) retourne 2 sans restaurer si le bail est illisible ; seul le cas où le gardien natif est absent est concerné.
- Non vérifié par exécution : aucun test ne tue le processus réel (règle du dépôt : pas de manipulation du bureau).
