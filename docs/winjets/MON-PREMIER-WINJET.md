# Mon premier Winjet

Ce guide vous mène d'un dossier vide à un Winjet posé sur votre bureau. Aucune programmation : vous écrivez des fichiers JSON (du texte) avec le Bloc-notes ou VS Code. La référence complète est dans [FORMAT-v1.md](FORMAT-v1.md).

À la fin, vous aurez un Winjet qui affiche un message et l'heure, avec deux mises en page (compacte et grande), un réglage, une donnée locale et un aperçu.

## 1. Copier le gabarit

Copiez le dossier `docs/winjets/template/` quelque part (par exemple `Documents\Winjets\`) et renommez la copie `mon-winjet`. Dedans :

```
mon-winjet\
  manifest.json
  widget.json
  README.md        (à supprimer avant de zipper)
  assets\
    .gitkeep       (à supprimer avant de zipper)
```

## 2. Remplir le manifeste

Ouvrez `manifest.json` et changez ces champs :

```json
{
  "format": "winjet/1",
  "id": "com.votrenom.mon-winjet",
  "name": "Mon Winjet",
  "version": "1.0.0",
  "author": "Votre nom",
  "description": "Un message et l'heure.",
  "license": "MIT",
  "compatibility": { "winlestia": ">=0.9.2" },
  "permissions": { "network": [] }
}
```

- `id` : unique, en minuscules, avec des points (`com.votrenom.mon-winjet`). Pas de majuscules, pas d'espaces.
- `version` : trois nombres (`1.0.0`).
- `permissions.network` : `[]` signifie « pas d'Internet ». Laissez-le ainsi ici.

## 3. Construire deux mises en page

Ouvrez `widget.json`. Une carte Winjet a une taille choisie par l'utilisateur ; vous décrivez ce qu'elle affiche **selon sa largeur** avec `layouts`. Les tailles de la grille sont 168, 352, 536 et 720 pixels (1, 2, 3 ou 4 cellules).

Winlestia retient la **dernière** mise en page dont les seuils sont atteints :

```json
"layouts": [
  {
    "minWidth": 0,
    "root": {
      "type": "column", "padding": 16, "gap": 8,
      "children": [
        { "type": "text", "text": "{{ settings.message }}", "style": "title", "maxLines": 2 },
        { "type": "clock", "variant": "digital", "format": "HH:mm" }
      ]
    }
  },
  {
    "minWidth": 340, "minHeight": 340,
    "root": {
      "type": "column", "padding": 24, "gap": 8,
      "children": [
        { "type": "text", "text": "{{ settings.message }}", "style": "display", "maxLines": 2 },
        { "type": "clock", "variant": "digital", "format": "HH:mm" },
        { "type": "text", "text": "{{ now | date:dddd d MMMM }}", "style": "caption", "tone": "secondary" }
      ]
    }
  }
]
```

- La première mise en page (compacte) commence à `"minWidth": 0` : elle sert tant que la carte est petite.
- La seconde (grande) n'est utilisée que si la carte fait au moins 340 px de large et 340 px de haut, soit 2 x 2 cellules (352 x 352).
- `{{ settings.message }}` insère la valeur du réglage `message` ; `{{ now | date:dddd d MMMM }}` insère la date du jour.
- Blocs possibles : `text`, `image`, `clock`, `calendar`, `list`, `chart`, `stat`, `progress`, `dots`, `buttons`, `spacer`, `divider`. Conteneurs : `column`, `row`, `stack`, `grid`. Il n'y en a pas d'autres.
- Pas de couleur écrite en dur. Pour colorer, utilisez `tone` (`primary`, `secondary`, `accent`) ou, sur un graphique, `color` avec un rôle de palette (`accent`, `positive`...).

## 4. Ajouter un réglage

Un réglage est une option que l'utilisateur change depuis les Réglages de Winlestia. Dans `widget.json`, la liste `settings` en contient déjà un (`message`). Ajoutez un interrupteur pour la date :

```json
"settings": [
  { "id": "message", "type": "text", "label": "Message affiché", "default": "Bonjour", "maxLength": 40 },
  { "id": "showDate", "type": "toggle", "label": "Afficher la date", "default": true }
]
```

Puis utilisez-le : enveloppez le bloc de date de la grande mise en page dans une colonne qui ne s'affiche que si le réglage est actif.

```json
{ "type": "column", "visibleWhen": "settings.showDate", "children": [
  { "type": "text", "text": "{{ now | date:dddd d MMMM }}", "style": "caption", "tone": "secondary" }
] }
```

`visibleWhen` s'écrit **sans accolades**. Autres types de réglages : `text`, `choice` (liste d'options), `number`.

## 5. Ajouter des données locales

Créez le fichier `assets/conseils.json` :

```json
{ "texte": "Pensez à boire un verre d'eau." }
```

Déclarez-le dans `widget.json`, au même niveau que `settings` :

```json
"data": {
  "conseils": { "type": "file", "path": "assets/conseils.json" }
}
```

Et affichez-le, à la fin de la grande mise en page :

```json
{ "type": "divider" },
{ "type": "text", "text": "{{ data.conseils.texte }}", "style": "body", "tone": "secondary", "maxLines": 3 }
```

Le nom `conseils` devient `data.conseils` dans les expressions, et `texte` est le champ du fichier. Le fichier doit rester sous `assets/` et faire moins de 256 Kio.

Le `widget.json` complet de ce guide tient en une quarantaine de lignes : les morceaux ci-dessus, dans `layouts`, `settings` et `data`, avec `"size": { "default": [352, 168], "min": [168, 168], "max": [536, 352] }`.

## 6. Image d'aperçu (facultatif)

Ajoutez à la racine un fichier `preview.png` (1 Mo et 1600 x 1000 pixels au plus), par exemple une capture de votre Winjet. Il est affiché dans les Réglages.

## 7. Créer le ZIP

Les fichiers doivent être **à la racine** du ZIP : on doit voir `manifest.json` en ouvrant l'archive, pas un dossier `mon-winjet`.

1. Supprimez `README.md` et `assets\.gitkeep` du dossier (ils ne sont pas autorisés).
2. Ouvrez PowerShell dans le dossier **parent** de `mon-winjet` et lancez :

```powershell
Compress-Archive -Path .\mon-winjet\* -DestinationPath .\com.votrenom.mon-winjet-1.0.0.zip -Force
```

Le `\*` est important : il zippe le contenu du dossier et non le dossier lui-même. Vérifiez : ouvrez le ZIP dans l'Explorateur, `manifest.json` et `widget.json` doivent être visibles tout de suite. Le ZIP doit faire moins de 5 Mio.

## 8. Importer dans Winlestia

1. Ouvrez les Réglages de Winlestia (Win+Alt+S), page **Winjets**.
2. Glissez le ZIP dans la zone d'import.
3. Winlestia valide le paquet et affiche une fiche. Vérifiez le **nom**, l'**auteur** (c'est le nom déclaré dans le manifeste) et le **réseau** : « aucun accès réseau » pour votre premier Winjet. Pour un paquet téléchargé, n'ajoutez que ce qui vous convient.
4. Cliquez sur **Ajouter au bureau**. La carte apparaît derrière vos fenêtres ; utilisez « Modifier les Winjets » pour la déplacer ou la redimensionner et voir la mise en page changer.

Pour mettre à jour : augmentez `version`, rezippez, réimportez le même `id`.

## 9. Corriger les erreurs

L'import affiche des messages en français qui commencent par le fichier et le chemin fautif (`$.layouts[1].root.children[0]`). Corrigez, rezippez, réimportez.

| Message | Cause et remède |
|---|---|
| `manifest.json est obligatoire.` | Le ZIP contient un dossier au lieu des fichiers. Rezippez avec `Compress-Archive -Path .\dossier\*`. |
| `entrée « x » : fichier non prévu à la racine` | Un fichier autre que `manifest.json`, `widget.json`, `preview.png` est à la racine (souvent `README.md`). Déplacez-le sous `assets/` ou supprimez-le. |
| `tout contenu doit se trouver sous assets/` | Un dossier à la racine n'est pas `assets`. |
| `extension « .svg » non autorisée` | Seuls `.json .png .jpg .jpeg .webp .gif .txt .md` sont permis. Convertissez l'image en PNG. |
| `JSON invalide ou trop profond` | Virgule oubliée ou en trop, guillemet manquant, commentaire. Collez le fichier dans un validateur JSON. |
| `$.id : identifiant invalide` | Minuscules, chiffres, points et tirets seulement, au moins deux segments (`com.nom.widget`). |
| `$.version : version invalide` | Trois nombres : `1.0.0`, pas `1.0`. |
| `$.format : valeur « … » non prise en charge` | Mettez exactement `"winjet/1"`. |
| `type « carte » inconnu` | Le bloc ou conteneur n'existe pas. Liste fermée : voir l'étape 3. |
| `valeur permise : …` | Une valeur d'énumération (`style`, `variant`, `align`...) n'est pas dans la liste affichée. |
| `couleur littérale interdite` / `couleur non permise` | Remplacez `#ff0000` par un rôle de palette : `accent`, `primary`, `secondary`, `tertiary`, `onSurface`, `onSurfaceVariant`, `positive`, `negative`. |
| `fichier introuvable dans le paquet : assets/x.json` | Le chemin ne correspond à aucun fichier : vérifiez le nom, les majuscules et le dossier `assets/`. |
| `expression invalide` | Une condition (`visibleWhen`, `enabledWhen`) est mal écrite : comparaisons `== != < <= > >=`, `&&`, `\|\|`, `!`, et rien d'autre. Pas d'accolades. |
| `clé d'état « x » non déclarée dans « state »` | Ajoutez la clé dans `"state": { "x": "valeur" }`. |
| `l'hôte « … » n'est pas déclaré dans permissions.network` | Ajoutez l'hôte exact (sans `https://`) dans `manifest.json`. |
| `seul https:// est autorisé.` | Une source `http` doit utiliser une adresse `https://`. |
| `« min » dépasse « max »` | Corrigez `size.min` et `size.max`. |
| `Trop d'erreurs : la liste est tronquée.` | Corrigez les premières erreurs, puis réimportez. |

Un Winjet qui s'importe mais n'affiche rien : vérifiez que la première mise en page a `"minWidth": 0`, que les chemins `{{ data.source.champ }}` correspondent au JSON, et que `visibleWhen` n'est pas toujours faux.

## Aller plus loin

- Un exemple complet avec boutons et données locales : [`examples/com.exemple.citation/`](examples/com.exemple.citation/).
- Générer un paquet avec une IA : [PROMPT-IA.md](PROMPT-IA.md).
- Valider vos fichiers avec les schémas : [`schema/`](schema/) (utilisables dans VS Code via `"$schema"` ou avec un validateur JSON Schema).
