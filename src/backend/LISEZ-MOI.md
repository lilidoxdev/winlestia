# Panneaux — dj

Un tableau de bord Windows natif, une barre des tâches personnalisée, quatre panneaux redimensionnables et les icônes d’origine des applications. Le fond d’écran et les fichiers restent à leur emplacement.

## Nouveautés 7.0

- Barre en bas : navigation, recherche, Démarrer, favoris épinglés, fenêtres ouvertes, volume, réseau, notifications et horloge. Clic droit sur un accès pour l’épingler ou le détacher.
- La poignée s’ouvre à gauche. Ctrl F1 reste personnalisable ; Ctrl F2 conserve la recherche globale.
- Accueil, Musique, Performances et Espaces sont de vraies vues WPF reliées aux services Windows. Les récents, favoris, sites et outils existants restent accessibles.
- Deux nouveaux thèmes : Atelier clair et Atelier sombre. Les autres thèmes sont conservés.
- Mouvements centralisés : transitions interruptibles, onglet souligné mobile, retour des boutons, menus et rotation de la pochette pendant la lecture. Les effets suivent le réglage de mouvements réduits.
- La page Performances ne charge que les sondes qu’elle présente. Les mesures complètes restent dans « Toutes les mesures ».
- **RETABLIR_BARRE_WINDOWS.exe** réaffiche la barre Windows ; un gardien natif la restaure aussi si le processus Panneaux s’arrête brutalement. La case correspondante dans Personnaliser permet de basculer entre les deux barres.

Validation, mesures et limites : [VALIDATION_7.0.md](VALIDATION_7.0.md).

## Nouveautés 6.5

- **L’accueil respire** : le champ « rechercher un accès, un calcul, une action » a disparu. La recherche vit désormais dans la commande rapide **Ctrl F2**, disponible partout, et l’accueil rend toute la place aux favoris et à **Reprendre**.
- **Les vraies icônes dans la commande rapide** : chaque application, jeu, dossier ou site retrouve son logo d’origine dans les résultats, au lieu d’un pictogramme générique.
- **Quatre nouvelles matières** en plus des six verres : **Papier calque**, **Encre**, **Affiche** et **Brume**. Ces thèmes ne changent pas seulement la couleur : ils changent la matière (opaque ou verre), la typographie des titres, l’arrondi, l’épaisseur des traits et la **disposition de l’accueil** (grille d’icônes, fiches ou liste).
- Toutes les fonctions restent identiques : les thèmes ne touchent qu’à l’apparence et s’appliquent à **toutes** les fenêtres, y compris les dialogues ouverts.

## Nouveautés 6.4

- **Le papier pense-bête est persistant** : une feuille collée à l’écran où tu écris directement, en police manuscrite. Son texte, sa largeur, sa place et sa transparence sont gardés et reviennent au démarrage suivant.
- **Molette** sur la feuille pour la redimensionner, **Ctrl + molette** pour la rendre plus ou moins transparente (35 % à 100 %), **glisser le ruban** pour la déplacer.
- La croix au survol détache la feuille et supprime son image ; une capture épinglée conserve aussi sa position et sa taille.
- Nouveau papier depuis **Atelier → Papier pense-bête**, la commande rapide (« papier ») ou un onglet rapide `glass:memo`.
- Quand l’espace est ouvert, les papiers restent lisibles au-dessus des panneaux, entre la barre du haut et le contenu, atténués et **transparents aux clics** : aucune navigation n’est gênée. Ils redeviennent pleinement manipulables dès que l’espace se replie, et s’effacent derrière un dialogue de Panneaux.

## Nouveautés 6.3

