# Winlestia 9.3 — Menus du dock

Clic droit sur une application : nom, chemin, nombre de fenêtres, activation d’une fenêtre précise, nouvelle fenêtre, lancement administrateur, épingler/détacher, favoris, Explorateur, réduire/agrandir, fermer une fenêtre ou toutes ses fenêtres. Ajouter aux favoris épingle aussi l’accès au dock ; retirer un favori conserve son épingle. Les accès externes sont enregistrés sans duplication.

Clic droit sur le logo Windows : menu Winlestia des applications installées, alimentation, événements, système, périphériques, réseau, disques, gestion de l’ordinateur, Terminal normal/administrateur, gestionnaire des tâches, paramètres, panneau de configuration, Explorateur, recherche/exécution et bureau. Les outils Windows s’ouvrent depuis ce menu personnalisé ; leurs interfaces internes ne sont pas remplacées.

Le bouton Applications près du son ouvre les applications en cours. Les fenêtres et les applications du catalogue reconnues en arrière-plan sont actualisées chaque seconde pendant que le dock est affiché. Ce panneau ne constitue pas une reproduction de la zone de notification native ni de ses menus spécifiques à chaque éditeur.

Les fenêtres se ferment normalement avec WM_CLOSE, permettant aux applications de demander l’enregistrement. Arrêter l’application est une action distincte, soumise à une confirmation Winlestia ; les exécutables du dossier Windows et le processus du shell sont exclus de cet arrêt forcé. Les menus occupent une petite fenêtre indépendante, ferment à Échap ou après perte de focus et maintiennent le dock visible.

Validation : les trois clics ouvrent leurs menus ; aucun avertissement QML ni erreur native dans ce scénario. Tests isolés des favoris, épingles, absence de doublons et fermeture de deux fenêtres d’un exécutable de test unique. Les 32 tests Python passent. Les réglages personnels ont été comparés avant/après relance et sont conservés. Sauvegarde : checkpoint-before-dock10. Rapports : validation-dock10/.
