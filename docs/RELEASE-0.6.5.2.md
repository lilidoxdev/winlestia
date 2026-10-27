# Winlestia 0.6.5.2

## Personnaliser
Refonte inspiree des composants, arrondis et couleurs de Caelestia : https://github.com/caelestia-dots/shell . Trois cartes Apparence / Bureau / Au quotidien, apercus de themes, interrupteurs, langues compactes et hierarchie typographique. Le selecteur d'animations est retire. La detection superieure dispose d'un bouton Calibrer unique ; aucun curseur redondant dans les parametres.

## Applications
Les onglets En cours / Arriere-plan utilisent une grille d'icones avec noms au survol. Clic gauche ou droit ouvre les actions. Les fermetures et arrets avec confirmation reviennent a la grille, dans le meme onglet, sans fermer le panneau. Retour et Echap restent disponibles. Le clic exterieur ferme le panneau ; aucun hook souris global n'est ajoute.

## Plein ecran
Le dock devient une capsule flottante complete quand son ecran accueille un vrai plein ecran. Le dessin, le verre et le masque de clic utilisent la meme forme detachee. Le cadre reste masque lorsque le dock ou son menu prend le focus.

La detection utilise les coordonnees physiques et le nom du moniteur Windows, sans multiplication des origines Qt par un facteur DPI. Une video plein ecran peut etre reconnue meme si le navigateur conserve son etat maximise. Une fenetre simplement maximisee avec barre de titre reste distincte. Le clip arrondi impose aux fenetres maximisees est retire lorsque leur client couvre le moniteur entier ; retour a l'arrondi a la sortie.

## Verification
32 tests Python de geometrie/recherche ; tests QML dans le runtime embarque : grille, clic droit, confirmation, maintien du panneau, clic exterieur, bouton de calibration, cadre masque, capsule et cas de DPI melanges simule ; fixture native : arrondis/restauration, epingles, recents. Aucune erreur QML sur ces parcours. Une capture du nouveau panneau a ete inspectee.

Le cas Firefox/Chrome du second PC reste a retester sur cette machine ; les tests locaux et simules ne couvrent pas toutes les versions de pilotes ou tous les lecteurs video. Les terminaux ne sont pas modifies par cette version.
