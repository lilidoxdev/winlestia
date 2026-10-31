# Journal des modifications

## Non publié

- Menu de session : extinction, redémarrage, déconnexion, veille et hibernation exigent la confirmation, vérifiée aussi côté C# ; la fermeture du menu annule le choix. Panneaux secondaires : marges et espacements sur les jetons `Appearance`, en-têtes alignés, message « Aucun résultat » dans la commande rapide.

- Mode plein écran : quand une fenêtre couvre tout un écran (vidéo, jeu, fenêtre sans bordure), le dock, l'OSD, les barres audio, les animations et le suivi des bords sont coupés et les minuteurs du frontend ralentissent ; tout revient à la sortie, sans redémarrage. L'état suit les événements de fenêtre au premier plan, avec un contrôle de secours toutes les 10 s pendant ce mode seulement. Option « Masquer le dock et l'OSD en plein écran » (activée par défaut), `winlestia quiet [on|off|auto]` pour le tester sans fenêtre plein écran (non enregistré), `quiet` dans `winlestia status`. Le watchdog, les outils de secours et le raccourci du panneau ne sont pas touchés.

- Fonds d'écran : le fond est appliqué par `IDesktopWallpaper` (transition du système, sans fenêtre ajoutée ; en effets allégés, ancien appel sans transition demandée). Avec plusieurs écrans, le sélecteur propose « Tous les écrans » ou un écran (Tab). Une ligne signale DesktopHut, Wallpaper Engine, Lively ou Wallpaper X s'il tourne (nom de processus, lu à l'ouverture et à l'application seulement). `winlestia wallpaper list` renvoie `{"displays":[...],"animated":[...]}` ; `winlestia wallpaper <chemin> --display <id>` cible un écran.

- Tableau de bord, panneau Média : barres audio (28 bandes, échelle logarithmique) issues de la capture WASAPI en boucle du périphérique de sortie par défaut. La capture et son thread n'existent que panneau visible (arrêt en moins de 500 ms) et sont absents en plein écran ou avec « Effets allégés » ; en silence les barres retombent à zéro puis les mises à jour cessent ; au plus 30 images/s par le tube nommé, hors du gros état. Aucun son n'est enregistré. `winlestia visualizer` renvoie `{"running":b,"frames":n}` ; `winlestia panel dashboard` bascule le tableau de bord.

- Commandes rapides : un curseur de luminosité par écran, externes compris (DDC/CI) ; lecture à l'ouverture puis toutes les 30 s panneau ouvert, écran sans DDC/CI grisé. `winlestia brightness` liste les écrans en JSON et `winlestia brightness <id> <0-100>` règle l'un d'eux (OSD affiché, écriture limitée à une toutes les 120 ms par écran).
- Réglages : champ de recherche (titre et description, sans tenir compte de la casse ni des accents) sur les six pages ; le résultat choisi ouvre la page, défile jusqu'à la ligne et la met en évidence brièvement.
- Commandes rapides : interrupteur « Effets allégés ». Si Windows refuse la bascule Wi-Fi ou Bluetooth, la page correspondante des Paramètres Windows s'ouvre en plus du message d'erreur.
- Palette Ctrl+F2 : fenêtres ouvertes (5 au plus, listées à l'ouverture et oubliées à la fermeture), une trentaine de pages des Paramètres Windows (FR/EN/ES) et préfixe `>file ` pour chercher des fichiers du profil dans l'index Windows (requête hors thread UI, 2 s, 200 ms d'anti-rebond, message si l'index est indisponible ; ni saisie ni chemins journalisés). `winlestia find windows` et `winlestia find files <terme>` ne renvoient que des comptes.

- Réglage « Seulement la fenêtre active » (sous « Liseré sur les applications », désactivé par défaut) : le liseré ne colore que la fenêtre au premier plan ; `winlestia status` expose `activeBorderOnly`.
- Commande `winlestia` (status, panel, desktop, wallpaper, palette, help) pour piloter le shell déjà lancé depuis un script ; tube nommé local réservé à l'utilisateur courant, liste blanche de commandes, aucune commande d'arrêt ni de suppression. Entrée PATH facultative à l'installation.
- Dock : bouton « + » pour créer un bureau virtuel ; clic droit sur une pastille pour fermer ce bureau ; une petite flèche affiche ou range, dans la barre, les boutons d'organisation automatique et d'applications ouvertes (état mémorisé).
- Le dossier de données passe à %LOCALAPPDATA%\Panneaux (L2).
- L'intégration du terminal est désormais facultative et réversible (L3).
- Préchargement de Files plus rapide ; correction du journal de démarrage (L4).
- Messageries : chargement à la première ouverture, fermeture après 10 min masquées. L'option d'arrière-plan est désactivée par défaut ; les compteurs de non-lus n'existent que lorsqu'elle est activée (L5).
- Textes FR/EN/ES : unités localisées (Go/Mo/Ko en français, GB/MB/KB sinon), état vide « Aucun média en lecture », jauges masquées quand la mesure est absente, nom d'utilisateur Windows affiché dans le tableau de bord à la place de « Panneaux », textes de l'atelier, de la corbeille et du dock traduits (L6).
