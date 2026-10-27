# Winlestia 0.7.6

- Réorganisation automatique animée sur 240 ms avec ralentissement progressif. Le minuteur d’animation s’arrête au repos ; aucun hook de souris permanent. Une manipulation, une réduction ou un agrandissement interrompt le mouvement concerné.
- Arrondis des fenêtres agrandies alignés sur leur surface visible DWM. Le découpage est réappliqué si une application le réinitialise, puis restauré au retour en fenêtre. Les vidéos et jeux en plein écran réel restent exclus.
- Logo du Bureau agrandi (116 px au lieu de 76 px, transparence et clics traversants conservés).
- Organisation automatique désactivée par défaut pour les nouvelles préférences. Les choix déjà enregistrés sont conservés.

Validation : tests natifs sur dix fenêtres dédiées (ouverture, réduction, restauration, fermeture, agrandissement, retour aux positions originales), partitions sur coordonnées négatives, réparation d’une région effacée par l’application, tests QML du logo et des interactions, 32 tests Python. Compilation autonome et installeur reconstruits.

Limites : le rendu des transitions dépend de la vitesse de redessin des applications. Les fenêtres protégées ou exécutées avec des privilèges supérieurs peuvent refuser un déplacement ou un découpage ; aucun résultat universel n’est garanti.
