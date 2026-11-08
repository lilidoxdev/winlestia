# Changelog

Format inspiré de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/). Les notes de chaque version sont aussi publiées sur la page GitHub Releases. Les entrées 0.9.0 et antérieures sont conservées dans la langue où elles ont été écrites.

## [Non publié]

### Modifications

- Intégration continue : contrôle de longueur de ligne (140), `dotnet format whitespace`, test de fumee de l'installateur, plancher de couverture documenté (règle du cliquet).
- Dépendances Python verrouillées par hachage (`src/frontend/requirements.lock.txt`), installées avec `--require-hashes` par `installer/build.ps1`.
- Historique du portage Caelestia condensé dans `docs/history/caelestia-windows.md`.

## [0.9.2] - 2026-11-06

### Ajouts

- Éditeur de palette dans les réglages (couleurs personnalisées).
- Page « Raccourcis clavier » : liste de tous les raccourcis globaux ; les raccourcis configurables peuvent être réaffectés (combinaisons réservées par Windows ou en conflit refusées avec explication, changement échoué annulé). Les raccourcis fixes indiquent la raison.
- Win+Alt+S ouvre les Réglages (réaffectable). Autres accès : menu de la zone de notification, `winlestia panel settings`.
- Icônes dans le dock pour les applications empaquetées.
- Mise à jour en direct du catalogue d'applications.
- Dock, panneau des signets et panneau social masquables séparément. Masquer le dock quand il remplace la barre des tâches rend la barre Windows.

### Modifications

- Réglages redessinés en huit pages (Apparence, Interface, Docks et panneaux, Raccourcis clavier, Média et audio, Apps/signets/liens, Fichiers et Windows, Général), avec recherche et colonne de navigation. Textes traduits en anglais et en espagnol.
- Le terminal suit le mode du thème (clair ou sombre).
- Gestionnaire de fichiers redessiné.
- « Ajouter un site web » retiré.
- L'instantané des réglages contient le dossier de données et la version.
- `WINLESTIA_DATA_DIR` est aussi pris en compte par le frontend (langue, météo, miniatures, journaux) et par le cache et les lancements récents du backend.
- L'installateur vérifie l'empreinte du Python téléchargé.

### Corrections

- Dictée vocale : Whisper.net revient sur .NET 8.
- Flèches affichées comme des cases vides dans la liste des raccourcis de mosaïque.
- Un raccourci non enregistré au démarrage était signalé « inchangé » ; l'enregistrement est retenté et le résultat réel affiché.
- Un raccourci Réglages identique à une autre combinaison est remplacé au chargement par la première libre parmi Win+Alt+S, Ctrl+Alt+F10, Ctrl+Alt+F11.
- Dock, signets et panneau social n'étaient plus masqués avant l'arrivée du premier instantané des réglages.

### Interne

- Source unique de la version : `Directory.Build.props`.
- CI : build, tests xUnit et pytest.
- Tests regroupés dans `tests/Winlestia.Tests` (xUnit) et pytest.
- Blocs `catch` vides classés : helper `Safe` et journalisation.
- Auto-tests du gestionnaire de fichiers exclus des builds Release.
- Logique pure du bail de barre des tâches extraite (`TaskbarLeasePolicy`) et testée (bail, repli « toujours visible », décision du gardien).
- Tests xUnit ajoutés (calculatrice, maths de couleur et palettes, options de lancement, mode silencieux, trame du tube de Fichiers, validation de cibles) ; helper `TempDir` à la place des nettoyages `catch { }`.
- CI : couverture coverlet avec résumé et plancher, syntaxe PowerShell de l'installateur, `compileall` Python.
- L'installateur refuse de se compiler sans `AppVersion` (plus de version par défaut codée en dur).
- Plans et journaux d'avancement terminés retirés de `docs/` (le texte de la release reste dans `docs/release-notes-0.9.2.md`, qui résume ce qui précède sans le reprendre).
- Code reformaté (`.editorconfig`, dotnet format, ruff, qmlformat), sans changement de comportement.
- Backend découpé en dossiers ; `frontend.py` découpé.
- Dépendances épinglées (fichiers de verrouillage, `global.json`).

