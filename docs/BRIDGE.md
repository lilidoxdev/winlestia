# Pont backend C# <-> frontend PySide6

Contrat du canal entre `src/backend/Bridge/QtShellBridge*.cs` (serveur ; un handler `[BridgeAction("nom")]` par action, dans `QtShellBridge.Actions.*.cs`) et `src/frontend/bridge_client.py` (client). Version du protocole : **1** (`QtShellBridge.ProtocolVersion`, `bridge_client.PROTOCOL_VERSION`). La forme lisible par machine est `docs/bridge-schema.json` ; les tests `BridgeContractTests` (xUnit) et `tests/test_bridge_contract.py` (pytest) vérifient par extraction statique que chaque message émis ou traité y figure.

## Cadrage

- Canal nommé Windows `\\.\pipe\PanneauxCaelestia`, octets, un seul client, `CurrentUserOnly`.
- JSON UTF-8, **une ligne par message** terminée par `
`. Requête client > 131072 octets : connexion fermée ; réponse lue côté client jusqu'à 8 Mio.
- Mode **requête/réponse strict** : le client écrit une requête, le serveur répond exactement une ligne. Le serveur n'écrit jamais de lui-même ; les notifications passent par la file `events` de la réponse suivante.
- Le client interroge en boucle (`state` toutes les 180 ms visible, 500 ms sinon, 1 s en mode silencieux ; `visualizer` 55 ms ; `audioBars` 33 ms) et intercale les commandes utilisateur.

## Poignée de main / version

Pas de message dédié : chaque requête porte `args.protocol` (entier). Au **premier message de chaque connexion**, le serveur compare à `ProtocolVersion` et écrit un avertissement dans le journal (`Qt IPC: version de protocole différente ...`) en cas de divergence ou d'absence (client ancien). La connexion n'est jamais refusée ; les commandes inconnues sont ignorées silencieusement. Un client récent sur un serveur ancien ne reçoit rien de plus (champ ignoré).

## Requête (client -> serveur)

`{"command": nom, "args": {...}}`. Les champs d'`args` absents prennent la valeur par défaut du serveur (`""`, `0`, `false`). Le client fusionne dans **chaque** requête l'abonnement courant :

`pid:number, epoch:number, protocol:number, visible:boolean, page:number, dock:boolean, controls:boolean, mixer:boolean, wifi:boolean, bars:boolean`

`pid` (processus du frontend), `epoch` (compteur de navigation ; les évènements `close` d'une époque révolue sont écartés), `visible/page/dock/controls/mixer/wifi/bars` (ce que le frontend affiche, pilote l'échantillonnage). `seq` n'est utile qu'à `audioBars`.

Cas spéciaux traités avant le `switch` : `dictation` (renvoie `{"dictation": {state, level, levels, lang, text, error}}`), `audioBars` (`{"audioBars": {seq, idle, bars}}`), `visualizer` (`{"spectrum": [..]}`). Toutes les autres commandes renvoient la réponse générale.

### Commandes

| Commande | Champs `args` | Requis |
|---|---|---|
| `activate` | hwnd:number, id:string | hwnd |
| `addAccess` | board:string | - |
| `addFolder` | board:string | - |
| `audio` | - | - |
| `audioBars` | seq:number | - |
| `bookmarksBrowser` | id:string | - |
| `bookmarksDeleteInBrowser` | confirmSync:boolean, id:string | - |
| `bookmarksHide` | id:string | - |
| `bookmarksItems` | - | - |
| `bookmarksOpen` | id:string | - |
| `bookmarksRefresh` | ifDirty:boolean | - |
| `bookmarksUnhideAll` | - | - |
| `brightness` | id:string, value:number | - |
| `capture` | - | - |
| `captureExport` | path:string | path |
| `captureShortcutMode` | enabled:boolean | - |
| `clipboard` | - | - |
| `clipboardCopy` | id:string | - |
| `clipboardFiles` | paths:array<string> | - |
| `clipboardPin` | id:string | - |
| `copyText` | text:string | text |
| `customTheme` | colors:object, glass:boolean, opacity:number | - |
| `desktopClose` | index:number | - |
| `desktopNew` | - | - |
| `desktopSwitch` | index:number, relative:number | - |
| `deviceMute` | id:string, muted:boolean | - |
| `deviceSelect` | id:string | - |
| `deviceVolume` | id:string, value:number | - |
| `dictation` | - | - |
| `dictationCapture` | enabled:boolean | - |
| `dockApp` | action:string, confirmed:boolean, hwnd:number, id:string, index:number | - |
| `dockPreview` | cx:number, cy:number, dpr:number, id:string, x:number, y:number | - |
| `dockPreviewEnd` | now:boolean | - |
| `edit` | id:string | - |
| `editEntry` | category:string, id:string, name:string | - |
| `emptyRecycleBin` | - | - |
| `entryIcon` | id:string, path:string | - |
| `explorerFolder` | - | - |
| `explorerOpen` | - | - |
| `explorerPalette` | accent:string, background:string, text:string | - |
| `explorerReport` | - | - |
| `explorerRestore` | - | - |
| `favorite` | id:string | - |
| `fileCancel` | - | - |
| `fileOpen` | gen:number, index:number | index |
| `fileResults` | - | - |
| `fileSearch` | gen:number, term:string | term |
| `icon` | id:string | - |
| `launch` | id:string | - |
| `masterMute` | muted:boolean | - |
| `media` | action:string, value:number | - |
| `mediaVolume` | value:number | - |
| `microphone` | - | - |
| `move` | board:string, id:string | - |
| `openFiles` | - | - |
| `palette` | - | - |
| `paletteRun` | id:string, kind:string, query:string | - |
| `preference` | enabled:boolean, key:string, value:string | key |
| `profiles` | - | - |
| `quit` | - | - |
| `radio` | kind:string | - |
| `raiseMedia` | - | - |
| `recycleFiles` | paths:array<string> | - |
| `refreshCatalogue` | - | - |
| `restoreTaskbar` | - | - |
| `reveal` | id:string | - |
| `runningRefresh` | - | - |
| `sessionMute` | id:string, muted:boolean | - |
| `sessionVolume` | id:string, value:number | - |
| `settings` | section:string | - |
| `shortcut` | combo:string, id:any, key:number, kind:string, modifiers:number | - |
| `shortcut-reset` | id:string | - |
| `socialEnable` | enabled:boolean, id:string | - |
| `socialGlobalMute` | muted:boolean | - |
| `socialHistoryClear` | - | - |
| `socialLogin` | id:string | - |
| `socialLogout` | id:string | - |
| `socialMute` | id:string, muted:boolean | - |
| `socialOpen` | id:string | - |
| `socialSound` | enabled:boolean | - |
| `socialTestSound` | - | - |
| `socialVolume` | value:number | - |
| `state` | - | - |
| `systemAction` | confirmed:boolean, id:string | - |
| `theme` | dark:boolean, mode:string | - |
| `toolRun` | id:string | - |
| `tools` | - | - |
| `visualizer` | - | - |
| `volume` | value:number | - |
| `wallpaperApply` | display:number, path:string | path |
| `wallpaperCapture` | h:number, token:string, w:number, x:number, y:number | - |
| `wallpaperChanged` | - | - |
| `wallpaperPicker` | - | - |
| `website` | id:string | - |
| `websites` | - | - |
| `wifiConnect` | password:string, ssid:string | ssid |
| `wifiForget` | ssid:string | ssid |
| `wifiPortal` | - | - |
| `wifiSettings` | page:string | - |
| `windowList` | - | - |
| `workspace` | id:string | - |
| `zipFiles` | paths:array<string> | - |

