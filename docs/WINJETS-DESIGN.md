# Winjets — conception

Document de référence interne. Le format public destiné aux créateurs est décrit dans
`docs/winjets/FORMAT-v1.md`, qui ne doit jamais contredire ce document.

## 1. Références visuelles

| Image | Observé | Décision |
|---|---|---|
| 1 — carte « six programmes » | Carte verticale très arrondie, verre sombre, liseré 1 px ; visuel circulaire en matrice de points ; légende en petites capitales monospace ; six boutons pilule, un actif plein. | Un créateur donne une identité à sa carte (bloc `dots`, bloc `image`) et propose des interactions internes (bloc `buttons` qui modifie un état d'instance). L'état actif prend l'accent de la palette. |
| 2 — **référence prioritaire** | Cartes translucides posées sur le fond ; rayon ~20 px ; gouttière régulière ~16 px ; petits formats (horloge, calendrier, météo), format large (batterie), grande carte horizontale en haut au centre (médias). Titre de carte petit en haut à gauche, valeur principale grande, détails estompés. Le fond reste visible entre et à travers les cartes. | Grille d'ancrage 8 px, cellule de base 168 px, gouttière 16 px : tailles 1×1, 2×1, 4×1, 2×2. Verre DWM natif + voile de la palette. Chaque Winjet déclare des mises en page par palier de taille. Aucun fond plein derrière les cartes. |
| 3 — variante sombre | Fond quasi noir, cartes sombres avec liseré clair, texte blanc ; petits formats groupés à gauche, lecteur isolé à droite. | Texte secondaire `onSurfaceVariant` avec contraste ≥ 4.5:1 sur le voile ; liseré renforcé en sombre ; positions libres sur n'importe quel écran, l'aimantage aide sans imposer. |

Arbitrage : l'image 2 fixe composition, proportions et verre ; l'image 3 la lisibilité en sombre ; l'image 1 l'étendue de l'expression d'un créateur.

## 2. Architecture

Responsabilités (règle du projet : état et persistance côté backend, rendu côté Qt) :

- **Backend C# — `src/backend/Winjets/`** (namespace `PanneauxGlass.Winjets`)
  - `WinjetPackage` / `WinjetManifest` / `WinjetDefinition` : modèle du paquet, lecture JSON.
  - `WinjetValidator` : validation d'un ZIP ou d'un dossier, renvoie une liste d'erreurs lisibles en français.
  - `WinjetLibrary` : paquets inclus (lecture seule) et installés (`<data>\winjets\packages\<id>\`), import, mise à jour, désinstallation.
  - `WinjetInstances` : instances posées, persistées dans `<data>\winjets\instances.json` par `AtomicFile`.
  - `WinjetDataService` : seul accès réseau des Winjets. `HttpClient` partagé, domaine obligatoirement déclaré dans `manifest.permissions.network`, HTTPS seulement, cache disque `<data>\winjets\cache\`, intervalle minimal 60 s (clamp), taille de réponse ≤ 512 Kio, timeout 10 s, un seul appel en vol par source, repli sur le cache en cas d'échec.
  - Actions bridge et état publié (§4).
- **Frontend Qt — `src/frontend/qml/winjets/` + `src/frontend/bridge_winjets.py` + `src/frontend/desktop_layer.py`**
  - `WinjetLayer.qml` déclaré dans `Main.qml` : un `Repeater` crée une fenêtre `WinjetWindow.qml` par instance visible.
  - `WinjetWindow.qml` : fenêtre sans cadre, transparente, verre via `native_glass`, coins arrondis ; contient `WinjetCard.qml`.
  - `WinjetRenderer.qml` + `blocks/*.qml` : interprète la composition déclarée, choisit la mise en page selon la taille réelle.
  - `desktop_layer.py` (ctypes) : place les fenêtres au fond de la pile, sans activation, les garde visibles après « Afficher le bureau », les replace après redémarrage d'Explorer.
  - Mode **Modifier les Winjets** : déplacement, poignées, repères d'alignement sur une surcouche par écran qui n'existe que pendant l'édition.
  - `PageWinjets.qml` (Réglages) et entrée **Ajouter un Winjet** dans `DockMenu.qml`.
- **Paquets inclus — `src/winjets/builtin/<id>/`**, installés dans `{app}\Winjets\builtin\`.

Le contenu d'un paquet est traité comme des données : aucun QML, JavaScript, script ou exécutable n'est chargé depuis un paquet. Le renderer n'interprète qu'une liste fermée de blocs et d'expressions.

### Couche bureau

- Fenêtres `Qt.Tool | Qt.FramelessWindowHint | Qt.WindowDoesNotAcceptFocus`, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, placées `HWND_BOTTOM`. Aucun propriétaire n'est donné (un propriétaire Progman détruirait les fenêtres au redémarrage d'Explorer).
- `WM_WINDOWPOSCHANGING` est filtré pour maintenir le fond de pile hors mode édition.
- « Afficher le bureau » : un hook `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` (événementiel, pas de sondage) détecte le passage au premier plan de `WorkerW`/`Progman` et réaffiche les cartes au-dessus du bureau sans activation ; elles retournent au fond dès qu'une autre fenêtre passe au premier plan.
- Redémarrage d'Explorer : le backend relaie `TaskbarCreated` ; le frontend réapplique la couche.
- Écrans : la géométrie d'une instance est stockée en pixels logiques relatifs à la zone de travail de son écran, plus la clé de l'écran (nom + taille) et une position normalisée. Changement de résolution → repositionnement proportionnel puis clamp ; écran absent → l'instance passe sur l'écran principal, marquée `recovered`. La page Réglages propose « Ramener tous les Winjets sur l'écran principal ».
- Repos : aucune animation ni minuterie hors besoin. L'horloge rafraîchit à la seconde seulement si les secondes sont affichées, sinon à la minute.

## 3. Grille et tailles

- Unité logique = pixel indépendant du DPI (96 dpi).
- Cellule 168, gouttière 16 : 1 col = 168, 2 = 352, 3 = 536, 4 = 720 ; mêmes valeurs en hauteur.
- Aimantage : grille 8 px, bords et centres des autres cartes, bords de la zone de travail (marge 16) ; seuil 8 px ; repères affichés pendant le geste.
- `minSize` du paquet ≥ 120×120 imposé par le moteur ; `maxSize` ≤ 1600×1000.

## 4. Contrat bridge

État publié par le backend (`state.winjets`) :

```json
{
  "editMode": false,
  "packages": [ { "id": "", "version": "", "name": "", "author": "", "description": "",
                  "builtin": true, "dir": "C:\\...", "preview": "C:\\...\\preview.png",
                  "network": ["api.example.com"], "definition": { /* widget.json validé */ },
                  "manifest": { /* manifest.json validé */ } } ],
  "instances": [ { "id": "w-…", "packageId": "", "hidden": false,
                   "geometry": { "screen": "\\\\.\\DISPLAY1|2560x1440", "x": 0, "y": 0, "w": 352, "h": 168,
                                 "nx": 0.0, "ny": 0.0, "recovered": false },
                   "settings": {}, "state": {} } ],
  "data": { "<instanceId>": { "<sourceId>": { "value": {}, "updated": "ISO-8601", "error": null, "stale": false } } },
  "lastImport": { "ok": true, "packageId": "", "errors": [] }
}
```

Actions (`Bridge.action(name, args)`) :

| Action | Arguments | Effet |
|---|---|---|
| `winjets.inspect` | `path` | Valide un ZIP sans l'installer ; remplit `lastImport` avec l'aperçu (`preview` = paquet décompressé en zone temporaire). |
| `winjets.import` | `path` | Valide puis installe ou met à jour le paquet. |
| `winjets.uninstall` | `packageId` | Retire le paquet et ses instances (refusé pour un paquet inclus). |
| `winjets.add` | `packageId`, `screen?`, `x?`, `y?` | Crée une instance à la taille par défaut, à la première place libre de l'écran. |
| `winjets.remove` | `instanceId` | Retire l'instance. |
| `winjets.setHidden` | `instanceId`, `hidden` | Masque/affiche. |
| `winjets.setGeometry` | `instanceId`, `geometry` | Enregistre position et taille (le frontend clampe). |
| `winjets.setSettings` | `instanceId`, `settings` | Valide contre les réglages déclarés puis enregistre. |
| `winjets.setState` | `instanceId`, `key`, `value` | État d'interaction (bloc `buttons`). Clé déclarée seulement. |
| `winjets.setEditMode` | `enabled` | Entrée/sortie du mode édition. |
| `winjets.refresh` | `instanceId` | Rafraîchit les sources réseau en respectant l'intervalle minimal. |
| `winjets.recoverAll` | — | Ramène toutes les instances sur l'écran principal. |

Le backend publie aussi l'événement `desktop.explorerRestarted` et le compteur `state.desktop.explorerEpoch` (incrémenté à chaque redémarrage d'Explorer).

Précisions de l'implémentation :

- `lastImport` contient aussi `inspect` (vrai pour `winjets.inspect`), `updated` (mise à jour d'un paquet déjà installé) et `package` (même forme qu'un élément de `packages`, avec `dir` et `preview` dans la zone temporaire pour un aperçu).
- Les champs nuls sont omis du JSON : `data.<id>.<src>.value` est absent tant que rien n'a pu être lu, `error` est absent sans erreur.
- `geometry` suit les noms `screen, x, y, w, h, nx, ny, recovered` ; `winjets.setGeometry` borne `w`/`h` aux tailles min/max du paquet.
- Les propriétés à valeurs énumérées des blocs acceptent une valeur littérale ou une expression entière `{{ ... }}` ; le validateur n'en analyse pas le contenu (arguments de filtre compris).

## 5. Format Winjet v1 (normatif)

Un paquet est un ZIP (ou un dossier) contenant à la racine :

```
manifest.json      obligatoire
widget.json        obligatoire
preview.png        facultatif (≤ 1 Mo, ≤ 1600×1000)
assets/            facultatif : images et JSON de données locales
```

Limites : ZIP ≤ 5 Mio, décompressé ≤ 20 Mio, ≤ 200 entrées, profondeur ≤ 4, aucun chemin absolu, `..`, lien ou nom réservé Windows. Extensions autorisées : `.json .png .jpg .jpeg .webp .gif .txt .md`. JSON ≤ 256 Kio chacun, profondeur ≤ 32. Tout autre contenu fait échouer l'import.

### manifest.json

```json
{
  "format": "winjet/1",
  "id": "com.exemple.horloge",
  "name": "Horloge",
  "version": "1.0.0",
  "author": "Nom déclaré",
  "description": "Une phrase.",
  "license": "MIT",
  "homepage": "https://exemple.com",
  "compatibility": { "winlestia": ">=0.9.2" },
  "permissions": { "network": ["api.coingecko.com"] }
}
```

- `id` : `^[a-z0-9]+(\.[a-z0-9-]+){1,4}$`, 3 à 64 caractères. `version` : SemVer `x.y.z`.
- `permissions.network` : noms d'hôte exacts, sans joker ni schéma ; vide par défaut.

### widget.json

```json
{
  "size": { "default": [352, 168], "min": [168, 168], "max": [720, 352] },
  "settings": [
    { "id": "showSeconds", "type": "toggle", "label": "Afficher les secondes", "default": false },
    { "id": "city", "type": "text", "label": "Ville", "default": "Paris", "maxLength": 40 },
    { "id": "range", "type": "choice", "label": "Période", "default": "7d",
      "options": [ { "value": "1d", "label": "24 h" }, { "value": "7d", "label": "7 jours" } ] },
    { "id": "decimals", "type": "number", "label": "Décimales", "default": 2, "min": 0, "max": 6 }
  ],
  "state": { "mode": "face" },
  "data": {
    "btc": { "type": "file", "path": "assets/btc-demo.json" },
    "live": { "type": "http", "url": "https://api.coingecko.com/api/v3/...", "refresh": 300,
              "enabledWhen": "settings.source == 'live'" }
  },
  "layouts": [
    { "minWidth": 0,   "root": { "type": "column", "children": [ ] } },
    { "minWidth": 340, "root": { "type": "row", "children": [ ] } },
    { "minWidth": 520, "minHeight": 300, "root": { } }
  ]
}
```

- **Mise en page** : on retient la dernière entrée de `layouts` dont `minWidth`/`minHeight` sont satisfaits par la taille réelle de la carte.
- **Conteneurs** : `column`, `row`, `stack` (superposition), `grid` (`columns`). Propriétés communes : `gap`, `padding`, `align` (`start|center|end|stretch`), `grow` (poids), `visibleWhen` (expression).
- **Blocs** :
  - `text` : `text`, `style` (`title|label|body|caption|display|mono`), `align`, `maxLines`, `tone` (`primary|secondary|accent`).
  - `image` : `src` (chemin sous `assets/`), `fit` (`contain|cover`), `radius`.
  - `clock` : `variant` (`analog|digital`), `showSeconds`, `format` (`HH:mm`, etc.), `timezone` (IANA, facultatif).
  - `calendar` : `variant` (`month|day`), `firstDay` (`monday|sunday`).
  - `list` : `items` (expression vers un tableau), `title`, `subtitle`, `trailing` (champs de l'élément), `max`.
  - `chart` : `series` (expression vers un tableau de nombres ou de `[t, v]`), `variant` (`line|area|bars`), `axes` (bool), `period` (texte), `color` (rôle de palette).
  - `stat` : `value`, `label`, `delta` (nombre signé, coloré selon le signe).
  - `progress` : `value` (0..1), `variant` (`ring|bar`), `label`.
  - `dots` : `matrix` (expression vers un tableau 2D de 0..1) ou `pattern` (`pulse|ripple|torus` animé seulement si visible), `shape` (`circle|square`).
  - `buttons` : `options` (`[{ value, label }]`), `bind` (`state.<clé>`).
  - `spacer`, `divider`.
- **Expressions** : `{{ chemin | filtre:arg }}` dans les chaînes. Chemins : `settings.*`, `state.*`, `data.<source>.*`, `now`, `instance.width`, `instance.height`. Filtres : `number:n`, `currency:EUR`, `percent:n`, `date:motif`, `upper`, `lower`, `default:valeur`, `last`, `first`, `delta` (variation relative entre premier et dernier élément d'une série). Conditions (`visibleWhen`, `enabledWhen`) : comparaisons `== != < <= > >=`, `&&`, `||`, `!`, littéraux. Aucune autre évaluation.
- **Actions** (champ `action` sur `text`, `image`, `stat`, conteneurs) : `{ "type": "setState", "key": "", "value": "" }`, `{ "type": "refresh" }`, `{ "type": "openUrl", "url": "https://…" }` (hôte déclaré dans `permissions.network` ou `homepage`), `{ "type": "openSettings" }`.
- **Couleurs** : uniquement des rôles de palette (`accent`, `primary`, `secondary`, `tertiary`, `onSurface`, `onSurfaceVariant`, `positive`, `negative`). Aucune couleur littérale.

## 6. Paquets livrés

- `winlestia.horloge` (1×1 analogique → 2×1 numérique + date → 2×2 + fuseaux).
- `winlestia.calendrier` (1×1 jour → 2×2 mois).
- `winlestia.graphique-btc` (2×1 prix + courbe → 4×1 + période + axes → 4×2 + liste des jours) avec `assets/btc-demo.json` hors ligne ; source `live` désactivée par défaut (réglage `source`).
- Exemple communautaire : `docs/winjets/examples/com.exemple.citation/` et son ZIP `docs/winjets/examples/com.exemple.citation-1.0.0.zip`.