- **L’atelier** : un bouton sur l’accueil ouvre tous les outils du quotidien en cartes — capture annotée, texte d’une image, pipette, règle, presse-papiers, son et micro, profils d’espace, gros fichiers, épinglage, notifications, Explorateur.
- **Capture annotée** : l’écran se fige, tu encadres, tu dessines (stylo, surligneur, gomme, annulation), puis tu copies, enregistres en PNG, lis le texte ou **épingles la capture sur le bureau**.
- **Papier épinglé** : la capture devient une feuille tenue par un ruban, posée au-dessus de toutes les fenêtres, déplaçable, redimensionnable à la molette, refermable d’un clic. Elle tombe et se redresse à l’ouverture.
- **Texte d’une image** : encadre un texte, il est reconnu par Windows, copié et ouvert dans une fenêtre modifiable.
- **Presse-papiers visuel** : tout ce que tu as copié — texte et images — conservé localement, avec épinglage, recherche et remise en copie. Réglable dans **Personnaliser → Le quotidien**.
- **Pipette et règle** : code hexadécimal sous le curseur, distance en pixels et en centimètres, partout sur le bureau.
- **Toujours au premier plan** (Ctrl F8 par défaut), **son et micro** en un clic, **notifications** coupées ou rétablies, **Explorateur** redémarré, **profils d’espace** qui rouvrent applications et disposition, **gros fichiers** classés par taille.
- **Raccourcis réglables** : Ctrl F1 (espace), Ctrl F2 (commande rapide), Ctrl F3 (capture), Ctrl F8 (épinglage). Chacun se change dans Personnaliser ou Onglets rapides ; un conflit est expliqué et l’ancien reste actif.
- **Mouvement Calme / Standard / Expressif**, appliqué à toute l’application, avec révélation par le flou, entrées échelonnées et fondu croisé des thèmes.

## Nouveautés 6.2

- **Dix thèmes globaux, deux familles** : six verres (Nuit boréale, Améthyste, Émeraude, Cuivre, Graphite, Sakura) et quatre matières (Papier calque, Encre, Affiche, Brume). Chaque thème choisit sa matière, ses halos, ses reflets, sa typographie, ses arrondis et sa disposition, sur **toutes** les fenêtres. Aucune fonction ne change.
- **Sonnerie de fin de focus** : un carillon de trois notes montantes, synthétisé par l’application, plus présent que le son de notification Windows. Un bouton dans le minuteur permet de l’écouter à l’avance.
- **Durée libre en minutes, heures ou secondes** : le minuteur affiche l’heure de fin exacte et accepte de quelques secondes à vingt-quatre heures.
- **Replier l’espace ferme tout** : note, minuteur en cours de réglage, onglets rapides et personnalisation disparaissent avec les panneaux, au clic extérieur comme au raccourci.

## Nouveautés 6.1

- Une **bande de verre** au-dessus de l’accueil affiche l’heure en grand, les secondes et la date. Elle s’éteint avec l’espace et suit le pointeur d’un reflet.
- **Ctrl F2** ouvre la **commande rapide** depuis n’importe quelle application, même launcher fermé. Le raccourci se change dans **Onglets rapides** (Ctrl ou Alt, une touche, Appliquer).
- La palette se ferme **au clic à l’extérieur**, avec **Échap** ou avec sa croix. Elle ne reste jamais posée sur l’écran.
- Le champ couvre douze **actions** en plus des accès : accueil, espace complet, note, minuteur, onglets rapides, personnalisation, commandes Windows, état du PC, capture, calculatrice, verrouillage, gestionnaire des tâches.
- Les **onglets rapides** au-dessus du champ se personnalisent : ajouter, renommer, changer l’icône, choisir une vue de Panneaux ou une application, réordonner, retirer. Douze au maximum.
- **Minuteur réglable** : un clic sur Minuteur ouvre le choix de la durée (5, 10, 15, 25, 45, 60, 90 minutes ou une durée libre) avec l’heure de fin annoncée.
- **Reprendre** occupe toute la largeur de l’accueil : les dix derniers accès ouverts depuis Panneaux, en deux colonnes. La zone Explorer a disparu.
- La recherche accepte un **calcul** (`12*7`, `2 400 / 60`) et **Entrée copie le résultat** ; sans correspondance, Entrée propose une recherche web.
- **Note rapide** : un bloc-notes persistant, enregistré automatiquement, copiable par Ctrl + Entrée. **Actions rapides** dans le centre de contrôle : verrouiller, capture, corbeille, alimentation.
- **Ctrl 1–4** ouvre directement un panneau, **Ctrl F** place le curseur dans la recherche, **Ctrl H** revient à l’accueil.

