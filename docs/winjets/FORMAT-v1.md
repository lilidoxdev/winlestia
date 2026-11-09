# Format Winjet v1 (`winjet/1`)

Spécification publique du format des paquets Winjets de Winlestia. Un Winjet est un widget de bureau décrit par des données JSON : Winlestia l'affiche, le paquet ne contient aucun code.

- Schémas JSON (draft 2020-12) : [`schema/manifest.schema.json`](schema/manifest.schema.json) et [`schema/widget.schema.json`](schema/widget.schema.json).
- Gabarit à remplir : [`template/`](template/). Exemple complet : [`examples/com.exemple.citation/`](examples/com.exemple.citation/).
- Guide pas à pas : [`MON-PREMIER-WINJET.md`](MON-PREMIER-WINJET.md).

Les schémas décrivent la forme du JSON ; Winlestia applique en plus des contrôles (références, hôtes, limites) listés plus bas. Un paquet qui passe les schémas peut donc encore être refusé à l'import, avec un message en français qui indique le chemin fautif.

## 1. Contenu d'un paquet

Un paquet est un fichier ZIP ou un dossier contenant, **à la racine** :

```
manifest.json      obligatoire
widget.json        obligatoire
preview.png        facultatif (aperçu, 1 Mo et 1600 x 1000 pixels au plus)
assets/            facultatif : images et JSON de données locales
```

Dans un ZIP, ces fichiers sont à la racine de l'archive, pas dans un dossier intermédiaire. Rien d'autre n'est permis à la racine (pas de `README.md`, pas de `LICENSE`) : tout autre fichier va sous `assets/`.

### Limites

| Élément | Limite |
|---|---|
| Taille du ZIP | 5 Mio |
| Taille décompressée | 20 Mio |
| Entrées | 200 au plus |
| Profondeur des dossiers | 4 au plus |
| Chemins | relatifs ; ni chemin absolu, ni `..`, ni lien, ni nom réservé Windows (`CON`, `NUL`, `COM1`...) |
| Extensions | `.json .png .jpg .jpeg .webp .gif .txt .md` |
| Fichier JSON | 256 Kio chacun, profondeur 32 au plus, sans commentaires ni virgule finale |
| Mise en page | 500 noeuds, 12 niveaux d'imbrication, 64 enfants par conteneur |
| Réglages | identifiants uniques |
| État (`state`) | 24 clés au plus ; valeur initiale de 100 caractères au plus |
| Sources (`data`) | 8 au plus |
| Réponse réseau | 512 Kio au plus par appel, délai de 10 s |

Tout autre contenu (script, exécutable, QML, extension non listée) fait échouer l'import. Les JSON sont lus comme des données : aucun fichier du paquet n'est exécuté ni chargé comme du code.

## 2. `manifest.json`

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

| Champ | Obligatoire | Règle |
|---|---|---|
| `format` | oui | exactement `"winjet/1"` |
| `id` | oui | `^[a-z0-9]+(\.[a-z0-9-]+){1,4}$`, 3 à 64 caractères. Sert d'identité : réimporter le même `id` met le paquet à jour. Convention : nom de domaine inversé (`com.exemple.horloge`). Le préfixe `winlestia.` est celui des paquets inclus : évitez-le. |
| `name` | oui | nom affiché |
| `version` | oui | SemVer `x.y.z` (`1.0.0`) |
| `author` | oui | nom déclaré par le créateur ; affiché tel quel à l'import, Winlestia ne le vérifie pas |
| `description` | non | une phrase |
| `license` | non | identifiant de licence (`MIT`...) |
| `homepage` | non | adresse `https://` ; son hôte est accepté par l'action `openUrl` |
| `compatibility.winlestia` | non | version minimale, éventuellement bornée : `>=0.9.2`, `>=0.9.2 <2.0.0` |
| `permissions.network` | non | liste de noms d'hôte exacts, en minuscules, sans schéma, sans port, sans chemin, sans joker. Vide par défaut. |

Tout champ inconnu est une erreur.

## 3. `widget.json`