### Clés de `preference`

`args.key` choisit la préférence ; `enabled` (booléen) ou `value` (nombre / chaîne) porte la valeur. Clés : `accentBorders`, `activeBorderOnly`, `autoTile`, `autostart`, `bookmarksRailVisible`, `clipboardHistory`, `clipboardImages`, `clipboardLimit`, `customGlassOpacity`, `desktopIndicator`, `desktopLogo`, `dictationEnabled`, `dictationKey`, `dockDrawerOpen`, `dockVisible`, `dockWidth`, `explorerTheme`, `filesDefaultHandler`, `filesPreload`, `frameThickness`, `glassOpacity`, `iconSize`, `motion`, `osd`, `quietFullscreen`, `reducedEffects`, `replaceTaskbar`, `screenFrame`, `socialBackground`, `socialRailVisible`, `taskbar`, `tileExclusionRemove`, `topActivationMargin`. Le détail des champs par clé est dans le schéma (`preferenceKeys`).

## Réponse générale (serveur -> client)

`{"state", "events", "notice", "wallpaperCapture", "bookmarksItems", "windows", "fileResults", "wallpaperPicker"}` ; les clés `null` sont émises telles quelles.

- `state` : objet instantané, clés de premier niveau : `systemRecent`, `wallpapers`, `wallpaperRevision`, `audioReadMs`, `audioMixer`, `quickTabs`, `social`, `bookmarks`, `settings`, `clipboard`, `diagnostics`, `dark`, `taskbarEnabled`, `networkKind`, `virtualDesktops`, `fullscreenMonitors`, `quiet`, `quietForced`, `performance`, `sysInfo`, `media`, `controls`, `apps`, `favorites`, `recent`, `websites`, `profiles`, `taskbarApps`, `runningApps`, `icons`, `iconRevision`. Le contenu des sous-objets est libre (lu avec `.get` côté Python, donc tolérant aux champs manquants) et n'est pas figé par ce contrat.
- `events` : tableau de chaînes, vidé à chaque réponse (32 maximum en file).
- `notice` : texte à afficher une fois (erreur ou confirmation).
- `wallpaperCapture`, `bookmarksItems`, `windows`, `fileResults`, `wallpaperPicker` : réponses ponctuelles aux commandes homonymes.

### Évènements (`events`)

Exacts : `toggle`, `access`, `media`, `performance`, `controls`, `palette`, `settings`, `settings:social`, `settings:bookmarks`, `clipboard`, `audio`, `microphone`, `tools`, `capture`, `capture-ocr`, `capture-select`, `wallpaper-switcher`, `close`, `bookmarks`, `social`, `files`, `dock`, `dictation:start`, `dictation:stop`, `dictation:keycancel`.
Préfixes (suffixe variable) : `workspace:`, `osd:volume:`, `osd:brightness:`, `shortcut-key:`, `shortcut-saved:`, `osd:mic:`, `dictation:key:` — `workspace:<id>`, `osd:volume:<n>:<muet>`, `osd:brightness:<n>`, `osd:mic:<n>:<muet>`, `shortcut-key:<mods>:<vk>`, `shortcut-saved:<kind>`, `dictation:key:<vk>`.
Évènements gérés côté client seulement (jamais émis par le serveur actuel) : `mixer`.

## Faire évoluer le contrat

1. Ajouter le message dans `docs/bridge-schema.json` et ce document.
2. Changer d'incompatibilité (champ retiré, sens modifié) : incrémenter `ProtocolVersion` et `PROTOCOL_VERSION` ensemble (un test vérifie qu'ils sont égaux au schéma).
3. Ajouter un champ optionnel ou une commande ne change pas la version.
