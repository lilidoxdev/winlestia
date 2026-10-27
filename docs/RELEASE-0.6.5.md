# Winlestia 0.6.5 — correctifs et audit

## Correctifs
- Raccourcis : fenetre independante, panneau principal masque, combinaison affichee pendant la saisie, validation backend, fermeture automatique uniquement apres succes et libelle synchronise. Echap annule.
- Langues : quatre cartes graphiques Windows/France/Royaume-Uni/Espagne, assets SVG locaux, selection persistante.
- Icones Workspace : demandes a la demande avec coalescence et nouvelles tentatives ; les images vectorielles de secours sont aussi rendues pour Qt ; encodage PNG hors du fil de saisie.
- Recents : applications observees au premier plan et raccourcis Windows Recent sont fusionnes par horodatage UTC ; dernier acces conserve, ordre decroissant, historique local borne. La collecte commence avec Winlestia et ne reconstitue pas un historique Windows absent ou desactive.
- Terminal : installation du profil accompagnee de RemoteSigned pour le compte courant lorsque necessaire, ancienne valeur sauvegardee puis restauree a la desinstallation. CMD lance son helper avec une politique limitee au processus. Les strategies imposees par une organisation restent prioritaires et sont signalees. Les settings Windows Terminal avec commentaires/virgules finales sont acceptes.
- Mise a jour/desinstallation : fermeture propre, puis nettoyage des seuls processus frontend/theme du dossier installe. Suppression du taskkill global par nom : les autres installations et applications Python restent intactes.

## Audit des performances
Defauts identifies : enumeration de tous les processus sur le dispatcher du dock ; icones demandees uniquement pour les favoris/dock ; encodeur d'images sur le dispatcher ; arret force du backend pouvant laisser son frontend orphelin ; listes recentes concatenees sans tri commun.

Corrections : inventaire des processus uniquement a l'ouverture du panneau dedie, sur une tache de fond ; un seul worker STA pour les recents, cache des liens et file bornee ; pas de Shell COM dans les requetes d'etat ; ecriture des icones en arriere-plan ; nettoyage du cycle de vie.

Mode allege automatique jusqu'a 8 Gio de memoire disponible pour le runtime, reglable dans Personnalisation : flou natif desactive, transparence conservee, sondes materielles moins frequentes. Le test sur le PC du collegue reste necessaire : aucune mesure sur cette machine n'est disponible et aucune garantie de fluidite universelle n'est annoncee.

## Verifications
- 32 tests Python de geometrie, recherche et palettes.
- QML du runtime embarque : popup seul, touche Ctrl+K capturee, succes simule, retour aux parametres/libelle, demandes d'icones coalescees, quatre cartes de langue ; aucune erreur QML.
- Fixture native : arrondis/restauration, ordre des epingles, recents mixtes tries et dedupliques.
- Capture audio : FFT variable et isolation du processus validees avec signal de test.
- Installation terminal isolee : profils, chemins portables, CMD AutoRun, reinstal idempotente, restauration, JSON avec commentaires ; banner reel lance sous environnement Restricted avec son option de processus.
- Nettoyage reel sur deux processus factices : seul le processus du dossier choisi est arrete.

Les dialogues secondaires herites ne sont pas tous traduits. L'estimation de ville reste reseau/IP. Les tests automatises ne remplacent pas une ouverture de session Windows ni l'essai sur la petite configuration.

Reference des politiques PowerShell : https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_execution_policies/
