# Explorateur natif — intégration locale

Windows 11 x64 : aucune injection, aucun remplacement de fichier système, aucun processus Explorer arrêté.

Activation : Personnaliser > Explorateur Windows > Icônes et contours assortis. Désactivé par défaut dans les nouvelles préférences ; activé sur la machine de développement pour essai.

Portée : Bureau, Téléchargements, Documents, Images, Musique, Vidéos, puis dossiers choisis avec Ajouter un dossier. Pas de parcours récursif du disque. Les dossiers connus sont résolus sur chaque PC ; les jonctions explicites sont résolues avant modification, les emplacements système sont exclus.

Couleurs : palette du frontend, synchronisée toutes les quatre secondes uniquement lorsque ses valeurs ou la liste des dossiers changent. ICO 16/32/48/64/128/256 px (DIB pour les petites tailles, PNG pour 256 px), générations conservées en LocalAppData pour éviter les références cassées. Les contours DWM sont actualisés toutes les deux secondes ; les couleurs de titre peuvent être ignorées par les versions qui dessinent leur propre en-tête.

Restauration : Personnaliser > Explorateur Windows > Restaurer. Les desktop.ini originaux, leurs attributs et les attributs des dossiers sont conservés dans %LOCALAPPDATA%/Winlestia/Explorer/manifest.json. Une modification extérieure détectée est conservée et signalée ; le manifeste correspondant reste disponible. Les contours DWM reviennent à leur état initial à la fermeture de Winlestia. Les icônes persistent jusqu’à restauration explicite.

Rapport : %LOCALAPPDATA%/Winlestia/Explorer/last-report.txt. Cette intégration ne change ni le contenu opaque de l’Explorateur, ni ses menus, ni les icônes virtuelles gérées par Windows dans la navigation. Aucune transparence du contenu n’est annoncée.

Validation : tests Explorer077 sur dossiers temporaires (ICO valides, pixels réellement chargés par le Shell, changement de thème, retour exact des octets et attributs, conflits préservés, dossiers système exclus), tests QML du dock et de la barre de fichiers, contrôle visuel réel des six dossiers personnels. Les droits et politiques propres à d’autres ordinateurs peuvent empêcher certaines personnalisations ; le rapport les indique sans forcer l’accès.

Avant diffusion finale : conserver le code dans le vrai dépôt 0.7.7, vérifier le scénario installation/désinstallation et proposer la restauration avant suppression des ressources. Aucun installeur ni release GitHub n’a été créé pour cette itération.

Winlestia ne dépend d'aucune application tierce (pas de Windhawk) ; seules les personnalisations natives sont appliquées.