## Ouvrir l’espace

Approcher le pointeur du **bord droit** pendant 0,15 seconde. Une grande capsule de verre apparaît. Un clic ouvre un grand widget d’accueil avec une courte transition graphique. L’ouverture utilise une transition courte du contenu ; les panneaux se recentrent ensemble lorsqu’une surface latérale est ajoutée.

**Ctrl + F1** ouvre ou replie l’espace, même si la poignée du bord est masquée. Par défaut, il ouvre l’accueil et les commandes Windows. Dans **Personnaliser → Ouvrir depuis n’importe quelle application**, choisir Ctrl/Alt/Maj, une touche et la vue à ouvrir, puis **Appliquer le raccourci**. Une combinaison déjà occupée est refusée en conservant la précédente. **Ctrl + Alt + Espace** reste disponible. **Échap** le replie. Un clic à l’extérieur ou le passage à une autre application replie l’espace. Dans la vue complète, le bouton d’épingle empêche le repli après 1,8 seconde passée à l’extérieur ; il conserve la fermeture au clic extérieur. Le bord se met en pause devant une fenêtre en plein écran.

Le panneau reste entièrement masqué au repos. Le démarrage automatique intervient à l’ouverture de la session Windows.

## Accueil

Les favoris sont classés par dernière ouverture **depuis Panneaux**. Les favoris jamais ouverts restent dans leur ordre d’ajout. Les boutons Tout, Applications, Jeux, Projets et Documents limitent la vue à un espace. La recherche a quitté l’accueil : **Ctrl F2** ouvre la commande rapide, qui parcourt tous les accès, les sites et les actions, et affiche le vrai logo de chaque application.

**Reprendre** occupe la largeur restante et présente les dix derniers accès ouverts depuis Panneaux, les plus récents en premier. Les sites épinglés restent accessibles au bas du widget ; clic droit sur leur logo pour les modifier. Le chevron **‹** à gauche ouvre l’état du PC. **Tout mon espace** ouvre la vue complète. Le chevron **›** à droite ajoute le centre de contrôle Windows et recentre les deux panneaux ensemble. Un nouveau clic le replie. Le bouton maison du bandeau ramène à l’accueil. Le bord ouvre l’accueil ; le raccourci utilise la vue choisie dans les paramètres, ou la dernière vue si cette option est sélectionnée.

## Centre de contrôle Windows

La flèche de droite de l’accueil ouvre un second panneau : connexions, musique, volume, luminosité et raccourcis Windows. Le panneau principal reste visible. La paire se recentre avec une transition nominale de 175 ms ; la préférence de durée des animations reste respectée.

- **Volume** : glisser la capsule, utiliser la molette ou les flèches du clavier. Le réglage pilote la sortie audio Windows par défaut. Le bouton de son permet de couper/réactiver le son ; le nom de la sortie est affiché et son bouton ouvre le choix dans Windows.
- **Luminosité** : même geste, sur l’écran où se trouve le panneau. Le contrôle utilise la luminosité matérielle DDC/CI quand l’écran la fournit. En cas d’indisponibilité, le curseur est désactivé et le lien Luminosité ouvre les réglages écran.
- **Musique** : titre, artiste, pochette, lecture/pause, précédent/suivant et progression issus des sessions Windows. Spotify est prioritaire lorsqu’il joue. Le bouton **…** choisit une autre source ou le mode automatique. Les commandes non prises en charge par l’application musicale sont désactivées.
- **Connexions** : les boutons Wi-Fi et Bluetooth reflètent les radios réelles et permettent leur bascule si Windows l’autorise. Un clic droit ouvre la page Windows correspondante. Une radio absente est signalée ; son bouton permet d’ouvrir les réglages. L’état Internet apparaît dans l’en-tête et sur la tuile Réseau.
- **Concentration** : le bouton en forme de lune ouvre les réglages Windows correspondants.
- **Raccourcis** : Capture d’écran, Calculatrice, Horloge, Notes, Fichiers, Audio et Réglages sont proposés. Ils replient le launcher avant d’ouvrir la commande Windows. Capture d’écran laisse un court délai pour que le launcher disparaisse de l’image.

