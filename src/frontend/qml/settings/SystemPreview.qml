import QtQuick
import qs.components
import qs.services
import qs.config
import "."

// Petit aperçu décoratif (≈ 120×72) des pages Dock, Raccourcis et Médias : montre en direct où à l'écran agit un réglage.
// kind : dock | iconSize | peripheral | dockLayout | rail | autoTile | tiles | shortcut | osd | pill | lyrics | dictation
// value : booléen (interrupteurs), taille en px (iconSize), nombre d'éléments de la barre (dockLayout), variante (tiles :
//         focus | swap | float | grow | shrink | exclude), identifiant du raccourci (shortcut).
// value2 : côté (rail : left | right), nombre d'appareils (peripheral), éléments du tiroir (dockLayout),
//          combinaison de touches (shortcut), nom de la touche (dictation).
// Tout est dessiné avec la palette ; rien ne tourne au repos, seules les transitions de valeur s'animent
// (et pas du tout avec les effets allégés).
Item {
    id: root

    property string kind
    property var value
    property var value2
    property string label: qsTr("Aperçu")

    readonly property var pal: Colours.palette
    readonly property bool flag: value === true
    readonly property bool animated: !Appearance.reducedEffects
    readonly property string sid: String(value ?? "")
    readonly property string tileVariant: kind === "tiles" ? sid : (kind === "shortcut" && sid.startsWith("tile")) ? sid.substring(4).toLowerCase() : ""
    readonly property bool tilesOn: tileVariant !== "" || kind === "autoTile"
    readonly property bool tiled: kind !== "autoTile" || flag
    readonly property real ratio: tileVariant === "grow" ? 0.62 : tileVariant === "shrink" ? 0.38 : 0.5
    readonly property bool plainShortcut: kind === "shortcut" && tileVariant === ""
    readonly property string capText: kind === "shortcut" ? (String(value2 ?? "") !== "" ? String(value2) : qsTr("Aucun")) : kind === "dictation" ? String(value2 ?? "") : ""

    implicitWidth: 120
    implicitHeight: 72
    Accessible.role: Accessible.Graphic
    Accessible.name: label

    // Teinte opaque : la palette du verre est presque transparente, donc on teinte la base opaque du thème.
    function tone(c: color, a: real): color {
        return Qt.tint(Colours.surfaceBase, Qt.alpha(c, a));
    }
    function clamp(v: real, lo: real, hi: real): real {
        return Math.max(lo, Math.min(hi, v));
    }

    component Lines: Column {
        id: lines

        property color tint

        spacing: 3

        Repeater {
            model: [0.9, 0.6, 0.78]

            Rectangle {
                required property real modelData

                width: lines.width * modelData
                height: 3
                radius: 1.5
                color: lines.tint
            }
        }
    }

    component Stripes: Row {
        Repeater {
            model: 7

            Rectangle {
                required property int index

                width: parent.width / 7
                height: parent.height
                color: index % 3 === 0 ? root.tone(root.pal.m3primary, 0.55) : index % 3 === 1 ? root.tone(root.pal.m3tertiary, 0.5) : root.tone(root.pal.m3secondary, 0.45)
            }
        }
    }

    // Rectangle dont l'opacité se fond d'une valeur à l'autre.
    component Fade: Rectangle {
        Behavior on opacity {
            enabled: root.animated

            Anim {
                duration: Appearance.anim.durations.small
            }
        }
    }

    component Win: Rectangle {
        id: win

        property color edge: root.pal.m3primary
        property bool outlined: false
        property real linesOpacity: 1

        radius: Appearance.rounding.tiny * 1.5
        color: root.tone(root.pal.m3onSurface, 0.16)
        border.width: outlined ? 2 : 0
        border.color: edge
        clip: true

        Behavior on x {
            enabled: root.animated

            Anim {
                duration: Appearance.anim.durations.small
            }
        }
        Behavior on y {
            enabled: root.animated

            Anim {
                duration: Appearance.anim.durations.small
            }
        }
        Behavior on width {
            enabled: root.animated

            Anim {
                duration: Appearance.anim.durations.small
            }
        }
        Behavior on height {
            enabled: root.animated

            Anim {
                duration: Appearance.anim.durations.small
            }
        }
        Behavior on linesOpacity {
            enabled: root.animated

            Anim {
                duration: Appearance.anim.durations.small
            }
        }

        Rectangle {
            x: 4
            y: 4
            width: parent.width - 8
            height: 5
            radius: 2.5
            color: root.tone(root.pal.m3onSurface, 0.3)
        }
        Lines {
            x: 5
            y: 14
            width: parent.width - 10
            opacity: win.linesOpacity
            tint: root.tone(root.pal.m3onSurface, 0.28)
        }
    }

    // Pastille ronde portant une icône : l'action du réglage.
    component Badge: Rectangle {
        id: badge

        property string icon
        property color fg: root.pal.m3onPrimaryContainer

        width: 18
        height: 18
        radius: 9
        color: root.pal.m3primaryContainer

        MaterialIcon {
            anchors.centerIn: parent
            text: badge.icon
            color: badge.fg
            font.pointSize: Appearance.font.size.small
        }
    }

    component Dock: Rectangle {
        id: dk

        property int count: 5
        property real iw: 9

        width: count * (iw + 3) + 4
        height: iw + 7
        radius: Math.min(height / 2, Appearance.rounding.small)
        color: root.tone(root.pal.m3onSurface, 0.18)

        Behavior on iw {
            enabled: root.animated

            Anim {
                duration: Appearance.anim.durations.small
            }
        }

        Row {
            anchors.centerIn: parent
            spacing: 3

            Repeater {
                model: dk.count

                Rectangle {
                    required property int index

                    width: dk.iw
                    height: dk.iw
                    radius: 2.5
                    color: root.tone(root.pal.m3primary, index === 0 ? 0.9 : 0.5)
                }
            }
        }
    }

    Rectangle {
        id: desk

        anchors.fill: parent
        radius: Appearance.rounding.small
        color: root.tone(root.pal.m3onSurface, 0.06)
        border.width: 1
        border.color: Qt.alpha(root.pal.m3outlineVariant, 0.8)

        Rectangle {
            id: stage

            anchors.fill: parent
            anchors.margins: 3
            radius: Math.max(0, Appearance.rounding.small - 3)
            color: root.tone(root.pal.m3primary, 0.08)
            clip: true

            // Zone de dessin ; les raccourcis gardent une bande en bas pour la touche.
            Item {
                id: surf

                anchors.fill: parent
                anchors.bottomMargin: root.capText !== "" ? 18 : 0

                // ---- dock / iconSize / peripheral / dockLayout : le dock en bas de l'écran ----
                Item {
                    id: dockView

                    visible: root.kind === "dock" || root.kind === "iconSize" || root.kind === "peripheral" || root.kind === "dockLayout"
                    anchors.fill: parent

                    Win {
                        x: parent.width * 0.15
                        y: parent.height * 0.08
                        width: parent.width * 0.7
                        height: parent.height * 0.36
                    }
                    // « dock » éteint : la barre des tâches de Windows reprend la place.
                    Fade {
                        visible: root.kind === "dock"
                        opacity: root.flag ? 0 : 1
                        anchors.left: parent.left
                        anchors.right: parent.right
                        anchors.bottom: parent.bottom
                        height: 8
                        color: root.tone(root.pal.m3onSurface, 0.24)

                        Rectangle {
                            x: 3
                            anchors.verticalCenter: parent.verticalCenter
                            width: 20
                            height: 4
                            radius: 2
                            color: root.tone(root.pal.m3onSurface, 0.45)
                        }
                    }
                    Dock {
                        id: mainDock

                        readonly property bool isLayout: root.kind === "dockLayout"

                        visible: root.kind !== "peripheral"
                        opacity: root.kind === "dock" && !root.flag ? 0 : 1
                        count: isLayout ? root.clamp(Number(root.value), 1, 8) : 5
                        iw: root.kind === "iconSize" ? 6 + (root.clamp(Number(root.value), 40, 80) - 40) / 40 * 7 : isLayout ? 8 : 9
                        x: (parent.width - width) / 2
                        y: parent.height - height - 3

                        Behavior on opacity {
                            enabled: root.animated

                            Anim {
                                duration: Appearance.anim.durations.small
                            }
                        }
                    }
                    // Tiroir : la rangée qui se déploie au-dessus de la barre.
                    Dock {
                        visible: root.kind === "dockLayout" && Number(root.value2) > 0
                        count: root.clamp(Number(root.value2), 1, 6)
                        iw: 8
                        x: mainDock.x + mainDock.width - width
                        y: mainDock.y - height - 3
                        opacity: 0.85
                    }
                    // Pastilles de batterie des appareils, à droite du dock.
                    Row {
                        visible: root.kind === "peripheral"
                        x: (parent.width - width) / 2
                        y: parent.height - height - 3
                        spacing: 3

                        Dock {
                            count: 4
                            iw: 9
                        }
                        Repeater {
                            model: root.clamp(Number(root.value2 ?? 1), 1, 3)

                            Fade {
                                required property int index

                                width: 22
                                height: 16
                                radius: 8
                                color: root.pal.m3secondaryContainer
                                opacity: root.flag && Number(root.value2) > 0 ? 1 : 0.18

                                MaterialIcon {
                                    anchors.centerIn: parent
                                    text: index === 0 ? "battery_full" : index === 1 ? "headphones" : "mouse"
                                    color: root.pal.m3onSecondaryContainer
                                    font.pointSize: Appearance.font.size.small
                                }
                            }
                        }
                    }
                }

                // ---- rail : bandeau qui se déploie sur un bord (value = visible, value2 = left | right) ----
                Item {
                    visible: root.kind === "rail"
                    anchors.fill: parent

                    Win {
                        x: parent.width * 0.22
                        y: parent.height * 0.14
                        width: parent.width * 0.56
                        height: parent.height * 0.6
                    }
                    Fade {
                        x: root.value2 === "right" ? parent.width - width : 0
                        y: parent.height * 0.1
                        width: 12
                        height: parent.height * 0.8
                        radius: 6
                        color: root.tone(root.pal.m3primary, 0.55)
                        opacity: root.flag ? 1 : 0.2

                        Column {
                            anchors.centerIn: parent
                            spacing: 4

                            Repeater {
                                model: 4

                                Rectangle {
                                    width: 6
                                    height: 6
                                    radius: 3
                                    color: root.pal.m3onPrimary
                                }
                            }
                        }
                    }
                }

                // ---- autoTile / tiles / raccourcis de tuilage : fenêtres rangées en tuiles ----
                Item {
                    id: tilesView

                    readonly property real gap: 3
                    readonly property real splitX: width * root.ratio

                    visible: root.tilesOn
                    anchors.fill: parent

                    Win {
                        x: root.tiled ? 0 : parent.width * 0.02
                        y: root.tiled ? 0 : parent.height * 0.04
                        width: root.tiled ? tilesView.splitX - tilesView.gap / 2 : parent.width * 0.55
                        height: root.tiled ? parent.height : parent.height * 0.62
                        outlined: root.tileVariant === "swap"
                    }
                    Win {
                        x: root.tiled ? tilesView.splitX + tilesView.gap / 2 : parent.width * 0.3
                        y: root.tiled ? 0 : parent.height * 0.22
                        width: root.tiled ? parent.width - tilesView.splitX - tilesView.gap / 2 : parent.width * 0.55
                        height: root.tiled ? parent.height / 2 - tilesView.gap / 2 : parent.height * 0.62
                        outlined: root.tileVariant === "focus" || root.tileVariant === "swap"
                    }
                    Win {
                        x: root.tiled ? tilesView.splitX + tilesView.gap / 2 : parent.width * 0.45
                        y: root.tiled ? parent.height / 2 + tilesView.gap / 2 : parent.height * 0.36
                        width: root.tiled ? parent.width - tilesView.splitX - tilesView.gap / 2 : parent.width * 0.5
                        height: root.tiled ? parent.height / 2 - tilesView.gap / 2 : parent.height * 0.58
                    }
                    // Fenêtre flottante par-dessus les tuiles (basculée en flottant, ou exclue).
                    Win {
                        visible: root.tileVariant === "float" || root.tileVariant === "exclude"
                        x: parent.width * 0.25
                        y: parent.height * 0.2
                        width: parent.width * 0.5
                        height: parent.height * 0.6
                        outlined: true
                        edge: root.tileVariant === "exclude" ? root.pal.m3error : root.pal.m3primary
                        color: root.tone(root.pal.m3onSurface, 0.3)
                    }
                    Badge {
                        visible: root.tileVariant !== ""
                        x: root.tileVariant === "swap" ? tilesView.splitX - width / 2 : root.tileVariant === "float" || root.tileVariant === "exclude" ? (parent.width - width) / 2 : tilesView.splitX / 2 - width / 2
                        y: root.tileVariant === "swap" ? parent.height / 4 - height / 2 : (parent.height - height) / 2
                        icon: root.tileVariant === "focus" ? "arrow_forward" : root.tileVariant === "swap" ? "swap_horiz" : root.tileVariant === "grow" ? "open_in_full" : root.tileVariant === "shrink" ? "close_fullscreen" : root.tileVariant === "float" ? "open_in_new" : "block"
                        color: root.tileVariant === "exclude" ? root.pal.m3error : root.pal.m3primaryContainer
                        fg: root.tileVariant === "exclude" ? root.pal.m3onError : root.pal.m3onPrimaryContainer
                    }
                }

                // ---- raccourcis : la surface qu'ils ouvrent ----
                // Panneau (tableau de bord, touches locales du panneau).
                Rectangle {
                    readonly property bool tabs: root.sid === "local-tabs"
                    readonly property bool isDash: root.sid === "dashboard" || root.sid === "dashboardFixed"

                    visible: root.plainShortcut && (isDash || root.sid.startsWith("local-"))
                    x: parent.width * 0.18
                    width: parent.width * 0.64
                    height: parent.height
                    radius: Appearance.rounding.small
                    color: root.tone(root.pal.m3onSurface, 0.14)
                    border.width: 1
                    border.color: Qt.alpha(root.pal.m3primary, 0.6)

                    Grid {
                        visible: parent.isDash
                        x: 5
                        y: 5
                        columns: 2
                        spacing: 3

                        Repeater {
                            model: 4

                            Rectangle {
                                width: (parent.parent.width - 13) / 2
                                height: (parent.parent.height - 13) / 2
                                radius: 3
                                color: root.tone(root.pal.m3primary, 0.35)
                            }
                        }
                    }
                    Row {
                        visible: parent.tabs
                        x: 5
                        y: 5
                        spacing: 3

                        Repeater {
                            model: 4

                            Rectangle {
                                required property int index

                                width: (parent.parent.width - 19) / 4
                                height: 5
                                radius: 2.5
                                color: index === 0 ? root.pal.m3primary : root.tone(root.pal.m3onSurface, 0.3)
                            }
                        }
                    }
                    Lines {
                        visible: !parent.isDash
                        x: 5
                        y: parent.tabs ? 16 : 5
                        width: parent.width - 10
                        tint: root.tone(root.pal.m3onSurface, 0.28)
                    }
                    Badge {
                        visible: !parent.isDash && !parent.tabs
                        x: parent.width - width - 4
                        y: parent.height - height - 4
                        icon: root.sid === "local-history" ? "content_paste" : root.sid === "local-search" ? "search" : "close"
                    }
                }
                // Fenêtre des paramètres.
                Win {
                    visible: root.plainShortcut && root.sid === "settings"
                    x: parent.width * 0.12
                    y: 0
                    width: parent.width * 0.76
                    height: parent.height

                    Rectangle {
                        y: 12
                        width: parent.width * 0.28
                        height: parent.height - 12
                        color: root.tone(root.pal.m3onSurface, 0.1)
                    }
                    Badge {
                        x: parent.width * 0.6 - width / 2
                        y: parent.height * 0.62 - height / 2
                        icon: "settings"
                    }
                }
                // Commande rapide : une barre de recherche au centre.
                Rectangle {
                    visible: root.plainShortcut && root.sid === "palette"
                    x: parent.width * 0.14
                    y: parent.height * 0.1
                    width: parent.width * 0.72
                    height: parent.height * 0.8
                    radius: Appearance.rounding.small
                    color: root.tone(root.pal.m3onSurface, 0.16)
                    border.width: 1
                    border.color: Qt.alpha(root.pal.m3primary, 0.6)

                    Rectangle {
                        x: 4
                        y: 4
                        width: parent.width - 8
                        height: 12
                        radius: 6
                        color: root.tone(root.pal.m3onSurface, 0.1)

                        MaterialIcon {
                            x: 3
                            anchors.verticalCenter: parent.verticalCenter
                            text: "search"
                            color: root.pal.m3primary
                            font.pointSize: Appearance.font.size.small
                        }
                    }
                    Lines {
                        x: 6
                        y: 22
                        width: parent.width - 12
                        tint: root.tone(root.pal.m3onSurface, 0.28)
                    }
                }
                // Fond d'écran suivant.
                Item {
                    visible: root.plainShortcut && root.sid === "wallpaper"
                    anchors.fill: parent
                    clip: true

                    Stripes {
                        anchors.fill: parent
                    }
                    Badge {
                        x: (parent.width - width) / 2
                        y: (parent.height - height) / 2
                        icon: "wallpaper"
                    }
                }
                // Capture : un cadre de sélection par-dessus une fenêtre.
                Item {
                    visible: root.plainShortcut && root.sid === "capture"
                    anchors.fill: parent

                    Win {
                        x: parent.width * 0.1
                        y: parent.height * 0.08
                        width: parent.width * 0.6
                        height: parent.height * 0.8
                    }
                    Rectangle {
                        x: parent.width * 0.3
                        y: parent.height * 0.22
                        width: parent.width * 0.58
                        height: parent.height * 0.62
                        radius: 2
                        color: Qt.alpha(root.pal.m3primary, 0.18)
                        border.width: 2
                        border.color: root.pal.m3primary
                    }
                    Badge {
                        x: parent.width * 0.88 - width
                        y: parent.height * 0.84 - height
                        icon: "photo_camera"
                    }
                }
                // Épingler : la fenêtre du premier plan reste devant.
                Item {
                    visible: root.plainShortcut && root.sid === "pin"
                    anchors.fill: parent

                    Win {
                        x: parent.width * 0.06
                        y: parent.height * 0.1
                        width: parent.width * 0.6
                        height: parent.height * 0.75
                    }
                    Win {
                        x: parent.width * 0.34
                        y: parent.height * 0.26
                        width: parent.width * 0.58
                        height: parent.height * 0.68
                        outlined: true
                        color: root.tone(root.pal.m3onSurface, 0.3)
                    }
                    Badge {
                        x: parent.width * 0.92 - width
                        y: parent.height * 0.06
                        icon: "push_pin"
                    }
                }
                // Dictée : fenêtre qui reçoit du texte et micro.
                Item {
                    visible: root.plainShortcut && root.sid === "dictation"
                    anchors.fill: parent

                    Win {
                        x: parent.width * 0.08
                        y: parent.height * 0.08
                        width: parent.width * 0.62
                        height: parent.height * 0.8
                    }
                    Badge {
                        x: parent.width * 0.88 - width
                        y: parent.height * 0.7 - height / 2
                        icon: "mic"
                        color: root.pal.m3primary
                        fg: root.pal.m3onPrimary
                    }
                }
                // Restaurer le bureau : les icônes reviennent.
                Item {
                    visible: root.plainShortcut && root.sid === "rescue"
                    anchors.fill: parent

                    Grid {
                        x: parent.width * 0.1
                        y: parent.height * 0.12
                        columns: 4
                        spacing: 5

                        Repeater {
                            model: 8

                            Rectangle {
                                width: 11
                                height: 11
                                radius: 3
                                color: root.tone(root.pal.m3primary, 0.5)
                            }
                        }
                    }
                    Badge {
                        x: parent.width * 0.88 - width
                        y: parent.height * 0.8 - height
                        icon: "restore"
                    }
                }
                // Raccourci inconnu : une simple fenêtre.
                Win {
                    visible: root.plainShortcut && ["dashboard", "dashboardFixed", "settings", "palette", "wallpaper", "capture", "pin", "dictation", "rescue"].indexOf(root.sid) < 0 && !root.sid.startsWith("local-")
                    x: parent.width * 0.2
                    width: parent.width * 0.6
                    height: parent.height
                }

                // ---- osd : l'indicateur de volume en bas de l'écran ----
                Item {
                    visible: root.kind === "osd"
                    anchors.fill: parent

                    Win {
                        x: parent.width * 0.12
                        y: parent.height * 0.08
                        width: parent.width * 0.76
                        height: parent.height * 0.5
                    }
                    Fade {
                        x: (parent.width - width) / 2
                        y: parent.height - height - 4
                        width: parent.width * 0.5
                        height: 16
                        radius: 8
                        color: root.pal.m3inverseSurface
                        opacity: root.flag ? 1 : 0.2

                        MaterialIcon {
                            x: 5
                            anchors.verticalCenter: parent.verticalCenter
                            text: "volume_up"
                            color: root.pal.m3inverseOnSurface
                            font.pointSize: Appearance.font.size.small
                        }
                        Rectangle {
                            x: 22
                            anchors.verticalCenter: parent.verticalCenter
                            width: parent.width - 30
                            height: 3
                            radius: 1.5
                            color: Qt.alpha(root.pal.m3inverseOnSurface, 0.35)

                            Rectangle {
                                width: parent.width * 0.6
                                height: parent.height
                                radius: 1.5
                                color: root.pal.m3inverseOnSurface
                            }
                        }
                    }
                }

                // ---- pill : la capsule média en haut de l'écran ----
                Item {
                    visible: root.kind === "pill"
                    anchors.fill: parent

                    Win {
                        x: parent.width * 0.12
                        y: parent.height * 0.3
                        width: parent.width * 0.76
                        height: parent.height * 0.62
                    }
                    Fade {
                        x: (parent.width - width) / 2
                        y: 2
                        width: parent.width * 0.62
                        height: 16
                        radius: 8
                        color: root.pal.m3inverseSurface
                        opacity: root.flag ? 1 : 0.2

                        MaterialIcon {
                            x: 5
                            anchors.verticalCenter: parent.verticalCenter
                            text: "music_note"
                            color: root.pal.m3inverseOnSurface
                            font.pointSize: Appearance.font.size.small
                        }
                        Rectangle {
                            x: 22
                            anchors.verticalCenter: parent.verticalCenter
                            width: parent.width - 30
                            height: 3
                            radius: 1.5
                            color: root.pal.m3inverseOnSurface
                        }
                    }
                }

                // ---- lyrics : les paroles sous le morceau en cours ----
                Item {
                    visible: root.kind === "lyrics"
                    anchors.fill: parent

                    Rectangle {
                        x: parent.width * 0.1
                        y: parent.height * 0.06
                        width: parent.width * 0.8
                        height: parent.height * 0.4
                        radius: Appearance.rounding.small
                        color: root.tone(root.pal.m3onSurface, 0.16)

                        Rectangle {
                            x: 4
                            anchors.verticalCenter: parent.verticalCenter
                            width: parent.height - 8
                            height: width
                            radius: 3
                            color: root.tone(root.pal.m3tertiary, 0.6)
                        }
                        Lines {
                            x: parent.height
                            anchors.verticalCenter: parent.verticalCenter
                            width: parent.width - parent.height - 6
                            tint: root.tone(root.pal.m3onSurface, 0.3)
                        }
                    }
                    Column {
                        x: parent.width * 0.18
                        y: parent.height * 0.58
                        width: parent.width * 0.64
                        spacing: 4

                        Fade {
                            width: parent.width
                            height: 5
                            radius: 2.5
                            color: root.pal.m3primary
                            opacity: root.flag ? 1 : 0.15
                        }
                        Fade {
                            width: parent.width * 0.7
                            height: 4
                            radius: 2
                            color: root.tone(root.pal.m3onSurface, 0.35)
                            opacity: root.flag ? 1 : 0.15
                        }
                    }
                }

                // ---- dictation : micro, touche maintenue et texte dans une fenêtre ----
                Item {
                    visible: root.kind === "dictation"
                    anchors.fill: parent

                    Win {
                        x: parent.width * 0.08
                        y: parent.height * 0.08
                        width: parent.width * 0.62
                        height: parent.height * 0.8
                        linesOpacity: root.flag ? 1 : 0.15
                    }
                    Badge {
                        x: parent.width * 0.88 - width
                        y: parent.height * 0.7 - height / 2
                        icon: "mic"
                        color: root.flag ? root.pal.m3primary : root.tone(root.pal.m3onSurface, 0.2)
                        fg: root.flag ? root.pal.m3onPrimary : root.pal.m3outline
                    }
                }
            }

            // Touche(s) du raccourci ou touche de dictée, dessinée comme une touche de clavier.
            Rectangle {
                visible: root.capText !== ""
                anchors.horizontalCenter: parent.horizontalCenter
                anchors.bottom: parent.bottom
                anchors.bottomMargin: 2
                width: Math.min(parent.width - 8, capLabel.implicitWidth + 12)
                height: 14
                radius: Appearance.rounding.tiny * 1.5
                color: root.tone(root.pal.m3onSurface, 0.2)
                border.width: 1
                border.color: Qt.alpha(root.pal.m3outline, 0.7)

                StyledText {
                    id: capLabel

                    anchors.centerIn: parent
                    width: parent.width - 8
                    horizontalAlignment: Text.AlignHCenter
                    elide: Text.ElideRight
                    text: root.capText
                    font.pointSize: Appearance.font.size.small
                    color: root.pal.m3onSurface
                }
            }
        }
    }
}
