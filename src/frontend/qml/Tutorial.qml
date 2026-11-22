// Visite guidée du premier lancement : voile sombre avec projecteur arrondi sur la vraie cible, carte d'explication à côté.
// Chargée par un Loader (Main.qml) seulement pendant la visite ; aucune animation ni minuteur au repos, seulement au changement d'étape.
import QtQuick
import QtQuick.Window
import QtQuick.Shapes
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "settings"

Window {
    id: root

    objectName: "tutorial"
    title: "Panneaux · Visite guidée"
    transientParent: null
    color: "transparent"
    flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint
    x: Bridge.screenX
    y: Bridge.screenY
    width: Bridge.screenWidth
    height: Bridge.screenHeight
    visible: true

    // Surfaces réelles à montrer (fournies par Main.qml).
    property var panel: null
    property var dock: null
    property var social: null
    property var bookmarks: null

    signal closeRequested

    property int index: 0
    property bool hasTarget: false
    property bool openDocksAtEnd: false
    // Ce que la visite a ouvert : relâché quand l'étape suivante n'en a plus besoin, et à la fin.
    property var held: ({
            "panel": false,
            "dock": false,
            "bookmarks": false,
            "social": false
        })
    // Projecteur (coordonnées de la fenêtre), animé seulement au changement d'étape.
    property real hx: width / 2
    property real hy: height / 2
    property real hw: 0
    property real hh: 0
    readonly property real spotPad: Appearance.padding.normal
    readonly property bool locked: !!(Bridge.lock && Bridge.lock.locked)

    function combo(id: string, fallback: string): string {
        const list = Bridge.settings.shortcuts ?? [];
        for (let i = 0; i < list.length; i++)
            if (list[i].id === id && list[i].combo)
                return list[i].combo;
        return fallback;
    }

    readonly property string dictationKeyName: {
        const vk = Bridge.settings.dictationKey ?? 0xA5;
        return vk === 0xA5 ? "AltGr" : qsTr(Bridge.vkName(vk));
    }
    readonly property bool pillEnabled: Bridge.settings.peripheralBattery ?? Bridge.peripherals?.enabled ?? false
    readonly property string dockSettings: qsTr("Paramètres › Docks et panneaux")

    // id, icône, titre, texte, touches, cible (tabs | dock | pill | bookmarks | social), onglet, besoin (panel | dock | bookmarks | social),
    // placement de la carte, off/offBody (fonction désactivée), fallback (cible absente).
    readonly property var steps: [
        {
            id: "welcome",
            icon: "waving_hand",
            title: qsTr("Bienvenue dans Winlestia"),
            body: qsTr("Winlestia remplace la barre des tâches et le menu Démarrer de Windows par une interface plus simple, dont les couleurs suivent votre fond d’écran. Cette visite dure environ une minute ; vous pouvez la passer à tout moment.")
        },
        {
            id: "palette",
            icon: "search",
            title: qsTr("Commande rapide"),
            body: qsTr("Un seul champ pour lancer une application, retrouver un fichier ou une fenêtre, ouvrir un réglage, calculer ou chercher sur le web."),
            combos: [combo("palette", "Ctrl+F2")]
        },
        {
            id: "dashboard",
            icon: "dashboard",
            title: qsTr("Le tableau de bord"),
            body: qsTr("Il affiche l’heure, la météo et votre profil. Ouvrez-le avec ce raccourci, ou amenez le pointeur tout en haut de l’écran."),
            combos: [combo("dashboard", "Ctrl+F1")],
            target: "tabs",
            tab: -1,
            needs: "panel",
            place: "below",
            openTab: 0
        },
        {
            id: "media",
            icon: "music_note",
            title: qsTr("Médias"),
            body: qsTr("Le lecteur de ce qui joue en ce moment, avec les paroles des chansons."),
            target: "tabs",
            tab: 1,
            needs: "panel",
            place: "below",
            openTab: 1
        },
        {
            id: "performance",
            icon: "speed",
            title: qsTr("Performance"),
            body: qsTr("L’activité du processeur, de la carte graphique et de la mémoire, en direct."),
            target: "tabs",
            tab: 2,
            needs: "panel",
            place: "below",
            openTab: 2
        },
        {
            id: "workspaces",
            icon: "workspaces",
            title: qsTr("Espaces de travail"),
            body: qsTr("Vos applications, jeux, projets et documents, chacun dans sa page. Quand le panneau est ouvert, ces touches ouvrent chaque page directement."),
            combos: ["Ctrl+1", "Ctrl+2", "Ctrl+3", "Ctrl+4"],
            target: "tabs",
            tab: 3,
            needs: "panel",
            place: "below",
            openTab: 3
        },
        {
            id: "dock",
            icon: "dock_to_bottom",
            title: qsTr("Le dock"),
            body: qsTr("La barre d’applications en bas de l’écran. Elle se cache toute seule : amenez le pointeur en bas, ou appuyez sur la touche Windows."),
            combos: ["Win"],
            target: "dock",
            needs: "dock",
            place: "above"
        },
        {
            id: "battery",
            icon: "battery_charging_full",
            title: qsTr("Batteries des appareils"),
            body: qsTr("À côté de l’horloge, la charge de votre casque, de vos manettes, souris et clavier sans fil, et de la batterie du portable."),
            target: "pill",
            needs: "dock",
            place: "above",
            off: !pillEnabled,
            offBody: qsTr("La pastille de batterie est désactivée. Activez-la dans %1.").arg(dockSettings),
            fallback: qsTr("Elle apparaît à côté de l’horloge du dock dès qu’un casque, une manette, une souris ou un clavier sans fil est connecté.")
        },
        {
            id: "bookmarks",
            icon: "bookmarks",
            title: qsTr("Favoris"),
            body: qsTr("Le bord gauche de l’écran garde vos favoris de navigateur à portée : amenez-y le pointeur pour les afficher."),
            target: "bookmarks",
            needs: "bookmarks",
            place: "right",
            off: !(Bridge.bookmarksRailVisible ?? true),
            offBody: qsTr("Le rail des favoris est désactivé. Activez-le dans %1.").arg(dockSettings)
        },
        {
            id: "notifications",
            icon: "notifications",
            title: qsTr("Notifications et messages"),
            body: qsTr("Le bord droit de l’écran réunit l’historique des notifications et vos messageries. Amenez-y le pointeur pour l’ouvrir."),
            target: "social",
            needs: "social",
            place: "left",
            off: !(Bridge.socialRailVisible ?? true),
            offBody: qsTr("Le rail des notifications est désactivé. Activez-le dans %1.").arg(dockSettings)
        },
        {
            id: "dictation",
            icon: "mic",
            title: qsTr("Dictée vocale"),
            body: qsTr("Maintenez la touche pour parler : ce que vous dites s’écrit là où se trouve votre curseur, dans l’application active. La voix est traitée sur votre ordinateur."),
            combos: [dictationKeyName],
            off: !(Bridge.settings.dictationEnabled ?? false),
            offBody: qsTr("La dictée est désactivée. Activez-la dans Paramètres › Médias et audio.")
        },
        {
            id: "lock",
            icon: "lock",
            title: qsTr("Verrouiller l’écran"),
            body: qsTr("Le premier raccourci verrouille votre session. En cas de problème d’affichage, le second rétablit le bureau Windows."),
            combos: [combo("lock", "Ctrl+Alt+L"), "Ctrl+Alt+Backspace"]
        },
        {
            id: "shortcuts",
            icon: "keyboard",
            title: qsTr("Les raccourcis à retenir"),
            body: "",
            wide: true
        },
        {
            id: "end",
            icon: "celebration",
            title: qsTr("C’est parti !"),
            body: qsTr("Une question ? Rejoignez la communauté sur Discord."),
            last: true
        }
    ]
    readonly property var shortcutRows: [
        {
            keys: combo("palette", "Ctrl+F2"),
            text: qsTr("Commande rapide")
        },
        {
            keys: combo("dashboard", "Ctrl+F1"),
            text: qsTr("Tableau de bord")
        },
        {
            keys: combo("settings", "Win+S"),
            text: qsTr("Ouvrir les paramètres")
        },
        {
            keys: combo("lock", "Ctrl+Alt+L"),
            text: qsTr("Verrouiller l’écran")
        },
        {
            keys: combo("wallpaper", "Win+W"),
            text: qsTr("Changer de fond d’écran")
        },
        {
            keys: combo("overview", "Win+Alt+Tab"),
            text: qsTr("Vue d’ensemble des fenêtres")
        },
        {
            keys: combo("capture", "Ctrl+F3"),
            text: qsTr("Capturer l’écran")
        },
        {
            keys: combo("pin", "Ctrl+F8"),
            text: qsTr("Épingler au premier plan")
        },
        {
            keys: "Win",
            text: qsTr("Afficher le dock")
        },
        {
            keys: "Esc",
            text: qsTr("Fermer le panneau")
        },
        {
            keys: "Ctrl+Alt+Space",
            text: qsTr("Tableau de bord (secours)")
        },
        {
            keys: "Ctrl+Alt+Backspace",
            text: qsTr("Restaurer le bureau")
        }
    ]
    readonly property var step: steps[index]
    readonly property string stepBody: step.off ? step.offBody : (!hasTarget && step.fallback ? step.fallback : step.body)

    // --- Cibles réelles -------------------------------------------------------------------------------------------------
    function findNamed(item, name: string): var {
        if (!item)
            return null;
        if (item.objectName === name && item.visible && item.width > 0)
            return item;
        const kids = item.children;
        for (let i = 0; i < kids.length; i++) {
            const found = findNamed(kids[i], name);
            if (found)
                return found;
        }
        return null;
    }
    function itemRect(item): var {
        const p = item.mapToGlobal(0, 0);
        return Qt.rect(p.x, p.y, item.width, item.height);
    }
    function railRect(rail): var {
        if (!rail || !rail.visible)
            return null;
        const bh = rail.bumpHeight, bw = rail.bumpWidth;
        return Qt.rect(rail.isLeft ? rail.x : rail.x + rail.width - bw, rail.y + (rail.height - bh) / 2, bw, bh);
    }
    function targetRect(st): var {
        if (st.off)
            return null;
        if (st.target === "tabs") {
            const tabs = findNamed(panel?.contentItem, "dashboardTabs");
            if (!tabs)
                return null;
            if (st.tab >= 0) {
                for (let i = 0; i < tabs.children.length; i++) {
                    const bar = tabs.children[i];
                    if (typeof bar.itemAt === "function" && bar.itemAt(st.tab))
                        return itemRect(bar.itemAt(st.tab));
                }
            }
            return itemRect(tabs);
        }
        if (st.target === "dock")
            return dock && dock.visible ? Qt.rect(dock.x, dock.y, dock.width, dock.height) : null;
        if (st.target === "pill") {
            const pill = findNamed(dock?.contentItem, "dockPeripherals");
            return pill ? itemRect(pill) : null;
        }
        if (st.target === "bookmarks")
            return railRect(bookmarks);
        if (st.target === "social")
            return railRect(social);
        return null;
    }
    function refresh(): void {
        const r = targetRect(step);
        hasTarget = r !== null;
        if (r) {
            hx = r.x - root.x - spotPad;
            hy = r.y - root.y - spotPad;
            hw = r.width + spotPad * 2;
            hh = r.height + spotPad * 2;
        } else {
            hx = width / 2;
            hy = height / 2;
            hw = 0;
            hh = 0;
        }
    }

    // --- Étapes : ouvre ce qu'il faut, relâche le reste ---------------------------------------------------------------------
    function release(name: string): void {
        if (!held[name])
            return;
        held[name] = false;
        if (name === "panel") {
            Bridge.holdEdge("panel", false);
            Bridge.closePanel();
        } else
            Bridge.holdEdge(name, false);
    }
    function go(i: int): void {
        index = Math.max(0, Math.min(steps.length - 1, i));
        const needs = step.needs ?? "";
        for (const name of ["panel", "dock", "bookmarks", "social"])
            if (name !== needs)
                release(name);
        if (needs === "panel") {
            Bridge.holdEdge("panel", true);
            Bridge.openTab(step.openTab);
            held.panel = true;
        } else if (needs !== "" && !held[needs]) {
            Bridge.holdEdge(needs, true);
            held[needs] = true;
        }
        refresh();
        fade.restart();
        settle.restart();
    }
    // Garde : fin ou interruption déjà traitée (fermeture, masquage et destruction peuvent se suivre).
    property bool ended: false
    function finish(completed: bool): void {
        if (ended)
            return;
        ended = true;
        for (const name of ["panel", "dock", "bookmarks", "social"])
            release(name);
        Bridge.finishTutorial(completed);
        if (openDocksAtEnd)
            Bridge.action("settings", {
                section: "docks"
            });
        closeRequested();
    }
    // Interruption (verrou, masquage inattendu) : la visite n'est pas marquée comme vue et reprendra plus tard.
    function abort(): void {
        if (ended)
            return;
        ended = true;
        for (const name of ["panel", "dock", "bookmarks", "social"])
            release(name);
        Bridge.abortTutorial();
        closeRequested();
    }
    function openPersonalisation(): void {
        finish(true);
        const pages = SettingsIndex.pages;
        for (let i = 0; i < pages.length; i++)
            if (/personali/i.test(pages[i].id)) {
                Bridge.action("settings", {
                    section: pages[i].id
                });
                return;
            }
        Bridge.navigate("settings");
    }
    function next(): void {
        if (index >= steps.length - 1)
            finish(true);
        else
            go(index + 1);
    }

    onLockedChanged: if (locked)
        abort()
    // Alt+F4 ou fermeture par le système : équivaut à Passer.
    onClosing: close => {
        close.accepted = false;
        finish(false);
    }
    onVisibleChanged: if (!visible && !ended)
        abort()
    Component.onCompleted: {
        go(0);
        requestActivate();
        keys.forceActiveFocus();
    }
    Component.onDestruction: {
        for (const name of ["panel", "dock", "bookmarks", "social"])
            release(name);
        if (!ended)
            Bridge.abortTutorial();
    }

    // Le panneau (aussi toujours au premier plan) se montre ou prend le focus après nous : on repasse au-dessus, sans minuteur.
    Connections {
        target: root.panel
        enabled: !root.ended

        function onVisibleChanged() {
            root.raiseAbove();
        }
        function onActiveChanged() {
            if (root.panel && root.panel.active)
                root.raiseAbove();
        }
    }
    function raiseAbove(): void {
        root.raise();
        root.requestActivate();
        keys.forceActiveFocus();
    }

    // Une fois les animations d'ouverture finies : mesure définitive de la cible, au-dessus du panneau qui vient de s'ouvrir.
    Timer {
        id: settle

        interval: (Appearance.anim.durations.large + 200) * (Bridge.settings.motionScale ?? 1)
        repeat: false
        onTriggered: {
            root.refresh();
            root.raiseAbove();
        }
    }

    Item {
        id: keys

        anchors.fill: parent
        focus: true
        Keys.onPressed: event => {
            event.accepted = true;
            if (event.key === Qt.Key_Escape)
                root.finish(false);
            else if (event.key === Qt.Key_Left)
                root.go(root.index - 1);
            else if (event.key === Qt.Key_Right || event.key === Qt.Key_Return || event.key === Qt.Key_Enter)
                root.next();
            else
                event.accepted = false;
        }

        // Voile avec un trou arrondi autour de la cible (remplissage pair/impair).
        Shape {
            anchors.fill: parent

            ShapePath {
                fillRule: ShapePath.OddEvenFill
                fillColor: Qt.alpha(Colours.palette.m3scrim, 0.6)
                strokeColor: "transparent"

                PathSvg {
                    path: {
                        const r = Math.max(0, Math.min(Appearance.rounding.normal, root.hw / 2, root.hh / 2));
                        const x = root.hx, y = root.hy, w = root.hw, h = root.hh;
                        return "M0,0 H" + root.width + " V" + root.height + " H0 Z M" + (x + r) + "," + y + " H" + (x + w - r) + " A" + r + "," + r + " 0 0 1 " + (x + w) + "," + (y + r) + " V" + (y + h - r) + " A" + r + "," + r + " 0 0 1 " + (x + w - r) + "," + (y + h) + " H" + (x + r) + " A" + r + "," + r + " 0 0 1 " + x + "," + (y + h - r) + " V" + (y + r) + " A" + r + "," + r + " 0 0 1 " + (x + r) + "," + y + " Z";
                    }
                }
            }
        }

        Rectangle {
            x: root.hx
            y: root.hy
            width: root.hw
            height: root.hh
            radius: Math.max(0, Math.min(Appearance.rounding.normal, root.hw / 2, root.hh / 2))
            color: "transparent"
            border.width: 2
            border.color: Colours.palette.m3primary
            opacity: root.hasTarget ? 1 : 0

            Behavior on opacity {
                Anim {}
            }
        }

        // Le voile absorbe les clics : rien d'autre ne peut être touché pendant la visite.
        MouseArea {
            anchors.fill: parent
            hoverEnabled: true
            acceptedButtons: Qt.AllButtons
            preventStealing: true
            onWheel: wheel => wheel.accepted = true
        }

        TutorialCard {
            id: card

            readonly property real margin: Appearance.padding.large * 2
            readonly property real gap: Appearance.spacing.large
            // Cible dans le panneau : carte hors du contenu du panneau (dessous, sinon à côté, sinon bas-centre de l'écran).
            readonly property bool offPanel: root.hasTarget && root.step.target === "tabs" && !!root.panel
            readonly property real pw: offPanel ? Number(root.panel.panelWidth || root.panel.width) : 0
            readonly property real ph: offPanel ? Number(root.panel.panelHeight || root.panel.height) : 0
            readonly property real pl: offPanel ? root.panel.x - root.x + (root.panel.width - pw) / 2 : 0
            readonly property real pb: offPanel ? root.panel.y - root.y + ph : 0
            readonly property string panelPlace: !offPanel ? "" : pb + gap + height + margin <= root.height ? "below" : pl + pw + gap + width + margin <= root.width ? "right" : pl - gap - width - margin >= 0 ? "left" : "bottom"
            readonly property string place: offPanel ? panelPlace : root.hasTarget ? (root.step.place ?? "center") : "center"

            function clampX(v: real): real {
                return Math.max(margin, Math.min(root.width - width - margin, v));
            }
            function clampY(v: real): real {
                return Math.max(margin, Math.min(root.height - height - margin, v));
            }

            width: root.step.wide ? 620 : 480
            height: implicitHeight
            x: offPanel ? (panelPlace === "below" ? clampX(pl + pw / 2 - width / 2) : panelPlace === "right" ? clampX(pl + pw + gap) : panelPlace === "left" ? clampX(pl - width - gap) : (root.width - width) / 2) : place === "right" ? clampX(root.hx + root.hw + gap) : place === "left" ? clampX(root.hx - width - gap) : place === "center" ? (root.width - width) / 2 : clampX(root.hx + root.hw / 2 - width / 2)
            y: offPanel ? (panelPlace === "below" ? clampY(pb + gap) : panelPlace === "bottom" ? root.height - height - margin : clampY(root.hy)) : place === "below" ? clampY(root.hy + root.hh + gap) : place === "above" ? clampY(root.hy - height - gap) : place === "center" ? (root.height - height) / 2 : clampY(root.hy + root.hh / 2 - height / 2)
            icon: root.step.icon
            title: root.step.title
            body: root.stepBody
            combos: root.step.off ? [] : (root.step.combos ?? [])
            index: root.index
            count: root.steps.length
            nextText: root.step.last ? qsTr("Terminer") : qsTr("Suivant")
            onSkip: root.finish(false)
            onPrevious: root.go(root.index - 1)
            onNext: root.next()

            // Étape batterie désactivée : proposer d'ouvrir les réglages une fois la visite terminée.
            TextButton {
                visible: root.step.id === "battery" && root.step.off
                objectName: "tutorialOpenDocks"
                text: qsTr("Ouvrir ces réglages à la fin de la visite")
                toggle: true
                type: TextButton.Tonal
                onClicked: root.openDocksAtEnd = internalChecked
            }

            // Grille compacte des raccourcis.
            GridLayout {
                visible: root.step.id === "shortcuts"
                Layout.fillWidth: true
                columns: 2
                columnSpacing: Appearance.spacing.large
                rowSpacing: Appearance.spacing.smaller

                Repeater {
                    model: root.shortcutRows

                    ColumnLayout {
                        id: cell

                        required property var modelData

                        Layout.fillWidth: true
                        Layout.preferredWidth: 1
                        spacing: 3

                        KeyCap {
                            combo: cell.modelData.keys
                        }

                        StyledText {
                            text: cell.modelData.text
                            font.pointSize: Appearance.font.size.small
                            color: Colours.palette.m3onSurfaceVariant
                        }
                    }
                }
            }

            // Dernière étape : communauté et personnalisation.
            RowLayout {
                visible: root.step.id === "end"
                Layout.fillWidth: true
                spacing: Appearance.spacing.normal

                DiscordLogo {
                    size: 40
                    color: Colours.palette.m3primary
                }

                TextButton {
                    objectName: "tutorialDiscord"
                    text: qsTr("Rejoindre le Discord")
                    onClicked: Qt.openUrlExternally("https://discord.gg/5jXv7NRj3Q")
                }

                TextButton {
                    objectName: "tutorialCustomise"
                    text: qsTr("Personnaliser")
                    type: TextButton.Tonal
                    onClicked: root.openPersonalisation()
                }
            }

            StyledText {
                visible: root.step.id === "end"
                Layout.fillWidth: true
                Layout.maximumWidth: card.inner
                text: qsTr("Revoir la visite : Paramètres › Général")
                wrapMode: Text.WordWrap
                font.pointSize: Appearance.font.size.small
                color: Colours.palette.m3outline
            }

            // Léger rebond d'échelle au changement d'étape (la carte reste opaque).
            opacity: 1
            SequentialAnimation {
                id: fade

                NumberAnimation {
                    target: card
                    property: "scale"
                    to: 0.97
                    duration: 0
                }
                Anim {
                    target: card
                    property: "scale"
                    to: 1
                    duration: Appearance.anim.durations.small
                }
            }
        }
    }

    Behavior on hx {
        enabled: !Appearance.reducedEffects
        Anim {
            duration: Appearance.anim.durations.expressiveDefaultSpatial
            easing.bezierCurve: Appearance.anim.curves.emphasizedDecel
        }
    }
    Behavior on hy {
        enabled: !Appearance.reducedEffects
        Anim {
            duration: Appearance.anim.durations.expressiveDefaultSpatial
            easing.bezierCurve: Appearance.anim.curves.emphasizedDecel
        }
    }
    Behavior on hw {
        enabled: !Appearance.reducedEffects
        Anim {
            duration: Appearance.anim.durations.expressiveDefaultSpatial
            easing.bezierCurve: Appearance.anim.curves.emphasizedDecel
        }
    }
    Behavior on hh {
        enabled: !Appearance.reducedEffects
        Anim {
            duration: Appearance.anim.durations.expressiveDefaultSpatial
            easing.bezierCurve: Appearance.anim.curves.emphasizedDecel
        }
    }
}
