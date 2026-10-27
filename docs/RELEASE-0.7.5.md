# Winlestia 0.7.5 — organisation automatique du bureau

## Mosaïque
Les fenetres redimensionnables se repartissent automatiquement dans la zone de travail de chaque moniteur, avec espacement et ordre stables. L'ouverture, la fermeture, la reduction et la restauration recalculent les emplacements. La disposition est activee par defaut ; un interrupteur dans Personnaliser > Bureau permet de la desactiver.

Le double-clic sur la barre de titre conserve l'agrandissement Windows. Tant qu'une fenetre geree est agrandie, sa disposition est preservee ; le bouton Restaurer permet de reprendre la mosaique. Une nouvelle application peut reouvrir la disposition partagee. Les jeux et videos qui couvrent vraiment un moniteur sont exclus du placement.

Les positions d'origine sont memorisees et restaurees a la desactivation ou a la fermeture normale pour les fenetres encore a leur emplacement gere. Les deplacements manuels, les fenetres reduites ou agrandies ne sont pas annules de force. Les boites de dialogue, outils flottants, fenetres fixes, bureaux virtuels masques et interfaces Winlestia sont exclus.

## Logo
Logo fourni affiche en blanc discret dans le coin superieur gauche de chaque ecran. Option Logo Winlestia, desactivee par defaut. Fond transparent, aucun clic intercepte, opacite attenuee suivant le verre. Masque en plein ecran et pendant les captures/pipette.

## Fluidite et limites
Detection legere toutes les 700 ms, sans hook souris global. Les placements qui ne changent pas ne sont pas reappliques ; un glisser-deposer en cours n'est pas interrompu. Les fenetres soumises a une taille minimale ne sont pas redimensionnees en boucle.

Le nombre gere simultanement est borne selon la taille du moniteur, jusqu'a 16 fenetres recentes par ecran. Les applications imposant de grandes tailles minimales ou un niveau d'administration superieur peuvent refuser un placement ; une organisation parfaite de toutes les applications Windows ne peut pas etre garantie. La restauration en cas de terminaison forcee du processus n'est pas garantie.

## Verification
Fixture native isolee : 3, 4 puis 10 vraies fenetres sans chevauchement, agrandir/restaurer, reduire/restaurer, fermer/reorganiser et restauration des positions initiales. Partitionnement de 1 a 16 cases avec coordonnees negatives. 30 cycles stables moyens sous 25 ms sur la machine de test ; ce n'est pas une mesure du PC du collegue.

Tests QML : logo visible, transparent aux clics et masque en plein ecran ; regressions grille et menus, plein ecran et DPI mixtes. Editeur de palette/raccourcis et 32 tests Python de regression passes. Aucun autre logiciel du bureau n'a ete deplace par la fixture : sa source ne contient que les fenetres de test.
