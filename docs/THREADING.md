# Règle de threading du backend

Dérivée du code (chemins relatifs à `src/backend/`). Trois familles de threads.

## 1. Thread UI (STA, Dispatcher WPF)

- `Core/Coordinator.cs` : `Program.Main` est `[STAThread]`. Toute fenêtre WPF, tout état partagé du shell (`Coordinator`, `QtShellBridge`) et toute mutation de collections vivent sur ce thread.
- `Bridge/QtShellBridge.cs` capture `Dispatcher.CurrentDispatcher` à la construction (champ `dispatcher`) et s'en sert pour revenir sur l'UI : `dispatcher.BeginInvoke(...)` (fire and forget) ou `dispatcher.InvokeAsync(...)` (quand on attend le résultat).
- Hors du pont, le même principe s'applique : `Application.Current.Dispatcher.BeginInvoke` dans `Core/Coordinator.cs` (fin des tâches de fond du catalogue) et `Invoke` dans `Bridge/QtShellBridge.cs` (`Fail`).
- Les minuteries d'interface sont des `DispatcherTimer` (ex. `timer` de `QtShellBridge`), jamais des `System.Threading.Timer`.

## 2. Tâches de fond (thread pool)

- Tout ce qui bloque (disque, COM, PowerShell, WinRT, réseau) part en `Task.Run`, jamais sur l'UI : `VirtualDesktops.*`, `RecycleActions.Zip`, `explorer.Sync/Restore`, `FilesDefaultHandler.Set`, la liste des fenêtres de la palette, etc. (`Bridge/QtShellBridge.cs`), ou `CatalogueTick` dans `Core/Coordinator.cs`.
- Le résultat revient à l'UI par le Dispatcher (point 1). Une tâche de fond ne touche jamais un objet WPF directement.
- Les commandes CLI (`Bridge/CliServer.cs`) sont traitées par `RunCli` ; la boucle `Serve` tourne en `Task.Run`, la réponse est bornée à 5 s (`WaitAsync`).

## 3. Lecture du tube du pont Qt

- `Bridge/QtShellBridge.cs`, méthode `Serve` (tube `PanneauxCaelestia`, `PipeOptions.Asynchronous`) : boucle `async` démarrée depuis le constructeur (`_ = Serve()`), sans thread dédié. `ReadLineAsync` attend sans bloquer.
- Règle : le lecteur du tube ne traite jamais une requête lui-même. Il parse le JSON, clone l'élément (`Clone()`), puis passe par `await dispatcher.InvokeAsync(() => Handle(element))` : `Handle` s'exécute donc toujours sur l'UI, et la réponse est écrite ensuite.
- Sens inverse (`Send`, appelé depuis l'UI) : file `events` bornée à 32 entrées + `EventWaitHandle` `Local\PanneauxCaelestiaWake`, que le frontend Qt attend de son côté.
- En fin de connexion, le nettoyage (`finally`) repasse aussi par `dispatcher.BeginInvoke`.

## Threads dédiés (exception)

- `Taskbar/FullscreenWatcher.cs` : un thread `IsBackground` nommé `FullscreenWatcher` porte les hooks `SetWinEventHook` (boucle de messages obligatoire) ; le recalcul tourne sur le thread pool.
- Processus frontend : sa sortie d'erreur est lue en événementiel (`BeginErrorReadLine`), et `Exited` revient sur l'UI via le Dispatcher.
