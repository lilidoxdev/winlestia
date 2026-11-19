# Historique : portage Caelestia pour Windows

Synthèse des notes de portage 8.1 à 9.3 (anciennement `docs/history/caelestia-windows/`, supprimées ; elles restent
consultables dans l'historique git). Le détail courant vit dans `docs/ARCHITECTURE.md` et `docs/BRIDGE.md`.

## Origine et licence

- Amont : [Caelestia Shell](https://github.com/caelestia-dots/shell) (caelestia-dots et contributeurs), tag **v1.4.0**,
  commit `04bfc87f65d6e13b287358e1024d9bbc055b0c58`. Ce tag correspond aux captures de référence du thème.
- Licence : GPLv3. Le portage est distribué avec son code source ; voir `LICENSE`, `THIRD_PARTY_NOTICES.md` et
  `src/backend/LICENSE-CAELESTIA.txt`.
- Le dépôt ne contient pas de copie de l'amont (pas de dossier `upstream/`). La correspondance des fichiers et les
  empreintes d'origine sont dans `src/frontend/UPSTREAM.json` ; les empreintes du portage dans
  `src/frontend/PORT_PATCHES.json`. `src/frontend/prepare_port.py` documente l'extraction initiale, pas la
  reconstruction : ne pas remplacer les QML livrés par une extraction brute de l'amont.

## Ce qui vient de Caelestia

Dashboard, calendrier, horloge, lecteur, visualiseur circulaire, bongocat, jauges, onglets, transitions, courbes
d'animation, boutons, ondulations au clic, menus, sliders, typographie et espacements. La bibliothèque et le lanceur
(8.1) ajoutent leurs propres sources QML.

## Décisions structurantes

- **Rendu** : Qt Quick 6.10 avec fenêtres natives transparentes (Direct3D 11), sans navigateur embarqué.
- **Remplacement des services Linux** : `frontend.py` remplace les services Quickshell par une connexion locale réservée
  au compte Windows courant (pont décrit dans `docs/BRIDGE.md`). Les adaptateurs sont sous `qml/Quickshell`,
  `qml/Caelestia` et `qml/qs/services`.
- **Backend** : .NET 8 (`Glass-v5`, projet `Panneaux.Glass`) : contrôle des médias via GSMTC, spectre par WASAPI
  loopback et FFT (aucun enregistrement), télémétrie PC, volume, luminosité, connexions.
- **Sobriété** : capteurs arrêtés hors des pages Dashboard/Performance, spectre arrêté hors de Média, écritures des
  sliders regroupées.
- **Dock** (8.x à 9.x) : bord physique inférieur, masquage automatique, zone de déclenchement de sept pixels, touche
  Windows seule pour l'afficher (les combinaisons Win+lettre sont conservées). Réveil par événement Windows plutôt que
  par attente périodique de 500 ms. Applications empaquetées et catalogue actualisés pendant l'affichage.
- **Menus du dock** (9.3) : clic droit sur une application (fenêtres, nouvelle fenêtre, administrateur, épingles,
  favoris, Explorateur, fermeture) et sur le logo Windows (applications, alimentation, système, réseau, disques,
  Terminal). Les fenêtres se ferment par `WM_CLOSE` ; arrêter un processus est une action distincte avec confirmation.
- **Filets de sécurité** : un gardien indépendant rétablit Explorer si le backend s'arrête ; un arrêt du frontend
  rétablit la barre Windows ; `RETABLIR_BARRE_WINDOWS.exe` et `SECOURS_BUREAU.exe` restent disponibles (voir
  `docs/RESTORE.md`).
- **Préférences** : fichier `preferences-glass.json` à côté du backend ; caches Qt sous `%LOCALAPPDATA%`. Favoris,
  récents et profils ne sont jamais remplacés par des données de test.
- **Météo** : facultative, données [Open-Meteo](https://open-meteo.com/) (CC BY 4.0) ; configurer l'offre du fournisseur
  adaptée avant une distribution commerciale.

## Limites connues

- Températures dépendantes des sondes et pilotes disponibles.
- Superposition soumise aux restrictions Windows pour les jeux en plein écran exclusif.
- Au portage initial, aucun installateur autonome ; il est venu ensuite avec `installer/` (Inno Setup).

## Validation d'époque

Chaque version 8.1 à 9.3 a été validée par captures et rapports JSON (pages, ouverture et fermeture des menus, arrêt des
capteurs, absence d'avertissement QML), puis par tests isolés des favoris, épingles et fermetures de fenêtres. Ces
artefacts n'ont pas été conservés ; les tests actuels sont décrits dans `docs/TESTS.md`.