**Modifier** permet de masquer et réordonner les quatre modules, masquer individuellement les curseurs, ajouter/éditer/retirer et réordonner les raccourcis. La bibliothèque fournit des pages de paramètres Windows et des applications classiques. Un raccourci personnalisé accepte son propre nom, une icône, une adresse `ms-settings:` ou une application, avec arguments facultatifs. Parcourir permet de choisir un exécutable ou un fichier .lnk. Les changements sont enregistrés automatiquement ; les raccourcis ne déplacent ni ne suppriment leurs fichiers cibles.

Le centre se replie avec l’espace lors d’un clic extérieur. Ses lectures périodiques et ses abonnements musicaux s’arrêtent lorsqu’il est masqué. Les modules désactivés ne sont pas interrogés pendant leur masquage.

## Quatre panneaux

Le panneau **État du PC**, à gauche de l’accueil, affiche la charge CPU/GPU, la mémoire utilisée, les températures exposées par le processeur, la carte graphique et les barrettes, l’activité et l’espace libre des disques, leurs températures, et les débits réseau reçus/envoyés. Les courbes gardent au maximum 60 relevés. Les valeurs indisponibles restent signalées. Les sondes CPU/RAM passent par LibreHardwareMonitor et PawnIO ; les NVIDIA compatibles utilisent directement NVML. Les droits et capteurs disponibles peuvent varier selon le PC. La collecte s’arrête lorsque ce panneau est masqué. Sur écran étroit, ouvrir un panneau latéral replie l’autre.

- **Applications** : sélection quotidienne, IA, développement, création et toutes les autres catégories.
- **Jeux** : jeux installés, catégories par genre et favoris. Rocket League reste identifié comme une installation à terminer dans Epic.
- **Projets** : cheats, reverse engineering, bots, trading, IA et développement.
- **Documents & médias** : Canada, administratif, images, vidéos, musique, archives, sauvegardes et disques.

La recherche du bandeau est commune aux quatre panneaux et ignore les accents. Elle traverse les filtres de catégories. Canada et ses dossiers Assurance Voiture et CEGEP sont directement recherchables. Entrée ouvre le premier résultat ; une recherche vide ne lance rien.

Glisser le titre pour déplacer un panneau. Étirer ses bords ou ses quatre coins pour changer sa taille. Position et taille sont enregistrées dès que la souris est relâchée et adaptées à la résolution de l’écran. Une taille minimale garde les commandes accessibles. Le bouton de grille dans le bandeau rétablit la disposition des quatre panneaux.

L’étoile en haut de chaque panneau affiche uniquement **les favoris de ce panneau**, indépendamment des autres. Cliquer de nouveau sur l’étoile revient au filtre précédent. Choisir une catégorie ou un filtre rapide quitte cette vue de favoris.

Les flèches ou la molette font défiler les pages d’un panneau. Les petits filtres et la liste de catégories affinent son contenu.

## Thèmes

Dans **Personnaliser → Thèmes**, dix cartes s’appliquent d’un clic, en direct, à toutes les surfaces déjà ouvertes. Chaque carte montre sa famille et une miniature de sa disposition.

**Les verres**

- **Nuit boréale** — bleu profond, halos cyan, le verre d’origine.
- **Améthyste** — prune et violet, une bande de lumière en travers du verre.
- **Émeraude** — vert profond, brume turquoise et pétales.
- **Cuivre** — ambre chaud, reflets de fin de journée.
- **Graphite** — neutre et mat, angles nets, trame technique.
- **Sakura** — prune rosé, halos fleur de cerisier.

**Les matières**

- **Papier calque** — papier crème opaque, encre brune, titres en serif, grain de fibres. Les accès deviennent des fiches avec leur catégorie.
- **Encre** — noir de terminal, phosphore vert, tout en chasse fixe (Consolas), bords francs, lignes de balayage. Chaque accès est une ligne de liste.
- **Affiche** — bleu nuit, corail et moutarde, titres en Arial Black, règles épaisses, motif imprimé et trame de points. Grandes fiches.
- **Brume** — pierre claire, ombres douces, courbes généreuses, matière mate et lumineuse.