```json
{
  "size": { "default": [352, 168], "min": [168, 168], "max": [720, 352] },
  "settings": [ ],
  "state": { "mode": "face" },
  "data": { },
  "layouts": [ { "minWidth": 0, "root": { "type": "column", "children": [ ] } } ]
}
```

Seuls `size.default` et `layouts` sont obligatoires.

### 3.1 Taille et grille

Les tailles sont en pixels logiques (indépendants du DPI). La grille a une cellule de 168 px et une gouttière de 16 px :

| Cellules | 1 | 2 | 3 | 4 |
|---|---|---|---|---|
| Pixels | 168 | 352 | 536 | 720 |

La même échelle vaut en hauteur : `[352, 168]` est un format 2 x 1. `size.min` est au moins `[120, 120]` (valeur par défaut), `size.max` au plus `[1600, 1000]` (valeur par défaut). L'utilisateur redimensionne la carte entre ces bornes ; `default` est ramené dans l'intervalle.

### 3.2 Réglages (`settings`)

Les réglages sont modifiables par l'utilisateur, par instance. Champs communs : `id` (une lettre puis lettres, chiffres ou `_`, 32 caractères au plus, unique), `type`, `label` (texte affiché), `default`.

| `type` | Champs en plus | `default` |
|---|---|---|
| `toggle` | | vrai ou faux |
| `text` | `maxLength` (facultatif) | texte |
| `choice` | `options` : `[{ "value": "7d", "label": "7 jours" }]`, au moins une | l'une des `value` |
| `number` | `min`, `max` (facultatifs) | nombre compris entre `min` et `max` |

Lecture dans les expressions : `settings.<id>`.

### 3.3 État d'instance (`state`)

`state` déclare des clés avec leur valeur initiale (texte, nombre ou vrai/faux). Seules les actions `setState` et le bloc `buttons` peuvent les modifier, et seulement pour une clé déclarée. L'état est propre à chaque instance posée sur le bureau. Lecture : `state.<clé>`.

### 3.4 Sources de données (`data`)

Le nom de la source (une lettre puis lettres, chiffres ou `_`) devient `data.<nom>` dans les expressions.

```json
"data": {
  "demo": { "type": "file", "path": "assets/donnees.json" },
  "live": { "type": "http", "url": "https://api.exemple.com/v1/prix", "refresh": 300,
            "enabledWhen": "settings.source == 'live'" }
}
```

- `file` : un JSON du paquet sous `assets/` (obligatoire : `path`). Lu localement, hors ligne.
- `http` : un JSON distant. `url` en `https://` seulement, dont l'hôte figure dans `permissions.network` ; sans cela l'import échoue. `refresh` : intervalle en secondes, 300 par défaut, **minimum 60** (une valeur plus petite est relevée à 60), 86 400 au plus. `enabledWhen` : condition ; tant qu'elle est fausse, la source n'est jamais appelée.

Une source indisponible ne bloque pas la carte : les chemins `data.*` sans valeur sont simplement vides, et le dernier résultat en cache est réutilisé en cas d'échec.

### 3.5 Mises en page (`layouts`)

`layouts` est une liste ordonnée. Chaque entrée :

```json
{ "minWidth": 340, "minHeight": 300, "root": { "type": "row", "children": [ ] } }
```

`minWidth` est obligatoire, `minHeight` facultatif. Winlestia retient **la dernière entrée** dont `minWidth` et `minHeight` sont satisfaits par la taille réelle de la carte. Ordonnez-les donc de la plus petite à la plus grande et faites commencer la liste par `"minWidth": 0`, qui couvre tous les cas. Comme la cellule 2 x 1 mesure 352 px, un seuil de 340 repère « au moins 2 colonnes » et 520 « au moins 3 ».

Chaque mise en page est un arbre complet : elle ne dépend pas des précédentes.

## 4. Conteneurs

Quatre conteneurs, tous avec `children` (liste de conteneurs et de blocs) :

| `type` | Rôle |
|---|---|
| `column` | enfants empilés verticalement |
| `row` | enfants alignés horizontalement |
| `stack` | enfants superposés |
| `grid` | grille ; `columns` (1 à 12) obligatoire |

