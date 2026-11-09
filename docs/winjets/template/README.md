# Gabarit de Winjet

Copiez ce dossier, renommez-le, puis remplissez les champs ci-dessous. Guide complet : `docs/winjets/MON-PREMIER-WINJET.md`. Référence : `docs/winjets/FORMAT-v1.md`.

## Contenu

```
manifest.json   identité du Winjet (à remplir)
widget.json     taille, réglages, données et mise en page (à adapter)
assets/         vos images et vos JSON de données (supprimez .gitkeep)
preview.png     facultatif : aperçu affiché dans les Réglages (à ajouter vous-même)
README.md       ce fichier : ne le mettez pas dans le ZIP
```

## `manifest.json` : champs à remplir

| Champ | Quoi mettre |
|---|---|
| `format` | ne pas changer : `winjet/1` |
| `id` | identifiant unique en minuscules, de type `com.votrenom.monwinjet` (lettres, chiffres, tirets ; 2 à 5 segments séparés par des points) |
| `name` | le nom affiché |
| `version` | `1.0.0` ; augmentez-la à chaque nouvelle version |
| `author` | votre nom ou pseudo |
| `description` | une phrase |
| `license` | par exemple `MIT` |
| `compatibility.winlestia` | version minimale de Winlestia, par exemple `>=0.9.2` |
| `permissions.network` | laissez `[]` si le Winjet n'utilise pas Internet ; sinon la liste des hôtes (`"api.exemple.com"`) |
| `homepage` | facultatif : adresse `https://` de votre page. Ajoutez la ligne vous-même si vous la voulez. |

## `widget.json` : parties à adapter

| Partie | Quoi faire |
|---|---|
| `size` | `default` = taille de départ, `min` et `max` = bornes. Tailles de la grille : 168, 352, 536, 720 |
| `settings` | un réglage par entrée : `id`, `type` (`toggle`, `text`, `choice`, `number`), `label`, `default` |
| `state` | (absent du gabarit) valeurs modifiables par des boutons : `"state": { "mode": "a" }` |
| `data` | (absent du gabarit) sources : fichier de `assets/` ou adresse `https://` d'un hôte déclaré |
| `layouts` | une mise en page par palier de largeur ; la première commence à `"minWidth": 0` ; la dernière satisfaite est utilisée |

Le gabarit affiche un message (réglage `message`) et l'heure, avec une seconde mise en page pour les grandes cartes.

## Avant de zipper

1. Supprimez `README.md` et `assets/.gitkeep` de la copie que vous zippez.
2. Les fichiers doivent être à la racine du ZIP.
3. Pas de couleur écrite en dur : utilisez des rôles de palette (`accent`, `primary`, `secondary`, `tertiary`, `onSurface`, `onSurfaceVariant`, `positive`, `negative`).
