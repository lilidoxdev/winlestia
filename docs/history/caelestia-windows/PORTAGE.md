# Panneaux 8.1 — portage Caelestia pour Windows

L’interface utilise les sources QML de [Caelestia Shell](https://github.com/caelestia-dots/shell), tag **v1.4.0**, commit `04bfc87f65d6e13b287358e1024d9bbc055b0c58`. Cette version correspond aux captures de référence. Ce portage est distribué avec la GPLv3 et son code source.

Voir aussi [les améliorations 8.1](AMELIORATIONS-8.1.md) pour la bibliothèque complète, Ctrl + F2, les paramètres et le dock.

## Ce qui vient directement de Caelestia

Dashboard, calendrier, horloge, lecteur, visualiseur circulaire, bongocat, jauges doubles, onglets, transitions de taille et de déplacement, courbes d’animation, boutons, ondulations au clic, menus, sliders, typographie et espacements. `UPSTREAM.json` conserve les chemins et empreintes d’origine ; `PORT_PATCHES.json` indique les empreintes du portage. `upstream/` reste la copie originale.

## Adaptations Windows

Qt Quick 6.10.3 rend des fenêtres natives transparentes avec Direct3D 11. Aucun navigateur n’est utilisé. `frontend.py` remplace les services Quickshell/Linux par une connexion locale réservée au compte Windows courant. Les adapters sont sous `qml/Quickshell`, `qml/Caelestia` et `qml/qs/services`.

Le backend .NET 8 est dans `../Glass-v5/` : `QtShellBridge.cs`, `WindowsSpectrum.cs`, `WindowsDockKey.cs`, `MediaController.cs`, `PcTelemetry.cs`, `SystemAudio.cs`, `MonitorBrightness.cs`, `WindowsConnections.cs` et les outils Panneaux existants. Le quatrième onglet est adapté aux applications et fichiers Windows.

La lecture audio utilise GSMTC ; le spectre utilise WASAPI loopback et une FFT, sans enregistrement audio. Les capteurs s’arrêtent hors des pages Dashboard/Performance. Le spectre s’arrête hors de Media. Les écritures des sliders sont regroupées pour éviter les commandes en attente.

Le dock est au bord physique inférieur et se masque automatiquement. La touche Windows seule l’affiche ; les combinaisons Win+lettre sont conservées. Un gardien indépendant rétablit Explorer si le backend s’arrête. Un arrêt du frontend rétablit également la barre Windows.

## Lancer et revenir en arrière

`LANCER_CAELESTIA.cmd` lance le logiciel installé. Survoler le haut au centre affiche les quatre onglets ; Ctrl+F1 ouvre/replie le panneau. Le bas de l’écran fait apparaître le dock. Ses boutons son/Wi-Fi ouvrent les commandes Windows. Le clic droit sur un accès propose favoris, Explorateur et personnalisation.

`REVENIR_A_PANNEAUX.ps1` désactive le nouveau frontend et relance l’interface précédente. `../Glass-v5/RETABLIR_BARRE_WINDOWS.exe` rétablit la barre Windows indépendamment. Le secours du Bureau dans Documents reste inchangé.

Les préférences restent dans `../Glass-v5/preferences-glass.json`. Favoris, récents, sites et profils ne sont pas remplacés par les données de test. Les caches Qt sont dans `%LOCALAPPDATA%/Panneaux/Caelestia`.

## Reconstruire

Installer Python 3.12 et .NET SDK 8, puis :

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
dotnet publish ..\Glass-v5\Panneaux.Glass.csproj -c Release -o .\artifacts\backend
```

Les sources QML livrées contiennent déjà les adaptations : ne pas les remplacer par une extraction brute de l’upstream. `prepare_port.py` documente l’extraction initiale, pas la reconstruction finale.

## Validation et limites

`validation/report.json` et les PNG montrent les quatre pages avec les données du PC, la fermeture et l’arrêt des capteurs. `verification-qt-keys.json` vérifie les combinaisons Windows sans injecter de frappes. `validation/summary.json` contient les mesures de la dernière exécution.

Les températures dépendent des sondes/pilotes disponibles. La superposition reste soumise aux restrictions Windows des jeux en plein écran exclusif. Le backend et les anciens outils Windows restent conservés ; ce premier portage n’est pas encore un installateur commercial autonome pour d’autres PC.

La météo est facultative : cliquer sur la tuile pour choisir une ville. Données [Open-Meteo](https://open-meteo.com/), attribution CC BY 4.0 ; configurer l’offre du fournisseur adaptée avant une distribution commerciale.
