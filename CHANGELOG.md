# Journal des modifications

## Non publié

- Palette Ctrl+F2 : fenêtres ouvertes (5 au plus, listées à l'ouverture et oubliées à la fermeture), une trentaine de pages des Paramètres Windows (FR/EN/ES) et préfixe `>file ` pour chercher des fichiers du profil dans l'index Windows (requête hors thread UI, 2 s, 200 ms d'anti-rebond, message si l'index est indisponible ; ni saisie ni chemins journalisés). `winlestia find windows` et `winlestia find files <terme>` ne renvoient que des comptes.

- Réglage « Seulement la fenêtre active » (sous « Liseré sur les applications », désactivé par défaut) : le liseré ne colore que la fenêtre au premier plan ; `winlestia status` expose `activeBorderOnly`.
- Commande `winlestia` (status, panel, desktop, wallpaper, palette, help) pour piloter le shell déjà lancé depuis un script ; tube nommé local réservé à l'utilisateur courant, liste blanche de commandes, aucune commande d'arrêt ni de suppression. Entrée PATH facultative à l'installation.
- Dock : bouton « + » pour créer un bureau virtuel ; clic droit sur une pastille pour fermer ce bureau ; une petite flèche affiche ou range, dans la barre, les boutons d'organisation automatique et d'applications ouvertes (état mémorisé).
- Le dossier de données passe à %LOCALAPPDATA%\Panneaux (L2).
- L'intégration du terminal est désormais facultative et réversible (L3).
- Préchargement de Files plus rapide ; correction du journal de démarrage (L4).
- Messageries : chargement à la première ouverture, fermeture après 10 min masquées. L'option d'arrière-plan est désactivée par défaut ; les compteurs de non-lus n'existent que lorsqu'elle est activée (L5).
- Textes FR/EN/ES : unités localisées (Go/Mo/Ko en français, GB/MB/KB sinon), état vide « Aucun média en lecture », jauges masquées quand la mesure est absente, nom d'utilisateur Windows affiché dans le tableau de bord à la place de « Panneaux », textes de l'atelier, de la corbeille et du dock traduits (L6).