Le thème est enregistré dans les préférences et retrouvé au démarrage. Il ne touche ni les raccourcis, ni les accès : uniquement l’apparence. La disposition des quatre panneaux et toutes les actions restent celles que tu as réglées.

## Personnalisation

Le bouton de réglages ouvre une fenêtre en verre avec enregistrement automatique : thème global, teinte du verre, intensité des reflets, taille des icônes, mode clair, noms et couleurs des panneaux, ordre des quatre panneaux, proportions de la grille, espacement, marges, largeur et hauteur de la capsule droite, durée des animations, repli et démarrage automatique.

Clic droit sur une icône : favoris, nom, catégorie, panneau de destination, icône personnalisée, emplacement, chemin à copier et masquage. Les accès masqués peuvent être réaffichés dans les réglages.

Glisser-déposer un accès réordonne les icônes ou change leur panneau. Glisser un fichier ou un dossier depuis l’Explorateur crée un accès dans Panneaux. **Le fichier d’origine ne se déplace pas.** Les boutons Ajouter permettent aussi de choisir des fichiers et dossiers.

La personnalisation s’exporte et s’importe en JSON. Elle est enregistrée dans `preferences-glass.json`, avec une copie précédente `.bak`. Le catalogue local est immédiatement disponible. Un inventaire de plus de 24 heures se rafraîchit en arrière-plan ; le menu de notification permet aussi de l’actualiser.

## Sites web

Dans la vue complète, les sites sont de petits boutons indépendants à gauche du bandeau. Il n’y a plus de grande capsule ni de zone réservée à droite. Le **+** ajoute une URL ; les sites s’ouvrent dans le navigateur par défaut de Windows.

Clic droit sur un site : modifier, choisir une image, déplacer ou retirer de la barre. Une image personnelle peut être recadrée et zoomée ; l’export PNG conserve sa transparence. L’option de verre peut être désactivée pour ne garder que le logo. Le nom apparaît au survol. Les mêmes sites sont accessibles depuis le bas de l’accueil.

Les catégories et menus contextuels utilisent des fenêtres de verre avec icônes et navigation au clavier.

## Secours indépendant

Lancer **Documents\SECOURS_BUREAU.exe**, ou rechercher **Secours - Bureau organisé** dans le menu Démarrer.

Le secours réaffiche les icônes Windows, remet les logiciels à gauche, les dossiers au centre et les jeux à droite, arrête Panneaux et désactive son démarrage automatique. Il est compilé en code natif avec son runtime intégré : il ne dépend ni de l’interface Glass ni de .NET ni de PowerShell.

Pour retrouver les positions exactes sauvegardées avant Glass, utiliser **Secours - Positions d’origine** dans le menu Démarrer. Le fichier `bureau-avant-glass.tsv` conserve ces positions. La restauration organisée a été vérifiée sur les **80 icônes** du Bureau, ainsi que la restauration exacte.

Depuis Glass, le bouton de retour au Bureau ou **Ctrl + Alt + Retour arrière** lancent aussi ce secours. En cas d’arrêt complet de l’application, utiliser son exécutable dans Documents ou son raccourci du menu Démarrer.

## Sources et vérification

Les sources C#, C++ et scripts de compilation restent dans ce dossier. `verification.json` contient les contrôles d’intégration ; `VALIDATION_6.1.md` décrit les essais de cette version (onglets rapides, palette globale, minuteur réglable), `VALIDATION_6.0.md` les précédents, `VALIDATION_5.5.md` et `VALIDATION_5.2.md` les précédentes. Les rapports `QUALITE*.md` sont les rapports historiques des versions précédentes. `glass.log` contient les incidents et démarrages. `preview-layout.png` est un export de la disposition WPF sur fond noir ; il ne restitue pas le flou réel de Windows.

L’application utilise WPF/.NET 8 installé sur ce PC. Le secours reste un exécutable Windows autonome.
