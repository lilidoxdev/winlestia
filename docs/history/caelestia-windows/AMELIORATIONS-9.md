# Panneaux 9.0 — Une seule matière, une seule transparence

Le cadre, le dashboard et le dock lisent maintenant la même valeur. Le réglage
« Transparence du verre » pilote les trois surfaces, et rien d'autre.

## La ligne au raccord

`ScreenBorder.qml` peignait la teinte du cadre dans une couche Qt (`layer.enabled`)
puis la découpait avec un `MultiEffect` masqué. La couleur était déjà semi-transparente
avant d'entrer dans la texture, puis composée une seconde fois à la sortie : l'alpha
réel du cadre tombait autour de 0,04 au lieu de 0,20.

Mesure sur le bureau réel, même colonne, avant correction :

| zone | couleur |
|---|---|
| cadre (y = 0…19) | 43, 72, 93 |
| dashboard (y = 22…) | 33, 42, 49 |

Le cadre laissait donc passer le fond presque brut, sans teinte, alors que le
dashboard appliquait la sienne. D'où la ligne nette au raccord.

Le cadre est désormais un `Shape` en anneau, rempli en `OddEvenFill` : un seul
tracé, aucune texture intermédiaire, aucun effet. L'alpha arrive à l'écran tel quel.

## Une seule matière

`Colours.qml` expose `surfaceBase` (couleur opaque de la palette) et `surfaceColor`
(`Qt.alpha(surfaceBase, surfaceOpacity)`). Le cadre, le dashboard et le dock
consomment tous les trois `surfaceColor`. Le cadre n'utilise plus sa propre
`paletteForScreen` pour son fond : plus de teinte parallèle, donc plus d'écart.

## Plus de transparence

- Curseur : 5 → 95 % (au lieu de 5 → 80 %).
- Bornes backend et frontend : alpha 0,05 → 0,95.
- Valeur en place : `GlassOpacity = 0.05`, soit 95 % de transparence — c'est la position
  que dj a laissée sur le curseur. Le verre laisse franchement voir le bureau et le
  texte des panneaux reste lisible.
- Épaisseur du cadre inchangée : 20 px.

Porcelaine et Nuit restent opaques : hors mode Automatique, `surfaceOpacity` vaut 1
et `surfaceColor` renvoie la couleur pleine, exactement comme avant.

## Coins bas

Deux défauts se cumulaient aux angles :

1. les huit auxiliaires natifs avaient la taille exacte de leur bande (20 px). Un
   flou gaussien de rayon 22 dans une fenêtre de 20 px est entièrement mangé par son
   bord : le liseré dur du noyau tombait pile sur la limite entre deux pièces ;
2. la couche Qt du cadre ne peignait rien (voir plus haut), donc ces liserés
   restaient seuls visibles.

Chaque fenêtre native est maintenant agrandie vers l'intérieur de `max(56, 2 × épaisseur + rayon)`
pixels. Le noyau de flou se développe entièrement à l'intérieur de la fenêtre et son
bord dur tombe hors de la zone découpée. La zone découpée, elle, reste strictement
la même : huit pièces disjointes, jamais le centre du bureau.

## Mesures

`validation-9/seam-report.json` — colonne centrale, dashboard ouvert, même fond
visible des deux côtés :

- cadre : 184,0 / 200,0 / 215,0
- dashboard : 184,0 / 200,0 / 215,0
- écart au raccord : **0,0**

Un premier passage sur un fond bleu sombre donnait 48,0 / 62,0 / 80,0 de part et
d'autre, également à 0,0 d'écart.

`validation-9/desktop-regression-report.json` — DLL de production, épaisseurs 4, 16 et 40 px :

- 0 pixel noir dans les quatre coins pour chacune des trois épaisseurs ;
- 8 auxiliaires par cadre, aucun ne couvre le centre du bureau ;
- chaque auxiliaire reste une bande bornée (< 40 % de la surface de l'écran) ;
- pixels du centre du bureau inchangés ;
- géométrie invalide : tout est masqué, puis tout revient au rétablissement.

Tests Python : 27/27 (palette chromatique et interaction/recherche).

## La barre des tâches disparue

Deux verrous se cumulaient.

1. `QtShellBridge` appelait `owner.SetTaskbar(false)` quand le moteur Qt s'arrêtait.
   Cette méthode écrivait `ReplaceTaskbar = false` dans les préférences : un simple
   arrêt du frontend décidait donc, durablement, que la barre n'était plus voulue.
   Au démarrage suivant, le secours n'était plus activé, et comme le dock Qt est
   conditionné par `TaskbarEnabled` (lui-même `taskbar.Enabled`), le dock restait
   caché pour toujours. Le 26/10 à 02:33:23, un arrêt du moteur Qt a suffi.
   `SuspendTaskbar()` restaure maintenant la barre Windows **sans** réécrire la
   préférence. Vérifié : frontend tué à 02:53:09, `ReplaceTaskbar` toujours à `true`.
2. En plein écran, `EdgePolicy` renvoyait `False` dès que la souris quittait les sept
   derniers pixels de l'écran, sans regarder si elle était dans le dock. Le dock
   s'ouvrait puis se refermait avant qu'on puisse cliquer une icône. Le test
   `inside` passe désormais avant le test `fullscreen` : une fois révélé, le dock
   reste tant que la souris est dedans.

Le journal affiche maintenant `Démarrage Panneaux 9.0`.

## Fichiers touchés

Frontend : `qml/ScreenBorder.qml`, `qml/ScreenFrame.qml`,
`qml/qs/services/Colours.qml`, `qml/ShellPreferences.qml`, `frontend.py`,
`interaction.py`, `tests/test_interaction_search.py`, `QtGlassBackdrop.cpp`,
`QtGlassBackdrop.dll`.
Backend : `Coordinator.cs`, `Models.cs`, `QtShellBridge.cs`,
`preferences-glass.json`.

## Sauvegardes

`checkpoint-20261026-021942-avant-reprise` (frontend) et
`../Glass-v5/checkpoint-20261026-before-8.6` (backend) restent la référence 8.6.

## En attente

Le retour visuel de dj sur cette 9.0. Rien n'est déclaré validé avant.
