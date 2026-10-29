# WinLestia / Panneaux 9.1 — Une matière continue

Le cadre, le dashboard et le dock partagent désormais un seul fond par écran. `surface_geometry.py` calcule l’union de leurs silhouettes et fournit les mêmes courbes au rendu QML et au compositeur Windows. Main.qml et Dock.qml affichent leurs contenus, sans ajouter un autre fond. Les anciens ScreenBorder.qml et DockBackground.qml ne dessinent plus la matière courante.

Le rendu natif utilise des courbes Direct2D, découpées en auxiliaires bornés. Le masque n’est plus une collection de rectangles issus des régions Win32. La silhouette commune évite les superpositions de transparence et les raccords divergents.

Le curseur contrôle à la fois la teinte et la contribution du flou. Auparavant, le flou natif restait opaque même lorsque la teinte devenait transparente. Le produit des transmissions des deux couches correspond maintenant à la transparence demandée. Les surfaces des cartes internes sont plus légères.

Plage de transparence : 5 à 98 %. Réglage choisi pour la relance : 88 %. Les changements faits ensuite dans l’interface restent enregistrés. Les thèmes Porcelaine et Nuit restent opaques ; les favoris, catégories, raccourcis et le masquage des icônes du bureau sont conservés.

## Vérification

`validation-9.1/surface-report.json` mesure une capture réelle sous Windows : mêmes pixels RGB sur les quatre échantillons cadre/dashboard/dock/cadre, aucune séparation inattendue dans les coins, transmission de contraste identique pour les trois surfaces à 88 %, 98 % et 50 %. L’écart aux frontières des auxiliaires natifs reste de 1 niveau RGB à forte transparence, au plus 2 niveaux dans les cas testés. Aucun avertissement QML ni erreur native.

Les 32 tests Python passent. `live-startup-report.json` confirme le démarrage réel, le fond DesktopHut, les réglages et les auxiliaires bornés. `baseline-opaque-blur-report.json` décrit l’essai antérieur ayant montré qu’une simple réduction du rayon de flou ne suffisait pas : il ne concerne pas le matériau final.

Sauvegardes : checkpoint-before-9.1 et ../Glass-v5/checkpoint-before-9.1.