Propriétés communes : `gap` (espace entre enfants, entier de 0 à 1000), `padding` (marge intérieure), `align` (`start`, `center`, `end`, `stretch`), `grow` (poids de répartition de l'espace restant), `visibleWhen` (condition ; faux = non affiché), `action` (rend le conteneur cliquable).

## 5. Blocs

Les blocs n'ont pas d'enfants. Une valeur de type texte accepte des expressions `{{ ... }}` (section 6) sauf mention contraire ; les valeurs d'énumération sont littérales. Les propriétés `visibleWhen`, `grow`, `gap`, `padding` sont acceptées par l'import sur tout noeud ; ce document et les schémas les déclarent sur les conteneurs, et les paquets fournis enveloppent un bloc dans une `column` quand ils en ont besoin.

| Bloc | Propriétés (obligatoires en gras) |
|---|---|
| `text` | **`text`**, `style` (`title`, `label`, `body`, `caption`, `display`, `mono`), `align` (`start`, `center`, `end`), `maxLines`, `tone` (`primary`, `secondary`, `accent`), `action` |
| `image` | **`src`** (chemin sous `assets/`, image `.png .jpg .jpeg .webp .gif`), `fit` (`contain`, `cover`), `radius`, `action` |
| `clock` | **`variant`** (`analog`, `digital`), `showSeconds` (vrai/faux ou expression), `format` (`HH:mm`, `HH:mm:ss`...), `timezone` (nom IANA, ex. `Europe/Paris` ; vide = fuseau local) |
| `calendar` | **`variant`** (`month`, `day`), `firstDay` (`monday`, `sunday`, valeur littérale) |
| `list` | **`items`** (expression vers un tableau), **`title`**, `subtitle`, `trailing` (**noms de champs** de chaque élément, sans accolades), `max` |
| `chart` | **`series`** (expression vers un tableau de nombres ou de paires `[t, v]`), `variant` (`line`, `area`, `bars`), `axes` (vrai/faux), `period` (texte), `color` (rôle de palette) |
| `stat` | **`value`**, `label`, `delta` (nombre signé, coloré selon le signe), `action` |
| `progress` | **`value`** (0 à 1), `variant` (`ring`, `bar`), `label` |
| `dots` | **`matrix`** (expression vers un tableau 2D de nombres de 0 à 1) ou **`pattern`** (`pulse`, `ripple`, `torus`, animé seulement quand la carte est visible), `shape` (`circle`, `square`) |
| `buttons` | **`options`** (`[{ "value": "a", "label": "A" }]`, 16 au plus), **`bind`** (`state.<clé>`, clé déclarée dans `state`) |
| `spacer` | aucune : espace flexible |
| `divider` | aucune : filet de séparation |

Exemple de `list` : avec `"items": "{{ data.demo.jours }}"` et `"title": "jour"`, chaque ligne affiche le champ `jour` de l'élément. Les valeurs sont affichées telles qu'elles sont dans le JSON : préparez-les déjà mises en forme.

## 6. Expressions et conditions

### Expressions

Dans une chaîne : `{{ chemin | filtre:arg | filtre }}`. Le texte autour des accolades est conservé : `"Total : {{ data.x.total | number:2 }} €"`.

Chemins permis :

| Chemin | Valeur |
|---|---|
| `settings.<id>` | réglage |
| `state.<clé>` | état d'instance |
| `data.<source>.<chemin>` | contenu de la source, champs séparés par des points |
| `now` | date et heure actuelles |
| `instance.width`, `instance.height` | taille réelle de la carte |

Filtres (liste fermée) :

| Filtre | Effet |
|---|---|
| `number:n` | nombre avec `n` décimales |
| `currency:EUR` | montant dans la devise (code ISO 4217) |
| `percent:n` | pourcentage avec `n` décimales |
| `date:motif` | date avec un motif (`dddd d MMMM yyyy`, `HH:mm`) ; tout ce qui suit le premier `:` est le motif |
| `upper`, `lower` | majuscules, minuscules |
| `default:valeur` | valeur de repli si le chemin est vide |
| `first`, `last` | premier ou dernier élément d'un tableau |
| `delta` | variation relative entre le premier et le dernier élément d'une série |

Aucun autre filtre, appel de fonction ni calcul n'existe.

### Conditions

`visibleWhen` et `enabledWhen` s'écrivent **sans accolades** : `"settings.source == 'live' && state.mode != 'auteur'"`.

- comparaisons : `==  !=  <  <=  >  >=` ;
- logique : `&&`, `||`, `!`, parenthèses ;
- littéraux : `'texte'`, `"texte"`, `1.5`, `true`, `false`, `null` ;
- valeurs : les chemins de la section 6 (un chemin inconnu vaut `null`, donc faux).

Une condition invalide est refusée à l'import (`visibleWhen`) ou laisse la source désactivée (`enabledWhen`).

## 7. Actions

Le champ `action` d'un `text`, `image`, `stat` ou d'un conteneur le rend cliquable :

| Action | Effet |
|---|---|
| `{ "type": "setState", "key": "mode", "value": "auteur" }` | modifie une clé d'état déclarée |
| `{ "type": "refresh" }` | rafraîchit les sources réseau, en respectant l'intervalle minimal |
| `{ "type": "openUrl", "url": "https://exemple.com/page" }` | ouvre l'adresse dans le navigateur ; l'hôte doit être dans `permissions.network` ou être celui de `homepage` |
| `{ "type": "openSettings" }` | ouvre les réglages du Winjet |

## 8. Couleurs

Aucune couleur littérale (`#ff0000`, `rgb(...)`, `hsl(...)`). Une couleur est un **rôle de palette**, qui suit le thème de l'utilisateur :

`accent`, `primary`, `secondary`, `tertiary`, `onSurface`, `onSurfaceVariant`, `positive`, `negative`.

Les propriétés de couleur (`color` sur `chart`) n'acceptent que ces valeurs ; `tone` sur `text` choisit entre `primary`, `secondary` et `accent`.

## 9. Sécurité

- **Contenu = données.** Winlestia n'exécute et ne charge aucun QML, JavaScript, script ni exécutable depuis un paquet. Il n'interprète qu'une liste fermée de conteneurs, de blocs, de filtres et d'actions.
- **Réseau.** Un paquet n'ouvre jamais de connexion lui-même. Seul le service de données de Winlestia appelle les sources `http`, uniquement vers les hôtes déclarés dans `permissions.network`, en HTTPS, au plus un appel en vol par source, avec un intervalle minimal de 60 s, un délai de 10 s et une réponse de 512 Kio au plus. Le dernier résultat est gardé en cache sur disque et resservi si l'appel échoue.
- **Transparence.** L'import affiche le nom, l'auteur déclaré et les hôtes réseau demandés avant l'ajout au bureau.
- **Fichiers.** Aucun chemin absolu, `..`, lien ou nom réservé ; extensions en liste fermée ; limites de taille et de profondeur de la section 1.
- **Pas de secrets.** Le format n'a pas de champ pour une clé d'API ; n'en mettez jamais dans un paquet partagé.

## 10. Versions du format

- Le champ `format` vaut `winjet/1` pour toute la série 1.x du format. Winlestia refuse un paquet dont `format` est inconnu, avec un message qui cite la valeur lue.
- **En v1, un champ inconnu est une erreur** : cela permet d'ajouter des champs plus tard sans que d'anciens paquets changent de sens.
- Les évolutions compatibles (nouvelle valeur d'énumération, nouveau bloc, nouveau filtre) ne sont pas annoncées par un nouveau numéro de format tant qu'elles ne changent pas le sens d'un paquet existant ; elles sont documentées ici avec la version de Winlestia qui les apporte. Un paquet qui les utilise déclare `compatibility.winlestia`.
- Une rupture (changement de sens d'un champ, suppression) introduit **`winjet/2`**, avec sa propre spécification (`FORMAT-v2.md`) et ses propres schémas. Winlestia continuera de lire les paquets `winjet/1` tant que `winjet/2` existe ; les deux formats ne se mélangent pas dans un même paquet.
- La version du paquet (`version`) est indépendante de celle du format : incrémentez-la à chaque publication ; un import d'une version plus récente du même `id` met le paquet à jour.
