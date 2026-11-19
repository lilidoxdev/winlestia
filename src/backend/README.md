# Backend Winlestia (Panneaux.Glass)

Un dossier par domaine. Les espaces de noms ne suivent pas les dossiers (inchangés) ; `VsCodeTheme*.cs` restent à la racine.

- `Core/` : point d'entrée et chef d'orchestre (`Coordinator`, `Program`), modèles de données, utilitaires sûrs, P/Invoke communs.
- `Theme/` : jetons de design, palettes, rendu du thème, mouvement, formes.
- `Audio/` : mixeur, barres et spectre audio, volume système, média, OSD.
- `Taskbar/` : barre des tâches / dock, icônes, aperçus, réservation d'écran, détection du plein écran.
- `Bridge/` : pont vers le frontend Qt (tube nommé) et serveur de la CLI `winlestia`.
- `Shell/` : réglages, raccourcis globaux, palette de commandes, démarrage automatique, catalogue d'applications, toasts.
- `Desktop/` : icônes du bureau, bureaux virtuels, récents.
- `Explorer/` : apparence et icônes de navigation de l'Explorateur Windows.
- `Files/` : intégration de Winlestia Fichiers (tube, gestionnaire par défaut, préchargement, recherche, corbeille).
- `Wallpaper/` : application et logique du fond d'écran, médias animés.
- `Telemetry/` : CPU/GPU/températures, graphes, sondes de performance.
- `Network/` : Wi-Fi, connexions, type de réseau.
- `Display/` : écrans, luminosité, disposition des panneaux.
- `Social/` : compteurs et notifications des services sociaux.
- `Focus/` : minuteur et alarme de concentration.
- `Web/` : raccourcis web, icônes, marque-pages.
- `Tiling/` : placement et pavage des fenêtres.
- `ControlCenter/` : centre de contrôle et son éditeur.
- `Windows/` : fenêtres WPF (accueil, tableau, notes, horloge, personnalisation, outils...).
- `Tools/` : outils (calculatrice, capture, presse-papiers, couleurs, dictée, espaces de travail...).
- `Verification/` : modes de vérification en ligne de commande et fixtures de test.
- `native/`, `assets/`, `web-icons/` : binaires natifs précompilés, ressources, icônes (non-C#).
