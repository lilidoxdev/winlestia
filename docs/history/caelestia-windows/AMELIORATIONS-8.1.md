# Panneaux 8.1 — bibliothèque et lanceur Caelestia

Le dépôt officiel [caelestia-dots/shell](https://github.com/caelestia-dots/shell) a été revérifié. Le portage conserve le tag v1.4.0 pour rester cohérent avec le thème choisi. Le lanceur ajoute ses sources `Content.qml`, `AppList.qml`, `AppItem.qml` et `LauncherConfig.qml` ; leurs origines et modifications figurent dans les manifestes.

## Navigation et dock

- Une application maximisée avec une barre de titre ne bloque plus la détection du bord inférieur.
- Une zone de sept pixels déclenche le dock. Atteindre volontairement le bord l’affiche même devant une application plein écran ; il reste masqué pendant l’usage normal de cette application.
- Les commandes ouvertes depuis le dock restent présentes jusqu’à Échap ou un clic extérieur.
- Un numéro de navigation empêche une ancienne fermeture de replier une vue qui vient d’être ouverte.
- Un événement Windows réveille le canal local à la demande : les raccourcis ne dépendent plus de l’attente périodique de 500 ms.

## Bibliothèque

Applications, Jeux, Projets et Documents ouvrent désormais une bibliothèque Qt : espaces latéraux, catégories, recherche, favoris, récents, tri et modes grille/liste. Le rendu est virtualisé ; seules les icônes nécessaires sont chargées. Le clic droit permet de renommer, catégoriser, choisir une icône et ranger un accès dans un autre espace. Les fichiers restent à leur emplacement.

L’onglet Workspaces du tableau de bord présente quatre cartes d’accès, les favoris et les récents. L’interface précédente reste disponible avec le mode de secours ; elle n’est plus utilisée pour ces vues.

## Ctrl + F2 et fonctions ajoutées

Le lanceur utilise les composants visuels et les transitions Caelestia avec les services Windows : applications, onglets rapides conservés, sites, commandes préfixées par `>` et calcul direct. Les flèches sélectionnent un résultat et Entrée l’ouvre ; un calcul est copié à la demande.

Les paramètres Qt regroupent les deux thèmes, les animations, la taille des icônes, le dock, le démarrage Windows, les raccourcis et l’historique. Le presse-papiers utilise également les surfaces et listes Caelestia, avec recherche, copie et épinglage. Les outils spécialisés Windows existants restent accessibles.

## Correspondance avec Caelestia

| Module examiné | Adaptation Windows |
| --- | --- |
| Dashboard / Media / Performance | Pages originales et services Windows conservés |
| Launcher | Composants importés, recherche locale et commandes Windows |
| Control center | Contrôles Caelestia raccordés au son, DDC/CI et radios Windows |
| Workspaces / Hyprland | Bibliothèque d’accès et profils Panneaux ; le compositeur Linux n’est pas utilisé |
| Sidebar / notifications | Le système de notifications Windows existant reste accessible via les outils |
| Schemes / wallpaper / power services | Thèmes clair/sombre repris ; services propres à Linux non chargés sous Windows |

## Validation

`tests/test_interaction_search.py` couvre le dock, les fenêtres maximisées, le plein écran, les calculs et la recherche. `tests/check_bridge.py` vérifie les changements de métadonnées dans le backend isolé uniquement, puis les rétablit. Les quinze vues, les captures, les erreurs QML et la veille des capteurs sont vérifiées dans `validation-8.1/report.json`.

Les préférences de test ne sont jamais recopiées sur l’installation. Une sauvegarde de la version 8.0 se trouve dans `../Glass-v5/checkpoint-20261025-before-8.1` ; le secours du Bureau reste indépendant et inchangé.

## Correctif du 26 octobre : dock indépendant

Le dock est explicitement séparé de la fenêtre supérieure (`transientParent: null`). Le test natif `tests/check_dock_lifecycle.py` reproduit la panne précédente et vérifie cinq phases, dont une apparition avant toute ouverture du haut et une réapparition après fermeture. Les rapports avant/après sont dans `validation-8.1/dock-lifecycle-before.json` et `dock-lifecycle.json`.
