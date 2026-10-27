# Winlestia Terminal — Version 1.1

Mise à jour : opacité à 68 % au lieu de 72 %, démarrage CMD direct via le chargeur natif et configuration du contexte privé de Windows Terminal, en-têtes Microsoft effacés au premier prompt et couleurs synchronisées avec le thème Winlestia. Le panneau d’informations et le logo ASCII Winlestia sont conservés.

Le service local sync_theme.py surveille toutes les quatre secondes les réglages Winlestia et le fond d’écran. Il réutilise chromatic_palette.generate_palette et wallpaper_sources.desktop_hut_active du projet, sans capture de l’écran. Il reconnaît les fonds DesktopHut et les fonds Windows fixes. Sur plusieurs écrans, le premier fond actif est utilisé pour la palette globale du Terminal. Les couleurs sont recalculées uniquement quand la source ou le mode change.

La palette Windows Terminal, le logo, les accents du prompt et les commandes suivent le fond d’écran. Les accents utilisent des couleurs ANSI indexées pour que le Terminal puisse également recolorer les cellules déjà affichées. Les couleurs sémantiques (erreurs rouges, chaînes vertes) restent distinctes. Les profils existants et leurs droits d’élévation ne sont pas modifiés par les mises à jour de couleur.

Le processus de synchronisation est lancé sans fenêtre et redémarre avec la session via la valeur utilisateur WinlestiaTerminalTheme. Restore.ps1 arrête cette synchronisation puis rétablit les réglages initiaux, y compris cette valeur de démarrage. Les fichiers et réglages de la version 1.0 ont également été sauvegardés dans backups/v1.1-*.

Vérification 1.1 : CMD lancé directement sans /k charge Clink ; les appels CMD /c et PowerShell non interactifs restent silencieux. PowerShell affiche sa bannière après le premier nettoyage d’écran, ce qui efface aussi le message de durée de chargement. Les cas synthétiques rouge et bleu réutilisent exactement la palette Winlestia et conservent tous les profils et l’opacité choisie. La source active constatée est blue-frost-blade-live-wallpaper.mp4.

# Configuration initiale — Version 1.0

Habillage installé pour les nouvelles sessions Windows Terminal de l'utilisateur : verre acrylique sombre à 72 % d’opacité, palette lavande/cyan, Cascadia Code 12 pt, curseur fin, icône Windows/terminal et prompt sur deux lignes. Le titre ASCII Winlestia est accompagné du système, de la session et de ses droits, du nom de machine, du shell, de la mémoire et du temps de fonctionnement.

Les profils existants sont conservés. Deux profils ont été ajoutés : Winlestia · PowerShell Admin et Winlestia · CMD Admin. Le profil normal par défaut reste Windows PowerShell. Les réglages de style sont partagés par les autres profils du Terminal, y compris les profils de développement. Windows Terminal a été désigné comme hôte par défaut pour les nouvelles consoles de ce compte Windows.

## Shells

- PowerShell 5.1 et PowerShell 7 : profils utilisateur communs aux hôtes ConsoleHost, coloration PSReadLine 2.4.5, suggestions provenant de l’historique, Tab pour les complétions, flèches pour rechercher dans l’historique. La bannière et le prompt distinguent Standard et Administrateur.
- CMD : Clink 1.9.35 installé localement, coloration des commandes et des arguments, suggestions et même style de prompt. Le démarrage passe par AutoRun de l’utilisateur ; aucun AutoRun système n’a été modifié.
- Les exécutions de scripts PowerShell, les sorties redirigées et CMD /c ne reçoivent pas la bannière. PowerShell -NoProfile et CMD /d contournent volontairement les profils de démarrage. Les sessions déjà ouvertes doivent être rouvertes.
- Les shells Linux/WSL héritent du verre et des couleurs de Windows Terminal ; leurs fichiers de configuration Linux n’ont pas été modifiés. Le Console Host historique imposé par une application ne peut pas recevoir l’acrylique de Windows Terminal.

## Fichiers

Banner.ps1 contient le logo et les informations locales. Profile.ps1 contient l’habillage PowerShell. clink-profile/ contient l’habillage CMD. settings.winlestia.json est une copie des réglages installés. Les scripts PowerShell sont en UTF-8 avec BOM pour rester compatibles avec Windows PowerShell 5.1.

## Vérification et retour

Essais interactifs PowerShell 5.1 et CMD dans un pseudo-terminal, avec couleurs ANSI effectivement émises. PowerShell utilise PSReadLine 2.4.5 et History sans erreur. Les exécutions non interactives produisent uniquement leur sortie attendue. Un aperçu réel du Terminal a été observé à l’écran. Les propriétés utilisées ont été contrôlées dans le schéma Microsoft. Le ZIP Clink correspond au SHA-256 publié pour la version officielle.

La sauvegarde des réglages initiaux et des seules valeurs de registre modifiées est indiquée dans backup-location.txt. Restore.ps1 rétablit les réglages sauvegardés avec confirmation. Il conserve les autres lignes ajoutées ultérieurement aux profils créés par cette installation, mais rétablit entièrement le fichier settings.json sauvegardé.

Sources : [apparence Windows Terminal](https://learn.microsoft.com/en-us/windows/terminal/customize-settings/profile-appearance), [thèmes Windows Terminal](https://learn.microsoft.com/en-us/windows/terminal/customize-settings/themes), [PSReadLine](https://learn.microsoft.com/en-us/powershell/module/psreadline/set-psreadlineoption), [Clink](https://chrisant996.github.io/clink/clink.html).

### Démarrage CMD dans Windows Terminal

Les essais ont mis en évidence deux vues différentes du registre : les CMD lancés directement voyaient AutoRun, mais ceux lancés par Windows Terminal ne le voyaient pas. ConfigurePackagedCmd.ps1 a donc configuré cette valeur depuis un processus enfant du Terminal. Un nouveau CMD lancé par le Terminal charge désormais Clink (PID 10092 lors du contrôle), comme un CMD lancé directement (PID 30336). La sauvegarde du contexte privé se trouve dans backups/packaged-autorun-before.json.

Pour une restauration complète des deux contextes, lancer Restore.ps1 depuis un PowerShell ouvert directement par Windows, par exemple depuis Exécuter avec powershell.exe -NoProfile -File suivi du chemin du script. Le script ouvre un processus de restauration dans le contexte du Terminal, puis rétablit les valeurs ordinaires. La virtualisation des applications MSIX est documentée par Microsoft : https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes.
