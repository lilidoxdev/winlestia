pragma Singleton
import QtQuick

// Description statique de l'interface des réglages : pages (navigation) et une entrée cherchable par ligne.
// Les composants de ligne lisent titre/description ici (byId), donc recherche et pages ne peuvent jamais diverger.
QtObject {
    id: root

    // group : les entrées d'un même groupe restent ensemble dans la navigation ; un espace/séparateur distingue les groupes.
    readonly property var pages: [
        {
            id: "personalisation",
            icon: "palette",
            group: 0,
            title: qsTr("Personnalisation"),
            subtitle: qsTr("Couleurs, mouvement, contours, bureau, panneau du haut")
        },
        {
            id: "docks",
            icon: "dock_to_bottom",
            group: 0,
            title: qsTr("Docks et panneaux"),
            subtitle: qsTr("Visibilité, barre des tâches, disposition")
        },
        {
            id: "shortcuts",
            icon: "keyboard",
            group: 1,
            title: qsTr("Raccourcis clavier"),
            subtitle: qsTr("Ouvrir, modifier, éviter les conflits")
        },
        {
            id: "media",
            icon: "volume_up",
            group: 1,
            title: qsTr("Médias et audio"),
            subtitle: qsTr("Indicateurs à l’écran, dictée vocale")
        },
        {
            id: "apps",
            icon: "apps",
            group: 2,
            title: qsTr("Applications, favoris et liens"),
            subtitle: qsTr("Favoris, réseaux sociaux, presse-papiers")
        },
        {
            id: "files",
            icon: "folder_open",
            group: 2,
            title: qsTr("Fichiers et Windows"),
            subtitle: qsTr("Explorateur, gestionnaire de fichiers")
        },
        {
            id: "general",
            icon: "tune",
            group: 3,
            title: qsTr("Général"),
            subtitle: qsTr("Langue, démarrage, restauration, à propos")
        },
        {
            id: "winjets",
            icon: "widgets",
            group: 3,
            title: qsTr("Winjets"),
            subtitle: qsTr("Cartes sur le bureau, galerie, import")
        },
        {
            id: "lock",
            icon: "lock",
            group: 3,
            title: qsTr("Verrouillage"),
            subtitle: qsTr("Mot de passe, verrouillage automatique, raccourci")
        }
    ]

    // Anciens identifiants de page, fusionnés dans « personnalisation ».
    readonly property var pageAliases: ({
            "appearance": "personnalisation",
            "interface": "personnalisation"
        })

    function pageIndex(id: string): int {
        id = pageAliases[id] ?? id;
        for (let i = 0; i < pages.length; i++)
            if (pages[i].id === id)
                return i;
        return 0;
    }

    // page = index dans `pages` ; section = libellé discret de la section contenant la ligne ; anchor = ligne à montrer quand celle-ci est
    // masquée.
    // kw : mots de recherche supplémentaires (français et anglais), jamais affichés.
    readonly property var entries: [
        {
            id: "theme",
            page: 0,
            section: qsTr("Couleurs"),
            label: qsTr("Thème"),
            desc: qsTr("Porcelaine est clair, Nuit est sombre, Automatique suit ton fond d’écran."),
            kw: "couleurs mode clair sombre nuit porcelaine automatique fond d’écran wallpaper dark light theme colors"
        },
        {
            id: "customPalette",
            page: 0,
            section: qsTr("Couleurs"),
            label: qsTr("Ma palette"),
            desc: qsTr("Crée tes propres couleurs avec le thème Personnalisé."),
            kw: "personnalise custom editeur couleurs verre",
            anchor: "theme"
        },
        {
            id: "transparency",
            page: 0,
            section: qsTr("Couleurs"),
            label: qsTr("Transparence du verre"),
            desc: qsTr("Plus la valeur est basse, plus on voit le fond d’écran à travers les panneaux."),
            kw: "opacite verre glass blur flou transparency opacity"
        },
        {
            id: "motion",
            page: 0,
            section: qsTr("Mouvement"),
            label: qsTr("Animations"),
            desc: qsTr("Choisis la vitesse des mouvements à l’écran."),
            kw: "vitesse transitions mouvement sans rapides motion speed"
        },
        {
            id: "reducedEffects",
            page: 0,
            section: qsTr("Mouvement"),
            label: qsTr("Effets allégés"),
            desc: qsTr("Retire le flou et les ombres pour un ordinateur moins puissant."),
            kw: "performance leger economie reduced effects light"
        },
        {
            id: "accentBorders",
            page: 0,
            section: qsTr("Contours et cadre"),
            label: qsTr("Liseré sur les fenêtres"),
            desc: qsTr("Ajoute un fin contour de couleur autour des fenêtres."),
            kw: "bordure contour fenetre accent border"
        },
        {
            id: "activeBorderOnly",
            page: 0,
            section: qsTr("Contours et cadre"),
            label: qsTr("Seulement la fenêtre active"),
            desc: qsTr("Le contour n’apparaît que sur la fenêtre au premier plan."),
            kw: "bordure active focus",
            anchor: "accentBorders"
        },
        {
            id: "screenFrame",
            page: 0,
            section: qsTr("Contours et cadre"),
            label: qsTr("Cadre de l’écran"),
            desc: qsTr("Un contour arrondi et coloré autour de ton écran."),
            kw: "bordure ecran frame arrondi"
        },
        {
            id: "frameThickness",
            page: 0,
            section: qsTr("Contours et cadre"),
            label: qsTr("Épaisseur du cadre"),
            desc: qsTr("Rend le cadre plus fin ou plus épais."),
            kw: "taille largeur frame thickness",
            anchor: "screenFrame"
        },
        {
            id: "desktopLogo",
            page: 0,
            section: qsTr("Bureau"),
            label: qsTr("Logo Winlestia"),
            desc: qsTr("Affiche un petit logo discret sur le bureau."),
            kw: "logo marque watermark"
        },
        {
            id: "desktopIndicator",
            page: 0,
            section: qsTr("Bureau"),
            label: qsTr("Indicateur de bureaux virtuels"),
            desc: qsTr("Montre des points pour savoir sur quel bureau tu es."),
            kw: "bureaux virtuels desktops pastille indicator"
        },
        {
            id: "quietFullscreen",
            page: 0,
            section: qsTr("Bureau"),
            label: qsTr("Mode discret en plein écran"),
            desc: qsTr("Cache les indicateurs du shell pendant un film ou un jeu."),
            kw: "plein ecran jeu video quiet fullscreen osd"
        },
        {
            id: "topActivation",
            page: 0,
            section: qsTr("Panneau du haut"),
            label: qsTr("Zone d’ouverture"),
            desc: qsTr("Bande en haut de l’écran qui ouvre le panneau quand la souris y passe."),
            kw: "calibrer haut ecran souris survol panneau calibrate hover"
        },
        {
            id: "dockVisible",
            page: 1,
            section: qsTr("Visibilité"),
            label: qsTr("Dock Winlestia"),
            desc: qsTr("Le dock avec les applications ouvertes, au bord bas de l’écran"),
            kw: "dock barre taskbar applications epingle"
        },
        {
            id: "dockLayout",
            page: 1,
            section: qsTr("Barre d’icônes"),
            label: qsTr("Boutons du dock"),
            desc: qsTr("Choisis les boutons de la barre, ceux du tiroir (la flèche de la barre l’ouvre) et leur ordre."),
            kw: "dock barre tiroir icones boutons wifi bluetooth micro avion reveil ordre masquer drawer"
        },
        {
            id: "dockLayoutReset",
            page: 1,
            section: qsTr("Barre d’icônes"),
            label: qsTr("Rétablir les boutons par défaut"),
            desc: "",
            kw: "reinitialiser reset defaut dock tiroir barre"
        },
        {
            id: "peripheralBattery",
            page: 1,
            section: qsTr("Barre d’icônes"),
            label: qsTr("Batterie des périphériques"),
            desc: qsTr("Affiche dans le dock le niveau de batterie des appareils connectés"),
            kw: "batterie casque manette souris clavier bluetooth peripheriques niveau pile"
        },
        {
            id: "bookmarksRail",
            page: 1,
            section: qsTr("Visibilité"),
            label: qsTr("Panneau des favoris (bord gauche)"),
            desc: qsTr("Se déploie quand la souris touche le bord gauche de l’écran"),
            kw: "favoris signets bookmarks rail gauche"
        },
        {
            id: "socialRail",
            page: 1,
            section: qsTr("Visibilité"),
            label: qsTr("Panneau des réseaux sociaux (bord droit)"),
            desc: qsTr("Se déploie quand la souris touche le bord droit de l’écran"),
            kw: "reseaux sociaux messagerie social rail droite"
        },
        {
            id: "replaceTaskbar",
            page: 1,
            section: qsTr("Barre des tâches"),
            label: qsTr("Masquer la barre des tâches de Windows"),
            desc: qsTr("Elle se rétablit à tout moment depuis Général › Restauration."),
            kw: "taskbar barre windows cacher remplacer hide"
        },
        {
            id: "iconSize",
            page: 1,
            section: qsTr("Barre des tâches"),
            label: qsTr("Taille des aperçus de bureaux"),
            desc: qsTr("Taille des icônes dans les vues de bureaux et d’applications"),
            kw: "icones vignettes workspace taille size"
        },
        {
            id: "autoTile",
            page: 1,
            section: qsTr("Disposition automatique"),
            label: qsTr("Organisation automatique"),
            desc: qsTr("Les fenêtres se partagent chaque écran en tuiles, dans un ordre stable, avec des raccourcis clavier"),
            kw: "tuiles tiling fenetres disposition"
        },
        {
            id: "tileShortcuts",
            page: 1,
            section: qsTr("Disposition automatique"),
            label: qsTr("Raccourcis du tuilage"),
            desc: qsTr("Actifs seulement quand l’organisation automatique est activée"),
            kw: "tuilage clavier raccourcis tiling",
            anchor: "autoTile"
        },
        {
            id: "tileExclusions",
            page: 1,
            section: qsTr("Disposition automatique"),
            label: qsTr("Applications exclues"),
            desc: qsTr("Aucune application exclue"),
            kw: "exclure exception tiling exclusions"
        },
        {
            id: "osd",
            page: 3,
            section: qsTr("Affichage à l’écran"),
            label: qsTr("Afficher l’OSD"),
            desc: qsTr("Indicateur de volume, luminosité et micro à l’écran"),
            kw: "volume luminosite micro osd on screen"
        },
        {
            id: "pill",
            page: 3,
            section: qsTr("Affichage à l’écran"),
            label: qsTr("Pilule"),
            desc: qsTr("Musique, volume et luminosité dans une capsule en haut de l’écran"),
            kw: "pilule capsule musique volume luminosite ile dynamique pill bulle son popup notification desactiver"
        },
        {
            id: "lyrics",
            page: 3,
            section: qsTr("Affichage à l’écran"),
            label: qsTr("Paroles"),
            desc: qsTr("Affiche les paroles synchronisées du morceau en cours. Le titre et l’artiste sont envoyés à lrclib.net."),
            kw: "paroles lyrics chanson musique karaoke lrclib synchronisees"
        },
        {
            id: "dictationEnabled",
            page: 3,
            section: qsTr("Dictée vocale"),
            label: qsTr("Activer la dictée"),
            desc: qsTr("Transcription locale, sans envoyer ta voix sur Internet"),
            kw: "voix micro dictee dictation speech"
        },
        {
            id: "dictationKey",
            page: 3,
            section: qsTr("Dictée vocale"),
            label: qsTr("Touche de dictée"),
            desc: qsTr("Maintenez la touche pour dicter (français, anglais, espagnol, détection auto)"),
            kw: "touche maintenue push to talk altgr",
            anchor: "dictationEnabled"
        },
        {
            id: "bookmarksBrowser",
            page: 4,
            section: qsTr("Favoris"),
            label: qsTr("Navigateur des favoris"),
            desc: qsTr("Le navigateur dont Winlestia lit les favoris"),
            kw: "chrome edge firefox brave signets bookmarks browser"
        },
        {
            id: "bookmarksUnhide",
            page: 4,
            section: qsTr("Favoris"),
            label: qsTr("Réafficher les favoris retirés"),
            desc: qsTr("Ceux retirés de Winlestia uniquement, pas du navigateur"),
            kw: "cache masques retires hidden bookmarks"
        },
        {
            id: "socialBackground",
            page: 4,
            section: qsTr("Réseaux sociaux"),
            label: qsTr("Garder les messageries actives en arrière-plan"),
            desc: qsTr("Compteurs de messages à jour, mais plus de mémoire utilisée."),
            kw: "arriere plan memoire compteurs background"
        },
        {
            id: "socialNetworks",
            page: 4,
            section: qsTr("Réseaux sociaux"),
            label: qsTr("Comptes des réseaux sociaux"),
            desc: qsTr("La connexion se fait toi-même dans la fenêtre du service. Snapchat Web peut être limité."),
            kw: "connexion compte login whatsapp discord instagram messenger telegram snapchat afficher muet deconnecter"
        },
        {
            id: "socialGlobalMute",
            page: 4,
            section: qsTr("Notifications"),
            label: qsTr("Couper toutes les notifications"),
            desc: qsTr("Plus de son ; les compteurs continuent"),
            kw: "sourdine muet silence mute"
        },
        {
            id: "socialSound",
            page: 4,
            section: qsTr("Notifications"),
            label: qsTr("Son de notification"),
            desc: qsTr("Joue un son à chaque nouveau message"),
            kw: "son sound alerte"
        },
        {
            id: "socialVolume",
            page: 4,
            section: qsTr("Notifications"),
            label: qsTr("Volume"),
            desc: qsTr("Volume du son de notification"),
            kw: "son niveau sound volume tester"
        },
        {
            id: "clipboardHistory",
            page: 4,
            section: qsTr("Presse-papiers"),
            label: qsTr("Historique du presse-papiers"),
            desc: qsTr("Garde les éléments copiés pour les recoller"),
            kw: "copier coller clipboard history"
        },
        {
            id: "clipboardImages",
            page: 4,
            section: qsTr("Presse-papiers"),
            label: qsTr("Garder les images copiées"),
            desc: qsTr("Ajoute aussi les captures et images à l’historique"),
            kw: "images captures clipboard",
            anchor: "clipboardHistory"
        },
        {
            id: "clipboardLimit",
            page: 4,
            section: qsTr("Presse-papiers"),
            label: qsTr("Taille de l’historique"),
            desc: qsTr("Nombre maximal d’éléments conservés"),
            kw: "limite nombre elements max limit",
            anchor: "clipboardHistory"
        },
        {
            id: "explorerTheme",
            page: 5,
            section: qsTr("Explorateur Windows"),
            label: qsTr("Icônes et contours assortis"),
            desc: qsTr("Dossiers personnels et sous-dossiers à tes couleurs (aperçu ci-dessus)"),
            kw: "explorer dossiers icones couleurs folders"
        },
        {
            id: "explorerFolders",
            page: 5,
            section: qsTr("Explorateur Windows"),
            label: qsTr("Dossiers personnalisés"),
            desc: qsTr("Ajouter un dossier, ouvrir l’Explorateur ou restaurer les icônes d’origine"),
            kw: "ajouter restaurer ouvrir explorer"
        },
        {
            id: "filesDefaultHandler",
            page: 5,
            section: qsTr("Winlestia Fichiers"),
            label: qsTr("Ouvrir les dossiers avec Winlestia Fichiers"),
            desc: qsTr("Explorer reste disponible ; désactivable à tout moment"),
            kw: "gestionnaire fichiers defaut handler files"
        },
        {
            id: "filesPreload",
            page: 5,
            section: qsTr("Winlestia Fichiers"),
            label: qsTr("Précharger Winlestia Fichiers"),
            desc: qsTr("Garde une instance cachée (~70 Mo de mémoire) pour ouvrir les dossiers plus vite ; " +
                       "l’arrêt prend effet au prochain démarrage"),
            kw: "rapide memoire preload"
        },
        {
            id: "language",
            page: 6,
            section: qsTr("Langue"),
            label: qsTr("Langue"),
            desc: qsTr("Langue de l’interface de Winlestia"),
            kw: "francais english espanol traduction language idioma"
        },
        {
            id: "autostart",
            page: 6,
            section: qsTr("Démarrage"),
            label: qsTr("Démarrer avec Windows"),
            desc: qsTr("Winlestia s’ouvre à ta connexion"),
            kw: "demarrage session connexion autostart startup"
        },
        {
            id: "weatherCity",
            page: 6,
            section: qsTr("Services"),
            label: qsTr("Ville de la météo"),
            desc: qsTr("Pour la météo du tableau de bord"),
            kw: "meteo weather ville city localisation"
        },
        {
            id: "sensorsStatus",
            page: 6,
            section: qsTr("Capteur de température"),
            label: qsTr("Température du processeur"),
            desc: qsTr("Service optionnel pour lire la température du processeur"),
            kw: "temperature cpu capteur pawnio service administrateur thermique"
        },
        {
            id: "refreshCatalogue",
            page: 6,
            section: qsTr("Services"),
            label: qsTr("Liste des applications"),
            desc: qsTr("Relit les applications installées"),
            kw: "actualiser catalogue refresh installees"
        },
        {
            id: "restore",
            page: 6,
            section: qsTr("Restauration"),
            label: qsTr("Rétablir la barre Windows"),
            desc: qsTr("Réaffiche la barre des tâches de Windows"),
            kw: "secours rescue taskbar restaurer urgence ctrl alt retour arriere"
        },
        {
            id: "about",
            page: 6,
            section: qsTr("À propos"),
            label: qsTr("À propos de Winlestia"),
            desc: qsTr("Version et dossier des données"),
            kw: "version dossier donnees data folder about"
        },
        {
            id: "tutorial",
            page: 6,
            section: qsTr("À propos"),
            label: qsTr("Revoir la visite guidée"),
            desc: qsTr("Une courte présentation de Winlestia, pas à pas"),
            kw: "tutoriel visite guidee aide decouverte premier lancement tour tutorial help onboarding"
        },
        {
            id: "discord",
            page: 6,
            section: qsTr("À propos"),
            label: qsTr("Communauté Discord"),
            desc: qsTr("Posez vos questions et échangez avec les autres utilisateurs"),
            kw: "discord communaute aide support serveur community help chat"
        },
        {
            id: "winjetsEdit",
            page: 7,
            section: qsTr("Bureau"),
            label: qsTr("Modifier les Winjets"),
            desc: qsTr("Déplacez, redimensionnez ou retirez les cartes directement sur le bureau."),
            kw: "widgets cartes deplacer edition desktop"
        },
        {
            id: "winjetsRecover",
            page: 7,
            section: qsTr("Bureau"),
            label: qsTr("Ramener tous les Winjets sur l'écran principal"),
            desc: qsTr("Utile après le débranchement d'un écran."),
            kw: "widgets ecran principal recuperer hors ecran"
        },
        {
            id: "winjetsGallery",
            page: 7,
            section: qsTr("Galerie"),
            label: qsTr("Galerie de Winjets"),
            desc: qsTr("Ajouter une carte au bureau"),
            kw: "widgets ajouter importer zip galerie"
        },
        {
            id: "lockPassword",
            page: 8,
            section: qsTr("Mot de passe"),
            label: qsTr("Mot de passe de verrouillage"),
            desc: qsTr("Le mot de passe demandé pour déverrouiller le shell"),
            kw: "verrouillage ecran lock password securite"
        },
        {
            id: "lockEnabled",
            page: 8,
            section: qsTr("Verrouillage"),
            label: qsTr("Activer l'écran de verrouillage"),
            desc: qsTr("Autorise le raccourci et le verrouillage automatique"),
            kw: "verrouiller ecran lock enable"
        },
        {
            id: "lockIdle",
            page: 8,
            section: qsTr("Verrouillage"),
            label: qsTr("Verrouiller après inactivité"),
            desc: qsTr("Verrouille le shell après un temps sans activité"),
            kw: "inactivite minuterie auto idle timeout"
        },
        {
            id: "lockShortcut",
            page: 8,
            section: qsTr("Verrouillage"),
            label: qsTr("Raccourci de verrouillage"),
            desc: qsTr("Combinaison qui verrouille le shell"),
            kw: "raccourci clavier lock shortcut"
        }
    ]

    readonly property var byId: {
        const map = {};
        for (const e of entries)
            map[e.id] = e;
        return map;
    }

    function label(id: string): string {
        return byId[id]?.label ?? "";
    }
    function desc(id: string): string {
        return byId[id]?.desc ?? "";
    }
}
