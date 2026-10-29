# WinLestia / Panneaux 9.2 — Clics, contours et fluidité

Les fenêtres de contenu possèdent désormais un masque d’entrée natif correspondant à leur silhouette visible. Les marges et les angles transparents laissent passer les clics vers les applications situées derrière ; les commandes du dashboard restent interactives. Le masque suit aussi le déplacement du dock.

En mode automatique, un liseré continu de 1 pixel à 23 % d’alpha permet de distinguer les formes, même lorsque le verre est très transparent. Il suit l’union cadre/dashboard/dock et ne dessine pas de raccords internes. La matière et le curseur de transparence restent communs aux trois surfaces.

Le compositeur conserve ses auxiliaires natifs et ne recalcule que les cellules affectées par une animation. La fenêtre du dashboard conserve son tampon pendant l’ouverture : seule la silhouette change. Les onglets déjà visités restent chargés ; les animations médias sont suspendues lorsqu’elles ne sont pas affichées. Les animations imbriquées de taille ont été retirées et l’ouverture/fermeture utilise une courbe de décélération sur 260 ms, adaptée au réglage de mouvement.

## Validation

- 32 tests Python passent.
- 14 contrôles du rendu Windows passent : matière identique, coins propres, absence de raccords entre cellules, transmission identique à 50 %, 88 % et 98 %, aucun verre résiduel à la fermeture.
- Des clics physiques envoyés vers une fenêtre de test indépendante traversent le bureau, la marge et l’angle transparent. Le bouton Applications ouvre son espace sans transmettre le clic à cette fenêtre.
- Sur 12 transitions à 165 Hz, le temps natif médian vaut 0,26 ms et son 95e percentile 0,44 ms, contre 8,85 ms et 41,31 ms avant modification. Les intervalles d’images mesurés pendant les transitions ont une médiane de 6,71 ms, un 95e percentile de 16,95 ms et un maximum de 27,76 ms. Cela mesure ce scénario ; le maintien permanent de 165 images/s n’est pas garanti.
- Démarrage réel confirmé, masque natif présent, aucun nouveau message QML. Réglages conservés, y compris opacité 0,55 et épaisseur 31 au moment de la relance. Les icônes du bureau sont masquées normalement.

Rapports dans validation-9.2/. Sauvegardes : checkpoint-before-9.2 et ../Glass-v5/checkpoint-before-9.2. Aucun favori, raccourci ou réglage personnel n’a été réinitialisé.