## [0.9.1] - 2026-11-04

### Corrections

- Panneau média et visualiseur (#1) : navigateurs affichés sous un nom lisible, informations de piste relues au changement, position de lecture mise à jour tant que le tableau de bord est visible, visualiseur calé sur le navigateur qui joue (processus enfants compris).
- Rail social (#2) : le tableau de bord se décale de la largeur couverte par le rail et adapte ses volets ; sur une très petite largeur logique, le surplus passe par-dessus.
- Le tableau de bord rouvre sur le dernier onglet utilisé (durée de la session).
- Données utilisateur écrites de façon atomique (préférences, historique du presse-papiers, compteurs sociaux, éléments récents, bail de la barre des tâches, exclusions de la mosaïque, fichiers d'apparence d'Explorer, langue, fonds masqués, ville météo).
- Préférences : un fichier corrompu est conservé en `.corrupt.bak` et les réglages sont restaurés depuis `.bak` ; les valeurs par défaut ne servent que si les deux échouent. Une erreur de lecture transitoire est retentée une fois.
- Les signets n'ouvrent que des adresses `http` et `https`. Le XML de la tâche est écrit dans un dossier privé.

### Modifications

- Installateur en anglais par défaut, avec détection de la langue d'affichage de Windows ; français et espagnol conservés.
- Licence et notices de Caelestia installées avec l'application.
- Projet passé à .NET 10.
- Qualité du code : aucune ligne de plus de 500 caractères ; backend réparti en 12 espaces de noms ; textes français retirés du code (chaînes d'interface dans `Views/Strings.cs` en français, anglais et espagnol, journaux en anglais) ; normaliseur de noms d'applications à table de règles ordonnée ; CSharpier et ruff format ; `.editorconfig` et analyseurs ajoutés.

## [0.9.0] - 2026-10-31

Changes since 0.8.0.

### Added

- **Command line.** `winlestia` drives the running shell from scripts through a named pipe reserved to the current user, with an allow-list of commands and no stop or delete command: `status`, `panel`, `palette`, `desktop`, `wallpaper`, `find`, `brightness`, `visualizer`, `quiet`, `wifi`, `help`. Optional PATH entry at setup.
- **Wi-Fi panel** in the quick controls: network list (name, strength, lock, connected, remembered), connect with password, forget. Uses Native Wifi, scans only while the list is open, never stores or logs the password. Enterprise (802.1X) and hidden networks are not handled; a button opens Windows settings instead.
- **Brightness per display**, external displays included (DDC/CI), in the quick controls and through `winlestia brightness`. Brightness and microphone mute/level also appear in the on-screen indicator (OSD), next to volume.
- **Audio bars** in the dashboard Media panel, from a loopback capture of the default output that runs only while the panel is visible. Nothing is recorded.
- **Fullscreen quiet mode**: when a window covers a whole display, the dock, OSD, audio bars, animations and edge tracking are switched off and timers slow down, then everything returns on exit. Option in Settings (on by default); `winlestia quiet` to test.
- **Wallpaper switcher** (Win+Alt+W, fallback Ctrl+Alt+W): full-screen, live preview, bundled collection of 10 images, add, open and remove images. Per-display targeting with Tab, and a notice when DesktopHut, Wallpaper Engine, Lively or Wallpaper X is running.
- **Command palette** (Ctrl+F2): open windows, about thirty Windows Settings pages, indexed file search with the `>file ` prefix, `>emoji` picker, `>wallpaper` picker, calculator with an operator (including French words and locale decimals), and session actions (shut down, restart, sign out, lock, sleep, hibernate) that ask for confirmation.
- **Settings search** across the six pages by title and description, ignoring case and accents; the result opens the page and highlights the row.
- **Virtual desktops in the dock**: indicator, `+` button to create a desktop, right-click to close one, Win+Ctrl+arrow switching.
- **Automatic tiling** controls: stable order, keyboard focus, swap, resize and float, drag-to-swap, per-application exclusions; dock button to turn it on or off. Still off by default.
- **Accent border**: optional "active window only" mode.
- **"Reduced effects"** switch in the quick controls.
- **Terminal integration** is now optional (unchecked at setup) and reversible.

### Changed

- **Data folder** moved to `%LOCALAPPDATA%\Panneaux`, migrated at first launch. The name is the project's former name and is kept on purpose.
- **Start-up** uses a per-user scheduled task, `WinlestiaShell`, instead of only the Run registry value. The installer still writes the Run value; the first shell start creates the task and then removes the Run value (if the task cannot be created, the Run value stays as a fallback). Desktop icons are re-applied after Explorer events and restarts; the Windows taskbar waits for Explorer if it is not ready.
- **Wallpaper** is applied through the Windows desktop wallpaper interface (system transition, no extra window).
- **Dock**: collapsible group of buttons behind an arrow; windows of elevated programs are listed (activate or close only).
- **Recent applications**: a Start menu shortcut appears in the palette about three seconds after being added; Store apps are re-read when the palette opens if the catalogue is stale; icon extraction is retried a limited number of times.
- **Messengers** load on first opening and are released after being hidden unless background mode is on.
- **Performance**: panels are loaded on first opening then kept, rarely used windows on demand, the terminal and interface palettes are computed in short-lived sub-processes, the clock updates once a minute, ACPI and PDH probes stop after three failures, the Explorer appearance hook is event-driven, pointer and glass sync intervals adapt, and the recycle-bin drop zone loads on the first file drag.
- **Interface**: theme tokens, rounded corners from `Appearance`, spacing tokens in secondary panels, grouped inks; translated interface strings (French, English and Spanish), localized units (GB/MB/KB or Go/Mo/Ko), empty states, display name in the dashboard.
- **Wallpaper collection** replaced by 10 images supplied by the maintainer, shipped as unmodified original files (not re-encoded or resized). They are not covered by the GPL-3.0 licence; rights remain with their owners and their licence has not been established. See `assets/wallpapers/CREDITS.txt`; removal can be requested through a GitHub issue. Existing copies of the previous bundled images are purged on upgrade.
- **Automatic theme glass transparency** now defaults to 5 %. Profiles still holding the old default value are migrated once.
- **Terminal**: translucent dark console background, text and accent from the palette; palette sync differential on restore.
- Session menu actions require confirmation, checked on the backend as well.

### Fixed

- Panel text on bright wallpapers in the Automatic theme: with a dark scheme, the glass tint is denser when the wallpaper is bright so pale text stays legible. Darker wallpapers render as before.
- Desktop indicator refresh, OSD icon and tooltips.
- Settings theme tile read after its delegate was destroyed.
- Wallpaper palette not following a static wallpaper when a live-wallpaper configuration existed.
- Binding loop in the Media panel; missing `shortcutText` slot.
- Start-up log errors.
- Non-breaking space between a value and its unit.
- Setup no longer blocked by messengers still loaded in the background.
- Upgrading no longer aborts while the shell is running.
- The dashboard now shows the display name chosen at install (it used to show the Windows account name).
- Interface strings completed in English and Spanish; the dashboard calendar follows the app language.

### Known limitations

- The installer wizard is in French only.
- English and Spanish are translated for the shell panels; some rarer system-derived labels may still appear in French. The language follows the Windows display language and can be forced with the `language` key (`auto`/`fr`/`en`/`es`) in `%LOCALAPPDATA%\Panneaux\Caelestia\language.json`. Fallback text is French.
- Not tested on a clean machine or VM, nor after a real reboot or sign-in (the scheduled-task launch was tested manually).
- Binaries are **not signed**, so SmartScreen may warn.
- Windows 11 x64 only. Temperature is in Celsius only.
- There is **no notification centre**: only the on-screen indicator and toasts. Windows notifications can be switched off from the tools panel.
- Window preview quality in the dock depends on what each application exposes to Windows.
- Weather: with no city set, your approximate location comes from your IP address through wttr.in; the forecast comes from Open-Meteo. Temperature is in °C only.
- Bundled wallpapers: the 10 images were supplied by the maintainer and are not covered by the GPL-3.0 licence; rights remain with their owners (see `assets/wallpapers/CREDITS.txt`). A rights holder can ask for removal through a GitHub issue.
- Automatic tiling is **off by default**.
- Tested on a real Windows 11 Home machine with a single 3440x1440 display, not on a clean virtual machine. Other display layouts, scalings and Windows editions have had less testing.
- Enterprise (802.1X) and hidden Wi-Fi networks are not handled in the Wi-Fi panel.
- The optional terminal integration prints a welcome banner in French, showing your session and computer names.
- When you confirm a wallpaper in the switcher, the previous wallpaper can flash briefly before the new one appears.
- The wallpaper switcher also lists images from `Pictures\Wallpapers`, `Pictures` and the Windows wallpaper folders, not only the bundled set.
- CPU temperature can show N/A, depending on the hardware.

### What was tested

Tested by hand and by script on one Windows 11 Home machine (single 3440x1440 display), with the installer built from a clean checkout:

| Check | Result |
| --- | --- |
| Install, first start, dock replaces the taskbar | Pass |
| Upgrade from 0.8.0 with the shell running | Pass |
| Main panels open (settings, clipboard, tools, audio, mixer, capture, palette, dashboard) | Pass |
| Theme change and wallpaper-driven palette | Pass |
| Global shortcuts Ctrl+F1, Ctrl+F2, Ctrl+Alt+Space, Ctrl+Alt+W | Pass |
| Autostart task created, launched manually with the shell closed | Pass |
| Taskbar restored after normal close, after a killed process, and by `--restore-taskbar` and Ctrl+Alt+Backspace | Pass |
| Uninstall with the shell running (user profile and wallpaper kept) | Pass |
| Clean machine or VM; real reboot and sign-in | Not run |
| Other display layouts and scalings | Not run |

These tests ran from an MSIX-packaged parent process, which redirects `%LOCALAPPDATA%` writes for its child processes; autostart was exercised from Task Scheduler, outside that redirection.

## 0.8.0 - 2026-10-29

Nouveautés : panneaux social et favoris, palette automatique, page « Tout ton espace », gains de performance. Correctifs : onglets des Préférences défilants, badge WhatsApp, température CPU, masquage du dock, icône Ethernet, icône Fichiers dans le dock, aperçu des fenêtres au survol. Détail : page GitHub Releases (v0.8.0).

## 0.7.6 - date non relevée

- Réorganisation automatique animée sur 240 ms avec ralentissement progressif. Le minuteur d’animation s’arrête au repos ; aucun hook de souris permanent. Une manipulation, une réduction ou un agrandissement interrompt le mouvement concerné.
- Arrondis des fenêtres agrandies alignés sur leur surface visible DWM. Le découpage est réappliqué si une application le réinitialise, puis restauré au retour en fenêtre. Les vidéos et jeux en plein écran réel restent exclus.
- Logo du Bureau agrandi (116 px au lieu de 76 px, transparence et clics traversants conservés).
- Organisation automatique désactivée par défaut pour les nouvelles préférences. Les choix déjà enregistrés sont conservés.

Validation : tests natifs sur dix fenêtres dédiées (ouverture, réduction, restauration, fermeture, agrandissement, retour aux positions originales), partitions sur coordonnées négatives, réparation d’une région effacée par l’application, tests QML du logo et des interactions, 32 tests Python. Compilation autonome et installeur reconstruits.

Limites : le rendu des transitions dépend de la vitesse de redessin des applications. Les fenêtres protégées ou exécutées avec des privilèges supérieurs peuvent refuser un déplacement ou un découpage ; aucun résultat universel n’est garanti.

## 0.7.5 - date non relevée (organisation automatique du bureau)

### Mosaïque
Les fenetres redimensionnables se repartissent automatiquement dans la zone de travail de chaque moniteur, avec espacement et ordre stables. L'ouverture, la fermeture, la reduction et la restauration recalculent les emplacements. La disposition est activee par defaut ; un interrupteur dans Personnaliser > Bureau permet de la desactiver.

Le double-clic sur la barre de titre conserve l'agrandissement Windows. Tant qu'une fenetre geree est agrandie, sa disposition est preservee ; le bouton Restaurer permet de reprendre la mosaique. Une nouvelle application peut reouvrir la disposition partagee. Les jeux et videos qui couvrent vraiment un moniteur sont exclus du placement.

Les positions d'origine sont memorisees et restaurees a la desactivation ou a la fermeture normale pour les fenetres encore a leur emplacement gere. Les deplacements manuels, les fenetres reduites ou agrandies ne sont pas annules de force. Les boites de dialogue, outils flottants, fenetres fixes, bureaux virtuels masques et interfaces Winlestia sont exclus.

### Logo
Logo fourni affiche en blanc discret dans le coin superieur gauche de chaque ecran. Option Logo Winlestia, desactivee par defaut. Fond transparent, aucun clic intercepte, opacite attenuee suivant le verre. Masque en plein ecran et pendant les captures/pipette.

### Fluidite et limites
Detection legere toutes les 700 ms, sans hook souris global. Les placements qui ne changent pas ne sont pas reappliques ; un glisser-deposer en cours n'est pas interrompu. Les fenetres soumises a une taille minimale ne sont pas redimensionnees en boucle.

Le nombre gere simultanement est borne selon la taille du moniteur, jusqu'a 16 fenetres recentes par ecran. Les applications imposant de grandes tailles minimales ou un niveau d'administration superieur peuvent refuser un placement ; une organisation parfaite de toutes les applications Windows ne peut pas etre garantie. La restauration en cas de terminaison forcee du processus n'est pas garantie.

### Verification
Fixture native isolee : 3, 4 puis 10 vraies fenetres sans chevauchement, agrandir/restaurer, reduire/restaurer, fermer/reorganiser et restauration des positions initiales. Partitionnement de 1 a 16 cases avec coordonnees negatives. 30 cycles stables moyens sous 25 ms sur la machine de test ; ce n'est pas une mesure du PC du collegue.

Tests QML : logo visible, transparent aux clics et masque en plein ecran ; regressions grille et menus, plein ecran et DPI mixtes. Editeur de palette/raccourcis et 32 tests Python de regression passes. Aucun autre logiciel du bureau n'a ete deplace par la fixture : sa source ne contient que les fenetres de test.

## 0.7.0 - date non relevée (theme personnalise)

Quatrieme theme Personnalise, en plus de Porcelaine, Nuit et Automatique. Le studio reprend les cartes, arrondis et typographie du panneau Personnaliser.

- Cinq couleurs : fond, texte, principal, secondaire et troisieme accent. Les surfaces, contours et couleurs de contraste des boutons sont derives de ces roles. Palette initiale inspiree de Porcelaine.
- Roue HSV avec luminosite, saisie hexadecimale #RRGGBB et apercu avant application.
- Pipette d'ecran : interface masquee pendant l'echantillonnage d'un pixel ; clic intercepte temporairement sur un fil Windows dedie, sans hook permanent ni enregistrement d'image. Echap ou clic droit annule. Le hook est retire a la fin et a la fermeture.
- Verre optionnel : desactive, le fond choisi est opaque et le curseur disparait ; active, un curseur regle sa transparence. Le cadre, le dashboard et le dock utilisent les memes roles et le meme materiau.
- Couleurs et transparence personnalisees sauvegardees. Annuler l'editeur ne sauvegarde pas le brouillon. Le theme Automatique conserve son propre reglage de verre.
- Les terminaux reprennent la palette personnalisee ; Windows Terminal adapte aussi son opacite et son effet acrylique. Le mode allege conserve son comportement pour les configurations modestes.

Tests : palette et validation hexadecimale, roue/editeur QML sans erreur, modes opaque/verre, selection et annulation d'un pixel deterministe, persistance native des cinq roles et du verre, 32 regressions Python, import du service terminal et cycle de vie reel du hook temporaire dans le runtime embarque. Capture du studio inspectee. Les couleurs visibles peuvent dependre de la gestion HDR du bureau ; le rendu sur le second PC reste a tester.

## 0.6.5.2 - date non relevée

### Personnaliser
Refonte inspiree des composants, arrondis et couleurs de Caelestia : https://github.com/caelestia-dots/shell . Trois cartes Apparence / Bureau / Au quotidien, apercus de themes, interrupteurs, langues compactes et hierarchie typographique. Le selecteur d'animations est retire. La detection superieure dispose d'un bouton Calibrer unique ; aucun curseur redondant dans les parametres.

### Applications
Les onglets En cours / Arriere-plan utilisent une grille d'icones avec noms au survol. Clic gauche ou droit ouvre les actions. Les fermetures et arrets avec confirmation reviennent a la grille, dans le meme onglet, sans fermer le panneau. Retour et Echap restent disponibles. Le clic exterieur ferme le panneau ; aucun hook souris global n'est ajoute.

### Plein ecran
Le dock devient une capsule flottante complete quand son ecran accueille un vrai plein ecran. Le dessin, le verre et le masque de clic utilisent la meme forme detachee. Le cadre reste masque lorsque le dock ou son menu prend le focus.

La detection utilise les coordonnees physiques et le nom du moniteur Windows, sans multiplication des origines Qt par un facteur DPI. Une video plein ecran peut etre reconnue meme si le navigateur conserve son etat maximise. Une fenetre simplement maximisee avec barre de titre reste distincte. Le clip arrondi impose aux fenetres maximisees est retire lorsque leur client couvre le moniteur entier ; retour a l'arrondi a la sortie.

### Verification
32 tests Python de geometrie/recherche ; tests QML dans le runtime embarque : grille, clic droit, confirmation, maintien du panneau, clic exterieur, bouton de calibration, cadre masque, capsule et cas de DPI melanges simule ; fixture native : arrondis/restauration, epingles, recents. Aucune erreur QML sur ces parcours. Une capture du nouveau panneau a ete inspectee.

Le cas Firefox/Chrome du second PC reste a retester sur cette machine ; les tests locaux et simules ne couvrent pas toutes les versions de pilotes ou tous les lecteurs video. Les terminaux ne sont pas modifies par cette version.

## 0.6.5.1 - date non relevée

- Reparation de l'installation du theme PowerShell : Set-ExecutionPolicy pouvait modifier CurrentUser puis signaler ExecutionPolicyOverride a cause de la politique du processus du setup. Le helper verifie maintenant la valeur relue et accepte uniquement une modification effectivement reussie. Les restrictions de groupe restent respectees.
- Le theme CMD/PowerShell est inclus automatiquement, y compris lors d'une mise a jour ayant conserve une ancienne liste de taches.
- Synchronisation initiale de la palette avant le lancement du worker ; arret de l'ancien worker enregistre ; mutex propre au dossier d'installation. Les fichiers de couleurs sont publies meme si Windows Terminal n'a pas encore de configuration.
- Windows Terminal nouvellement initialise : ajout du schema Winlestia manquant et activation de son apparence. Les editions Preview sont aussi detectees. Le CMD classique utilise les couleurs RVB du theme ; Windows Terminal conserve ses couleurs indexees adaptatives.
- Ctrl+F2 : extraction des icones des applications et fichiers/dossiers recents, et demande de chargement des la creation des lignes.
- Echecs d'installation : stderr du helper est conserve dans install-result.json pour un diagnostic exploitable.

### Verification

PowerShell 5.1 interactif dans un vrai pseudo-terminal : logo ASCII, panneau systeme et prompt affiches, couleur rouge RVB verifiee, fermeture propre. Test reproduisant ExecutionPolicyOverride avec changement applique, puis restauration de la valeur precedente. Tests de palettes rouge/bleue et de configuration Terminal neuve ; test d'import du worker avec le Python embarque ; installation/reinstallation/restauration isolees ; regressions QML sans erreur.

Le rendu du fond transparent exige un hote compatible tel que Windows Terminal. Une strategie d'entreprise interdisant les profils non signes doit etre geree par son administrateur. Le PC du collegue reste a retester ; les tests locaux ne constituent pas une verification sur cette machine.

## 0.6.5 - date non relevée (correctifs et audit)

### Correctifs
- Raccourcis : fenetre independante, panneau principal masque, combinaison affichee pendant la saisie, validation backend, fermeture automatique uniquement apres succes et libelle synchronise. Echap annule.
- Langues : quatre cartes graphiques Windows/France/Royaume-Uni/Espagne, assets SVG locaux, selection persistante.
- Icones Workspace : demandes a la demande avec coalescence et nouvelles tentatives ; les images vectorielles de secours sont aussi rendues pour Qt ; encodage PNG hors du fil de saisie.
- Recents : applications observees au premier plan et raccourcis Windows Recent sont fusionnes par horodatage UTC ; dernier acces conserve, ordre decroissant, historique local borne. La collecte commence avec Winlestia et ne reconstitue pas un historique Windows absent ou desactive.
- Terminal : installation du profil accompagnee de RemoteSigned pour le compte courant lorsque necessaire, ancienne valeur sauvegardee puis restauree a la desinstallation. CMD lance son helper avec une politique limitee au processus. Les strategies imposees par une organisation restent prioritaires et sont signalees. Les settings Windows Terminal avec commentaires/virgules finales sont acceptes.
- Mise a jour/desinstallation : fermeture propre, puis nettoyage des seuls processus frontend/theme du dossier installe. Suppression du taskkill global par nom : les autres installations et applications Python restent intactes.

### Audit des performances
Defauts identifies : enumeration de tous les processus sur le dispatcher du dock ; icones demandees uniquement pour les favoris/dock ; encodeur d'images sur le dispatcher ; arret force du backend pouvant laisser son frontend orphelin ; listes recentes concatenees sans tri commun.

Corrections : inventaire des processus uniquement a l'ouverture du panneau dedie, sur une tache de fond ; un seul worker STA pour les recents, cache des liens et file bornee ; pas de Shell COM dans les requetes d'etat ; ecriture des icones en arriere-plan ; nettoyage du cycle de vie.

Mode allege automatique jusqu'a 8 Gio de memoire disponible pour le runtime, reglable dans Personnalisation : flou natif desactive, transparence conservee, sondes materielles moins frequentes. Le test sur le PC du collegue reste necessaire : aucune mesure sur cette machine n'est disponible et aucune garantie de fluidite universelle n'est annoncee.

### Verifications
- 32 tests Python de geometrie, recherche et palettes.
- QML du runtime embarque : popup seul, touche Ctrl+K capturee, succes simule, retour aux parametres/libelle, demandes d'icones coalescees, quatre cartes de langue ; aucune erreur QML.
- Fixture native : arrondis/restauration, ordre des epingles, recents mixtes tries et dedupliques.
- Capture audio : FFT variable et isolation du processus validees avec signal de test.
- Installation terminal isolee : profils, chemins portables, CMD AutoRun, reinstal idempotente, restauration, JSON avec commentaires ; banner reel lance sous environnement Restricted avec son option de processus.
- Nettoyage reel sur deux processus factices : seul le processus du dossier choisi est arrete.

Les dialogues secondaires herites ne sont pas tous traduits. L'estimation de ville reste reseau/IP. Les tests automatises ne remplacent pas une ouverture de session Windows ni l'essai sur la petite configuration.

Reference des politiques PowerShell : https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_execution_policies/

## 0.6.0 - date non relevée

La numerotation passe de 9.5.0 a 0.6.0 ; le setup accepte la mise a jour avec le meme identifiant.

- Spectre FFT dynamique a 48 bandes : capture WASAPI du processus du lecteur et de ses enfants, sans repli sur le son global. Analyse en memoire, aucun enregistrement. Test reel avec signal sinusoidal et processus exclu reussi.
- Menu alimentation : cartes avec icones, confirmation, position au-dessus du dock.
- Fluidite : retrait du hook souris global de fermeture des panneaux, remplacement par detection des transitions de boutons ; suppression des abonnements globaux bruyants aux changements de position/etat ; cadence du mixeur reduite. Le ralentissement du PC du collegue reste a confirmer sur cette machine, sa cause exacte n'est pas prouvee.
- Langues : detection de la langue d'affichage Windows (FR/EN/ES, anglais sinon), choix manuel persistant dans Personnalisation et premier catalogue de traduction de l'interface principale. Certains libelles secondaires et dialogues Windows/herites restent dans leur langue initiale ; la couverture n'est pas encore exhaustive.
- Meteo : en absence de ville manuelle, estimation de ville par adresse IP (wttr.in), puis meteo Open-Meteo. Ce n'est pas une localisation GPS ; VPN/reseau peuvent affecter la ville.
- Ctrl+F2 : raccourcis recents du dossier Windows Recent en tete ; applications observees au premier plan depuis le lancement sont aussi prises en compte. Les historiques Windows desactives/proteges ne sont pas contournes.
- Calibration : bouton Calibrer dans Personnalisation, panneau principal masque, reglage isole et ligne rouge non interactive sur l'ecran. Valider avec la coche ; Echap annule.

Tests : 32 tests Python ; runtime QML/menus sans erreur ; FFT/isolation native ; choix/persistance des langues et ordre recent d'abord. Le PC du collegue et une vraie ouverture de session restent a tester.

Implementation WASAPI : https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/

## 9.5.0 - date non relevée

- L'installateur embarque le theme CMD/PowerShell et propose son installation. Profils interactifs, ASCII Winlestia, Clink, couleurs, verre Windows Terminal et adaptation au fond d'ecran. Les reglages existants sont sauvegardes. Les sessions elevees du meme compte utilisent les memes profils.
- Media : bouton volume cible a la place du bouton de deplacement ; pochette tournante pendant la lecture, angle conserve en pause.
- Le visualiseur utilise le niveau des sessions audio du lecteur selectionne, sans repli sur la sortie globale. C'est un indicateur d'amplitude, pas une analyse FFT par frequence. Le volume d'un navigateur concerne ses sessions, pas un onglet individuel.
- Dock : separation des groupes epingles/ouverts, deplacement des epingles par glissement, largeur reglable au bord droit et centrage conserve.
- Applications : onglets en cours/arriere-plan ; executables accessibles de la session, groupes par chemin. Les processus proteges ou inaccessibles ne sont pas listes.
- Alimentation : veille, redemarrage et arret avec confirmation.
- Reglages : hauteur de detection du bord superieur de 0 a 160 pixels, persistante.
- Fenetres maximisees : decoupe arrondie dans la zone de travail ; region precedente restauree au retour en mode normal ou a la fermeture du shell. Les pleins ecrans exclusifs restent geres par leur application.
- Curseurs : protection des valeurs choisies contre les retours d'etat anciens pendant les commandes audio et luminosite.
- Mise a jour : conservation du catalogue et des preferences backend ; correctif Explorer 9.4 conserve.

### Verification

32 tests Python ; chargement QML et menus avec etat de test ; protection contre etat obsolete ; installation/restauration terminal dans un environnement isole ; fixture native pour arrondis et ordre des epingles ; verification Core Audio avec session silencieuse dediee. Aucune commande d'arret, redemarrage ou veille n'est executee par ces tests. Le demarrage de session Windows et un second ordinateur restent a tester manuellement.

Le setup est gratuit, Windows 11 x64. Le depot reste prive : les testeurs doivent disposer d'un acces GitHub.

