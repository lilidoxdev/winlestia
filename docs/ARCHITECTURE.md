# Architecture de Winlestia

Vue d'ensemble pour s'orienter dans le dépôt. Les détails vivent dans les documents cités en fin de page.

## Processus

| Processus | Source | Rôle |
|---|---|---|
| Glass (backend) | `src/backend/` (`Panneaux.Glass.exe`, C# .NET 8, WPF) | Chef d'orchestre : réglages, barre des tâches, dock, tuilage, audio, fond d'écran, télémétrie. Il est l'unique propriétaire de l'état. |
| Frontend | `src/frontend/` (PySide6 / QML) | Interface (tableau de bord, palette, réglages). Lancé et surveillé par le backend ; il n'a aucun droit sur le bureau. |
| Garde (watchdog) | `Panneaux.Glass.exe --taskbar-guard <pid> <fichier>` | Processus séparé qui rétablit la barre Windows si le backend s'arrête ou plante. |
| Secours | `SECOURS_BUREAU.exe` (`Secours.cpp`) | Outil indépendant (Ctrl+Alt+Retour arrière) qui arrête Winlestia et rend un bureau normal. |
| Winlestia Fichiers | `src/files/` (WPF) | Gestionnaire de fichiers. Préchargé masqué par le backend, joint par tube nommé (`src/backend/Files/`). |
| CLI `winlestia` | `src/cli/` | Petite commande qui parle au backend par le tube `WinlestiaCli` (`Bridge/CliServer.cs`). |
| Synchro terminal | `src/terminal/` (`sync_theme.py`) | Service local qui relit les réglages et le fond d'écran toutes les quatre secondes et applique la palette à Windows Terminal et à CMD. |

Instance unique : un mutex `Local\PanneauxGlassV5` et des événements nommés (afficher, quitter, restaurer la barre) permettent aux relances de signaler l'instance déjà active.

## Flux de données

1. `Program.Main` lit la ligne de commande une seule fois et fabrique un `RunOptions` immuable (mode de lancement normal, revue ou vérification).
2. Le `Coordinator` charge les réglages (`Store`, `Core/Models.cs`), crée les fenêtres WPF et, en mode Qt, le `QtShellBridge`.
3. Le pont démarre le frontend (rattaché à un Job Object : il meurt avec le backend) et ouvre le tube `PanneauxCaelestia`.
4. Le frontend envoie `{command, args}` ; le pont répond par l'état complet, les évènements en file, un éventuel message et des listes ponctuelles.
5. Chaque commande est traitée par une méthode `[BridgeAction("nom")]` (`Bridge/QtShellBridge.Actions.*.cs`). Un test de contrat vérifie que le schéma et les handlers coïncident.
6. Dans l'autre sens, le backend pousse des évènements (`Send("…")`) que le frontend récupère à son prochain échange ; le minuteur de 500 ms échantillonne audio, réseau et télémétrie seulement quand une page les affiche.
7. Les réglages persistent dans `%LOCALAPPDATA%\Panneaux` ; les outils externes (terminal, Fichiers) les relisent.

## Découpage des gros fichiers

`Coordinator` et `QtShellBridge` sont des classes partielles découpées par domaine : `Coordinator.<Domaine>.cs` (Shell, Tools, Views, Layout, Boards, Preview, Palette, Feedback, Dialogs, Desktop, Catalogue) et `QtShellBridge.<Domaine>.cs` (Frontend, Pipe, Cli, Sampling, Icons, State, Actions). Aucun fichier `Coordinator.*` ne dépasse 350 lignes ; les fichiers sources restent sous 500 lignes.

Le Coordinator ne fait plus qu'orchestrer. Les collaborateurs vivent dans `Core/Services/` derrière des interfaces :

- `ShellState` : état partagé (ouvert, replié, détaillé, dialogue ouvert, épinglé…) lu par le Coordinator, le dialogue et la boucle d'horloge.
- `IDialogService` (`Scope`, `ScopeAsync`, `Layers`, `Confirm`, `Message`, `PickFiles`, `PickFolder`) : portée des dialogues modaux.
- `ILaunchService` (`Launch`, `Reveal`, `LaunchTarget`, `LaunchShortcut`, `WebSearch`, `AddPath`, `Rescue`) et `LaunchRules` (décisions pures).
- `ITickController` (`Tick`) : poignée latérale et repli automatique ; `TickRules` garde les seuils.
- `IShortcutBindings` : libellés, états, réaffectation et aiguillage des raccourcis globaux (`Resolve` renvoie un `HotkeyAction`).
- `QuickTabRules` (cibles `glass:`), `LayoutRules` (disposition des panneaux) et `ShellChrome` (construction de la poignée et de la barre d'outils).
- `ICatalogueService`, `IDesktopIconsService`, `ITrayService`, `IPreviewExportService` : catalogue, icônes du Bureau, zone de notification, export d'aperçu.

Les règles pures (`*Rules`) sont testées dans `tests/Winlestia.Tests` sans fenêtre ni processus.

## Winjets (frontend)

Spécification : `docs/WINJETS-DESIGN.md`. Le backend publie `state.winjets` ; le frontend ne fait que l'afficher et renvoyer des actions `winjets.*`.

- `bridge_winjets.py` : objet `Bridge.winjets` (seule propriété ajoutée au Bridge). Fonctions pures testées : clé d'écran, géométrie relative à la zone de travail, rééchelonnage et clamp, repli sur l'écran principal (`recovered`), première place libre, aimantation (`snap_move`, `snap_resize`, seuil 8, grille 8, marge 16).
- `winjet_expr.py` : gabarits `{{ }}`, filtres et conditions, sans `eval` ni `Qt.createQmlObject` : le contenu d'un paquet n'est jamais du code.
- `desktop_layer.py` (ctypes) : style `WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE`, fond de pile tenu par `WM_WINDOWPOSCHANGING` (filtre natif), `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` pour « Afficher le bureau », réapplication au redémarrage d'Explorer et aux changements d'écran. Ni minuteur de scrutation ni hook souris. `desktop_glass.py` pose le verre natif par carte.
- `qml/winjets/` : `WinjetLayer` (une `WinjetWindow` par instance, `EditOverlay` par écran en édition), `WinjetCard` (voile de palette, liseré, habillage d'édition), `WinjetRenderer` (choix de la mise en page selon la taille réelle) et `BlockNode` (types de blocs d'une liste fermée, chargés par URL). Réglages d'une instance : `WinjetSettingsForm`, dans Réglages > Winjets (`settings/PageWinjets.qml`).
- Tests : `tests/test_winjet_expr.py`, `tests/test_winjet_geometry.py`, `tests/test_winjets_qml.py` (hors écran, vrais paquets).

## Où lire la suite

- Carte des dossiers du backend : `src/backend/README.md`.
- Contrat du pont : `docs/BRIDGE.md` et `docs/bridge-schema.json` (test : `tests/Winlestia.Tests/BridgeContractTests.cs`).
- Règle de threading (thread UI, tâches, sondes) : `docs/THREADING.md`.
- Restauration de la barre des tâches, du bureau et du terminal : `docs/RESTORE.md`, `docs/TROUBLESHOOTING.md`.
- Explorateur natif : `docs/NATIVE-EXPLORER.md`. Raccourcis : `docs/SHORTCUTS.md`.
- Tests et commandes de vérification : `docs/TESTS.md`.
- Installation et désinstallation : `docs/INSTALL.md`.
