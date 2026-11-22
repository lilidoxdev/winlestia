import QtQuick
import qs.components
import qs.services
import qs.config
import "."
import ".."

// Aperçu décoratif (≈ 120×72) des pages Applications, Fichiers, Général, Winjets et Verrouillage.
// Même vocabulaire que OptionPreview : mini-écran arrondi, mini-fenêtres, tout en palette, rien ne tourne au repos.
// kind : bookmarksBrowser | bookmarksUnhide | socialBackground | socialNetworks | socialMute | socialSound | socialVolume
//        | clipboardHistory | clipboardImages | clipboardLimit
//        | explorerTheme | explorerFolders | filesDefaultHandler | filesPreload
//        | language | autostart | weatherCity | refreshCatalogue | sensors | sensorsConsent | restoreTaskbar | about | tutorial | discord
//        | winjetsEdit | winjetsRecover
//        | lockPassword | lockEnabled | lockIdle | lockIdleTime | lockShortcut | lockNow
// value : interrupteur (booléen), nombre, identifiant ou liste selon le kind. value2 : variante (lockPassword : current|new|confirm).
Item {
    id: root

    property string kind
    property var value
    property var value2
    property string label: qsTr("Aperçu")

    readonly property var pal: Colours.palette
    readonly property bool flag: value === true
    readonly property bool animated: !Appearance.reducedEffects
    readonly property real num: Number(value) || 0

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
    function idleText(sec: real): string {
        const m = Math.floor(sec / 60);
        const s = Math.round(sec % 60);
        return m > 0 ? (s > 0 ? qsTr("%1 min").arg(m) + " " + qsTr("%1 s").arg(s) : qsTr("%1 min").arg(m)) : qsTr("%1 s").arg(s);
    }

    // Élément qui apparaît/disparaît en fondu quand l'interrupteur change (rien du tout avec les effets allégés).
    component Fade: Item {
        property bool on: true

        opacity: on ? 1 : 0

        Behavior on opacity {
            enabled: root.animated

            Anim {
                duration: Appearance.anim.durations.small
            }
        }
    }

    component Bar: Rectangle {
        height: 3
        radius: 1.5
        color: root.tone(root.pal.m3onSurface, 0.28)
    }

    component Ico: MaterialIcon {
        color: root.pal.m3primary
        font.pointSize: Appearance.font.size.large
    }

    component Lines: Column {
        id: lines

        property color tint: root.tone(root.pal.m3onSurface, 0.28)

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

    // Mini-fenêtre : barre de titre + zone de contenu (enfants placés dans `content`).
    component MiniWin: Rectangle {
        id: mw

        property bool outlined: false
        property bool dashed: false
        default property alias content: body.data

        radius: Appearance.rounding.tiny * 1.5
        color: root.tone(root.pal.m3onSurface, 0.16)
        border.width: outlined ? 1.5 : 0
        border.color: root.pal.m3primary

        Rectangle {
            x: 3
            y: 3
            width: parent.width - 6
            height: 4
            radius: 2
            color: root.tone(root.pal.m3onSurface, 0.3)
        }
        Item {
            id: body

            x: 3
            y: 10
            width: parent.width - 6
            height: parent.height - 13
        }
    }

    // Petit dossier : onglet + corps.
    component Fold: Item {
        property color tint: root.pal.m3primary

        width: 16
        height: 12

        Rectangle {
            width: 7
            height: 4
            radius: 1.5
            color: parent.tint
        }
        Rectangle {
            y: 2
            width: parent.width
            height: parent.height - 2
            radius: 2.5
            color: parent.tint
        }
    }

    // Écran de verrouillage miniature : bureau flouté, avatar, champ à points, horloge.
    component LockFace: Item {
        id: face

        property bool locked: true
        property int dots: 0
        property bool field: true
        property string fieldState: "idle"

        anchors.fill: parent

        // Fond : le bureau, plus sombre quand l'écran est verrouillé.
        MiniWin {
            x: 8
            y: 6
            width: parent.width * 0.45
            height: parent.height * 0.6
            opacity: face.locked ? 0.35 : 1
        }
        MiniWin {
            x: parent.width * 0.5
            y: 14
            width: parent.width * 0.42
            height: parent.height * 0.6
            opacity: face.locked ? 0.35 : 1
        }
        Rectangle {
            anchors.fill: parent
            radius: stage.radius
            color: Qt.alpha(root.pal.m3scrim, face.locked ? 0.45 : 0)
        }
        Fade {
            on: face.locked
            anchors.fill: parent

            Rectangle {
                x: (parent.width - width) / 2
                y: 5
                width: 14
                height: 14
                radius: 7
                color: root.pal.m3primaryContainer

                MaterialIcon {
                    anchors.centerIn: parent
                    text: "person"
                    color: root.pal.m3onPrimaryContainer
                    font.pointSize: Appearance.font.size.small
                }
            }
            Rectangle {
                visible: face.field
                x: (parent.width - width) / 2
                y: 24
                width: parent.width * 0.55
                height: 11
                radius: 5.5
                color: root.tone(root.pal.m3onSurface, 0.22)
                border.width: 1
                border.color: face.fieldState === "bad" ? root.pal.m3error : face.fieldState === "good" ? root.pal.m3primary : Qt.alpha(root.pal.m3outline, 0.6)

                Row {
                    anchors.centerIn: parent
                    spacing: 3

                    Repeater {
                        model: Math.min(face.dots, 8)

                        Rectangle {
                            width: 4
                            height: 4
                            radius: 2
                            color: root.pal.m3onSurface
                        }
                    }
                }
            }
        }
        // Cadenas : fermé quand verrouillé, ouvert sinon.
        MaterialIcon {
            x: (parent.width - width) / 2
            y: parent.height - height - 5
            text: face.locked ? "lock" : "lock_open"
            color: face.locked ? root.pal.m3primary : root.pal.m3outline
            font.pointSize: Appearance.font.size.normal
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

            // ---- bookmarksBrowser : value = identifiant du navigateur choisi ----
            Item {
                visible: root.kind === "bookmarksBrowser"
                anchors.fill: parent

                MiniWin {
                    anchors.fill: parent
                    anchors.margins: 6

                    Rectangle {
                        width: 11
                        height: 11
                        radius: 5.5
                        color: root.pal.m3primaryContainer

                        StyledText {
                            anchors.centerIn: parent
                            text: String(root.value ?? "").charAt(0).toUpperCase()
                            font.pointSize: Appearance.font.size.smaller
                            font.weight: Font.Bold
                            color: root.pal.m3onPrimaryContainer
                        }
                    }
                    Rectangle {
                        x: 14
                        width: parent.width - 14
                        height: 11
                        radius: 5.5
                        color: root.tone(root.pal.m3onSurface, 0.1)
                    }
                    Row {
                        y: 16
                        spacing: 3

                        Repeater {
                            model: 4

                            Rectangle {
                                width: 16
                                height: 7
                                radius: 3.5
                                color: root.tone(root.pal.m3primary, 0.55)
                            }
                        }
                    }
                }
            }

            // ---- bookmarksUnhide : value = nombre de favoris retirés (revient en pointillé) ----
            Item {
                visible: root.kind === "bookmarksUnhide"
                anchors.fill: parent

                MiniWin {
                    anchors.fill: parent
                    anchors.margins: 6

                    Row {
                        y: 3
                        spacing: 3

                        Repeater {
                            model: 3

                            Rectangle {
                                width: 16
                                height: 7
                                radius: 3.5
                                color: root.tone(root.pal.m3primary, 0.55)
                            }
                        }
                    }
                    Row {
                        y: 16
                        spacing: 3

                        Repeater {
                            model: Math.min(3, root.num)

                            Rectangle {
                                width: 16
                                height: 7
                                radius: 3.5
                                color: "transparent"
                                border.width: 1
                                border.color: root.pal.m3primary
                            }
                        }
                    }
                }
                MaterialIcon {
                    anchors.right: parent.right
                    anchors.bottom: parent.bottom
                    anchors.margins: 6
                    text: "visibility"
                    color: root.num > 0 ? root.pal.m3primary : root.pal.m3outline
                    font.pointSize: Appearance.font.size.normal
                }
            }

            // ---- socialBackground : vrai = messageries chargées (pastille, mémoire utilisée) ----
            Item {
                visible: root.kind === "socialBackground"
                anchors.fill: parent

                Rectangle {
                    x: 12
                    y: 12
                    width: 22
                    height: 22
                    radius: 7
                    color: root.pal.m3primaryContainer
                    opacity: root.flag ? 1 : 0.45

                    MaterialIcon {
                        anchors.centerIn: parent
                        text: "chat"
                        color: root.pal.m3onPrimaryContainer
                        font.pointSize: Appearance.font.size.normal
                    }
                }
                Fade {
                    on: root.flag
                    x: 28
                    y: 8
                    width: 11
                    height: 11

                    Rectangle {
                        anchors.fill: parent
                        radius: 5.5
                        color: root.pal.m3error

                        StyledText {
                            anchors.centerIn: parent
                            text: "3"
                            font.pointSize: Appearance.font.size.smaller
                            font.weight: Font.Bold
                            color: root.pal.m3onError
                        }
                    }
                }
                Column {
                    x: parent.width * 0.5
                    y: 14
                    spacing: 6

                    Repeater {
                        model: ["sync", "memory"]

                        Row {
                            required property int index
                            required property string modelData

                            spacing: 4

                            MaterialIcon {
                                text: parent.modelData
                                color: root.pal.m3onSurfaceVariant
                                font.pointSize: Appearance.font.size.small
                            }
                            Rectangle {
                                anchors.verticalCenter: parent.verticalCenter
                                width: root.flag ? 26 : (parent.index === 0 ? 4 : 9)
                                height: 4
                                radius: 2
                                color: parent.index === 0 ? root.pal.m3primary : root.pal.m3tertiary

                                Behavior on width {
                                    enabled: root.animated

                                    Anim {
                                        duration: Appearance.anim.durations.small
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // ---- socialNetworks : value = liste des réseaux {id, connected, enabled} ----
            Item {
                visible: root.kind === "socialNetworks"
                anchors.fill: parent

                Row {
                    anchors.centerIn: parent
                    spacing: 7

                    Repeater {
                        model: Array.isArray(root.value) ? root.value.slice(0, 4) : []

                        Item {
                            required property var modelData

                            width: 20
                            height: 24

                            Rectangle {
                                width: 20
                                height: 20
                                radius: 6
                                color: root.tone(root.pal.m3onSurface, 0.14)

                                Image {
                                    anchors.centerIn: parent
                                    width: 14
                                    height: 14
                                    sourceSize.width: 28
                                    sourceSize.height: 28
                                    fillMode: Image.PreserveAspectFit
                                    source: Bridge.localUrl(Bridge.assetRoot + "/social/" + parent.parent.modelData.id + ".svg")
                                    opacity: parent.parent.modelData.enabled ? 1 : 0.35
                                }
                            }
                            Rectangle {
                                anchors.horizontalCenter: parent.horizontalCenter
                                anchors.bottom: parent.bottom
                                width: 6
                                height: 6
                                radius: 3
                                color: modelData.connected ? root.pal.m3primary : root.pal.m3outlineVariant
                            }
                        }
                    }
                }
            }

            // ---- socialMute : vrai = tout coupé (cloche barrée, pas de bannière) ----
            Item {
                visible: root.kind === "socialMute"
                anchors.fill: parent

                Rectangle {
                    x: 8
                    y: 8
                    width: parent.width - 16
                    height: 16
                    radius: 6
                    color: root.tone(root.pal.m3onSurface, 0.18)
                    opacity: root.flag ? 0.3 : 1

                    Behavior on opacity {
                        enabled: root.animated

                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }

                    Rectangle {
                        x: 4
                        anchors.verticalCenter: parent.verticalCenter
                        width: 8
                        height: 8
                        radius: 4
                        color: root.pal.m3primary
                    }
                    Lines {
                        x: 17
                        y: 4
                        width: parent.width - 22
                    }
                }
                Ico {
                    anchors.horizontalCenter: parent.horizontalCenter
                    y: 28
                    text: root.flag ? "notifications_off" : "notifications"
                    color: root.flag ? root.pal.m3outline : root.pal.m3primary
                }
            }

            // ---- socialSound : vrai = ondes sonores visibles ----
            Item {
                visible: root.kind === "socialSound"
                anchors.fill: parent

                Ico {
                    anchors.centerIn: parent
                    text: root.flag ? "volume_up" : "volume_off"
                    color: root.flag ? root.pal.m3primary : root.pal.m3outline
                }
            }

            // ---- socialVolume : value = 0..100, barres éclairées selon le niveau ----
            Item {
                visible: root.kind === "socialVolume"
                anchors.fill: parent

                Ico {
                    x: 14
                    anchors.verticalCenter: parent.verticalCenter
                    text: root.num <= 0 ? "volume_off" : root.num < 50 ? "volume_down" : "volume_up"
                }
                Row {
                    x: 46
                    anchors.verticalCenter: parent.verticalCenter
                    height: 30
                    spacing: 3

                    Repeater {
                        model: 6

                        Rectangle {
                            required property int index

                            anchors.bottom: parent.bottom
                            width: 5
                            height: 6 + index * 4.5
                            radius: 2
                            color: root.num >= (index + 1) * 100 / 6 - 8 && root.num > 0 ? root.pal.m3primary : root.tone(root.pal.m3onSurface, 0.2)
                        }
                    }
                }
            }

            // ---- clipboardHistory : vrai = pile d'éléments gardés, faux = rien conservé ----
            Item {
                visible: root.kind === "clipboardHistory"
                anchors.fill: parent

                Column {
                    anchors.centerIn: parent
                    spacing: 3

                    Repeater {
                        model: 3

                        Rectangle {
                            required property int index

                            width: 70
                            height: 12
                            radius: 4
                            color: root.tone(root.pal.m3onSurface, index === 0 ? 0.26 : 0.16)
                            opacity: index === 0 || root.flag ? 1 : 0

                            Behavior on opacity {
                                enabled: root.animated

                                Anim {
                                    duration: Appearance.anim.durations.small
                                }
                            }

                            Bar {
                                x: 5
                                anchors.verticalCenter: parent.verticalCenter
                                width: parent.width * (index === 0 ? 0.6 : index === 1 ? 0.45 : 0.7)
                            }
                        }
                    }
                }
                MaterialIcon {
                    anchors.right: parent.right
                    anchors.top: parent.top
                    anchors.margins: 5
                    text: root.flag ? "history" : "block"
                    color: root.flag ? root.pal.m3primary : root.pal.m3outline
                    font.pointSize: Appearance.font.size.normal
                }
            }

            // ---- clipboardImages : vrai = la capture reste dans la liste ----
            Item {
                visible: root.kind === "clipboardImages"
                anchors.fill: parent

                Column {
                    anchors.centerIn: parent
                    spacing: 3

                    Rectangle {
                        width: 74
                        height: 12
                        radius: 4
                        color: root.tone(root.pal.m3onSurface, 0.16)

                        Bar {
                            x: 5
                            anchors.verticalCenter: parent.verticalCenter
                            width: 42
                        }
                    }
                    Fade {
                        on: root.flag
                        width: 74
                        height: 24

                        Rectangle {
                            anchors.fill: parent
                            radius: 4
                            color: root.tone(root.pal.m3onSurface, 0.16)

                            Rectangle {
                                x: 4
                                y: 4
                                width: 24
                                height: 16
                                radius: 3
                                color: root.tone(root.pal.m3tertiary, 0.5)

                                MaterialIcon {
                                    anchors.centerIn: parent
                                    text: "image"
                                    color: root.pal.m3tertiary
                                    font.pointSize: Appearance.font.size.small
                                }
                            }
                            Bar {
                                x: 34
                                anchors.verticalCenter: parent.verticalCenter
                                width: 30
                            }
                        }
                    }
                }
            }

            // ---- clipboardLimit : value = nombre maximal d'éléments (8..80) ----
            Item {
                visible: root.kind === "clipboardLimit"
                anchors.fill: parent

                Column {
                    x: 14
                    anchors.verticalCenter: parent.verticalCenter
                    spacing: 2

                    Repeater {
                        model: 6

                        Rectangle {
                            required property int index

                            width: 56
                            height: 6
                            radius: 3
                            color: root.num >= 8 + index * 14 ? root.tone(root.pal.m3primary, 0.6) : "transparent"
                            border.width: 1
                            border.color: root.tone(root.pal.m3primary, 0.4)
                        }
                    }
                }
                StyledText {
                    anchors.right: parent.right
                    anchors.rightMargin: 12
                    anchors.verticalCenter: parent.verticalCenter
                    text: String(Math.round(root.num))
                    font.pointSize: Appearance.font.size.larger
                    font.weight: Font.Bold
                    color: root.pal.m3primary
                }
            }

            // ---- explorerTheme : vrai = dossiers à la couleur du thème, faux = jaune/gris neutre ----
            Item {
                visible: root.kind === "explorerTheme"
                anchors.fill: parent

                MiniWin {
                    anchors.fill: parent
                    anchors.margins: 6

                    Row {
                        spacing: 6

                        Repeater {
                            model: 4

                            Fold {
                                tint: root.flag ? root.pal.m3primary : root.tone(root.pal.m3onSurface, 0.45)
                            }
                        }
                    }
                    Row {
                        y: 16
                        spacing: 6

                        Repeater {
                            model: 2

                            Fold {
                                tint: root.flag ? root.pal.m3tertiary : root.tone(root.pal.m3onSurface, 0.45)
                            }
                        }
                    }
                }
            }

            // ---- explorerFolders : action, fenêtre avec un dossier ajouté ----
            Item {
                visible: root.kind === "explorerFolders"
                anchors.fill: parent

                MiniWin {
                    anchors.fill: parent
                    anchors.margins: 6

                    Row {
                        spacing: 6

                        Repeater {
                            model: 2

                            Fold {
                                tint: root.tone(root.pal.m3onSurface, 0.45)
                            }
                        }
                        Fold {
                            tint: root.pal.m3primary
                        }
                    }
                    MaterialIcon {
                        x: 40
                        y: 14
                        text: "add_circle"
                        color: root.pal.m3primary
                        font.pointSize: Appearance.font.size.normal
                    }
                }
            }

            // ---- filesDefaultHandler : un dossier s'ouvre dans Winlestia Fichiers (vrai) ou dans l'Explorateur ----
            Item {
                visible: root.kind === "filesDefaultHandler"
                anchors.fill: parent

                Fold {
                    x: 12
                    anchors.verticalCenter: parent.verticalCenter
                    tint: root.pal.m3primary
                }
                MaterialIcon {
                    x: 36
                    anchors.verticalCenter: parent.verticalCenter
                    text: "arrow_forward"
                    color: root.pal.m3outline
                    font.pointSize: Appearance.font.size.normal
                }
                MiniWin {
                    x: 58
                    anchors.verticalCenter: parent.verticalCenter
                    width: 48
                    height: 44
                    outlined: root.flag

                    Rectangle {
                        width: parent.width
                        height: 12
                        radius: 3
                        color: root.flag ? root.pal.m3primaryContainer : root.tone(root.pal.m3onSurface, 0.3)

                        StyledText {
                            anchors.centerIn: parent
                            text: root.flag ? "W" : "E"
                            font.pointSize: Appearance.font.size.smaller
                            font.weight: Font.Bold
                            color: root.flag ? root.pal.m3onPrimaryContainer : root.pal.m3onSurface
                        }
                    }
                    Lines {
                        y: 16
                        width: parent.width
                    }
                }
            }

            // ---- filesPreload : vrai = fenêtre déjà prête (éclair), faux = il faut attendre ----
            Item {
                visible: root.kind === "filesPreload"
                anchors.fill: parent

                MiniWin {
                    x: 20
                    anchors.verticalCenter: parent.verticalCenter
                    width: 54
                    height: 46
                    opacity: root.flag ? 1 : 0.35
                    outlined: root.flag

                    Behavior on opacity {
                        enabled: root.animated

                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }

                    Lines {
                        width: parent.width
                    }
                }
                Ico {
                    x: 82
                    anchors.verticalCenter: parent.verticalCenter
                    text: root.flag ? "bolt" : "hourglass_empty"
                    color: root.flag ? root.pal.m3primary : root.pal.m3outline
                }
            }

            // ---- language : value = fr | en | es | auto ----
            Item {
                visible: root.kind === "language"
                anchors.fill: parent

                Rectangle {
                    anchors.fill: parent
                    anchors.margins: 8
                    radius: Appearance.rounding.small
                    color: root.tone(root.pal.m3onSurface, 0.14)

                    Rectangle {
                        id: flagBox

                        x: 6
                        anchors.verticalCenter: parent.verticalCenter
                        width: 28
                        height: 28
                        radius: 14
                        color: root.pal.m3primaryContainer
                        clip: true

                        Image {
                            anchors.fill: parent
                            visible: root.value !== "auto"
                            fillMode: Image.PreserveAspectCrop
                            sourceSize.width: 56
                            sourceSize.height: 56
                            source: root.kind === "language" ? Bridge.localUrl(Bridge.assetRoot + "/flags/" + (root.value === "auto" ? "windows" : root.value) + ".svg") : ""
                        }
                        MaterialIcon {
                            anchors.centerIn: parent
                            visible: root.value === "auto"
                            text: "desktop_windows"
                            color: root.pal.m3onPrimaryContainer
                            font.pointSize: Appearance.font.size.normal
                        }
                    }
                    Column {
                        x: 40
                        anchors.verticalCenter: parent.verticalCenter
                        spacing: 3

                        StyledText {
                            text: root.value === "fr" ? "Bonjour" : root.value === "es" ? "Hola" : root.value === "en" ? "Hello" : root.value === "ru" ? "Привет" : "Windows"
                            font.pointSize: Appearance.font.size.normal
                            font.weight: Font.Medium
                            color: root.pal.m3onSurface
                        }
                        Bar {
                            width: 30
                        }
                    }
                }
            }

            // ---- autostart : vrai = Winlestia apparaît tout de suite après l'ouverture de session ----
            Item {
                visible: root.kind === "autostart"
                anchors.fill: parent

                Rectangle {
                    x: 8
                    anchors.bottom: parent.bottom
                    anchors.bottomMargin: 6
                    width: parent.width - 16
                    height: 8
                    radius: 4
                    color: root.tone(root.pal.m3onSurface, 0.2)
                }
                Fade {
                    on: root.flag
                    anchors.fill: parent

                    MiniWin {
                        x: 12
                        y: 6
                        width: 56
                        height: 36
                    }
                    Rectangle {
                        anchors.right: parent.right
                        anchors.rightMargin: 12
                        y: 8
                        width: 28
                        height: 28
                        radius: Appearance.rounding.smaller
                        color: root.pal.m3primaryContainer

                        StyledText {
                            anchors.centerIn: parent
                            text: "W"
                            font.pointSize: Appearance.font.size.large
                            font.weight: Font.Bold
                            color: root.pal.m3onPrimaryContainer
                        }
                    }
                }
                Ico {
                    anchors.horizontalCenter: parent.horizontalCenter
                    y: 12
                    visible: !root.flag
                    text: "power_settings_new"
                    color: root.pal.m3outline
                }
            }

            // ---- weatherCity : épingle + météo du tableau de bord ----
            Item {
                visible: root.kind === "weatherCity"
                anchors.fill: parent

                Ico {
                    x: 16
                    anchors.verticalCenter: parent.verticalCenter
                    text: "location_on"
                }
                Rectangle {
                    x: 44
                    anchors.verticalCenter: parent.verticalCenter
                    width: 62
                    height: 40
                    radius: Appearance.rounding.small
                    color: root.tone(root.pal.m3onSurface, 0.16)

                    MaterialIcon {
                        x: 6
                        y: 5
                        text: "partly_cloudy_day"
                        color: root.pal.m3tertiary
                        font.pointSize: Appearance.font.size.large
                    }
                    Bar {
                        x: 6
                        y: 28
                        width: 36
                    }
                }
            }

            // ---- refreshCatalogue : la liste des applications est relue ----
            Item {
                visible: root.kind === "refreshCatalogue"
                anchors.fill: parent

                Grid {
                    x: 14
                    anchors.verticalCenter: parent.verticalCenter
                    columns: 3
                    spacing: 5

                    Repeater {
                        model: 6

                        Rectangle {
                            width: 16
                            height: 16
                            radius: 5
                            color: root.tone(root.pal.m3primary, 0.45)
                        }
                    }
                }
                Ico {
                    x: 82
                    anchors.verticalCenter: parent.verticalCenter
                    text: "refresh"
                }
            }

            // ---- sensors : value = état (active, stopped, absent…) ----
            Item {
                visible: root.kind === "sensors"
                anchors.fill: parent

                Ico {
                    x: 16
                    anchors.verticalCenter: parent.verticalCenter
                    text: "thermostat"
                    color: root.value === "active" ? root.pal.m3primary : root.pal.m3outline
                }
                Rectangle {
                    x: 44
                    anchors.verticalCenter: parent.verticalCenter
                    width: 62
                    height: 28
                    radius: Appearance.rounding.small
                    color: root.tone(root.pal.m3onSurface, 0.16)

                    StyledText {
                        anchors.centerIn: parent
                        text: root.value === "active" ? "58 °C" : "-- °C"
                        font.pointSize: Appearance.font.size.normal
                        font.weight: Font.Medium
                        color: root.value === "active" ? root.pal.m3primary : root.pal.m3outline
                    }
                }
            }

            // ---- sensorsConsent : service administrateur, autorisation Windows ----
            Item {
                visible: root.kind === "sensorsConsent"
                anchors.fill: parent

                Ico {
                    anchors.centerIn: parent
                    text: "admin_panel_settings"
                }
            }

            // ---- restoreTaskbar : barre des tâches Windows qui revient en bas ----
            Item {
                visible: root.kind === "restoreTaskbar"
                anchors.fill: parent

                MiniWin {
                    x: 12
                    y: 6
                    width: parent.width - 24
                    height: 34
                }
                Rectangle {
                    anchors.bottom: parent.bottom
                    width: parent.width
                    height: 12
                    color: root.tone(root.pal.m3onSurface, 0.3)

                    Row {
                        anchors.centerIn: parent
                        spacing: 4

                        Repeater {
                            model: 5

                            Rectangle {
                                width: 6
                                height: 6
                                radius: 2
                                color: root.pal.m3primary
                            }
                        }
                    }
                }
            }

            // ---- about : carte de version ----
            Item {
                visible: root.kind === "about"
                anchors.fill: parent

                Rectangle {
                    x: 14
                    anchors.verticalCenter: parent.verticalCenter
                    width: 30
                    height: 30
                    radius: Appearance.rounding.smaller
                    color: root.pal.m3primaryContainer

                    StyledText {
                        anchors.centerIn: parent
                        text: "W"
                        font.pointSize: Appearance.font.size.large
                        font.weight: Font.Bold
                        color: root.pal.m3onPrimaryContainer
                    }
                }
                Column {
                    x: 52
                    anchors.verticalCenter: parent.verticalCenter
                    spacing: 4

                    Bar {
                        width: 44
                        color: root.pal.m3primary
                    }
                    Bar {
                        width: 30
                    }
                    Bar {
                        width: 38
                    }
                }
            }

            // ---- tutorial : surbrillance sur un élément + points d'étapes ----
            Item {
                visible: root.kind === "tutorial"
                anchors.fill: parent

                MiniWin {
                    x: 10
                    y: 6
                    width: 60
                    height: 40
                    outlined: true
                }
                Rectangle {
                    x: 62
                    y: 20
                    width: 44
                    height: 24
                    radius: Appearance.rounding.small
                    color: root.pal.m3primaryContainer

                    MaterialIcon {
                        anchors.centerIn: parent
                        text: "tour"
                        color: root.pal.m3onPrimaryContainer
                        font.pointSize: Appearance.font.size.large
                    }
                }
                Row {
                    anchors.horizontalCenter: parent.horizontalCenter
                    anchors.bottom: parent.bottom
                    anchors.bottomMargin: 5
                    spacing: 4

                    Repeater {
                        model: 4

                        Rectangle {
                            required property int index

                            width: 5
                            height: 5
                            radius: 2.5
                            color: index === 0 ? root.pal.m3primary : root.tone(root.pal.m3onSurface, 0.3)
                        }
                    }
                }
            }

            // ---- discord : le logo dans une carte, avec deux bulles de discussion ----
            Item {
                visible: root.kind === "discord"
                anchors.fill: parent

                Rectangle {
                    x: 14
                    anchors.verticalCenter: parent.verticalCenter
                    width: 36
                    height: 36
                    radius: Appearance.rounding.small
                    color: root.pal.m3primaryContainer

                    DiscordLogo {
                        anchors.centerIn: parent
                        size: 22
                        color: root.pal.m3onPrimaryContainer
                    }
                }
                Column {
                    x: 58
                    anchors.verticalCenter: parent.verticalCenter
                    spacing: 4

                    Rectangle {
                        width: 46
                        height: 10
                        radius: 5
                        color: root.tone(root.pal.m3primary, 0.5)
                    }
                    Rectangle {
                        x: 10
                        width: 36
                        height: 10
                        radius: 5
                        color: root.tone(root.pal.m3onSurface, 0.22)
                    }
                    Rectangle {
                        width: 28
                        height: 10
                        radius: 5
                        color: root.tone(root.pal.m3primary, 0.5)
                    }
                }
            }

            // ---- winjetsEdit : vrai = cadre pointillé, poignée de taille et croix de retrait sur la carte ----
            Item {
                visible: root.kind === "winjetsEdit"
                anchors.fill: parent

                Rectangle {
                    x: 8
                    anchors.bottom: parent.bottom
                    anchors.bottomMargin: 5
                    width: parent.width - 16
                    height: 6
                    radius: 3
                    color: root.tone(root.pal.m3onSurface, 0.2)
                }
                Rectangle {
                    id: card

                    x: parent.width - width - 14
                    y: 8
                    width: 50
                    height: 34
                    radius: Appearance.rounding.small
                    color: root.tone(root.pal.m3onSurface, 0.2)
                    border.width: root.flag ? 1.5 : 0
                    border.color: root.pal.m3primary

                    Lines {
                        x: 6
                        y: 8
                        width: parent.width - 12
                    }
                    Fade {
                        on: root.flag
                        anchors.fill: parent

                        Rectangle {
                            anchors.right: parent.right
                            anchors.bottom: parent.bottom
                            anchors.margins: -3
                            width: 7
                            height: 7
                            radius: 3.5
                            color: root.pal.m3primary
                        }
                        Rectangle {
                            anchors.right: parent.right
                            anchors.top: parent.top
                            anchors.margins: -3
                            width: 9
                            height: 9
                            radius: 4.5
                            color: root.pal.m3error
                        }
                    }
                }
                Fade {
                    on: root.flag
                    x: 10
                    y: 12
                    width: 20
                    height: 20

                    MaterialIcon {
                        anchors.centerIn: parent
                        text: "open_with"
                        color: root.pal.m3primary
                        font.pointSize: Appearance.font.size.large
                    }
                }
            }

            // ---- winjetsRecover : une carte perdue hors écran revient sur l'écran principal ----
            Item {
                visible: root.kind === "winjetsRecover"
                anchors.fill: parent

                Rectangle {
                    x: 6
                    y: 12
                    width: 22
                    height: 18
                    radius: 3
                    color: "transparent"
                    border.width: 1
                    border.color: root.pal.m3outline

                    Rectangle {
                        x: 4
                        y: 4
                        width: 10
                        height: 8
                        radius: 2
                        color: root.pal.m3primary
                    }
                }
                MaterialIcon {
                    x: 32
                    y: 14
                    text: "arrow_forward"
                    color: root.pal.m3outline
                    font.pointSize: Appearance.font.size.normal
                }
                Rectangle {
                    x: 52
                    y: 6
                    width: 54
                    height: 40
                    radius: 3
                    color: root.tone(root.pal.m3onSurface, 0.12)
                    border.width: 1
                    border.color: root.pal.m3outline

                    Rectangle {
                        x: 8
                        y: 8
                        width: 24
                        height: 16
                        radius: 3
                        color: root.pal.m3primary
                    }
                }
            }

            // ---- lockPassword : value = longueur saisie, value2 = current | new | confirm|good | confirm|bad ----
            LockFace {
                visible: root.kind === "lockPassword"
                dots: root.num
                fieldState: String(root.value2 ?? "").endsWith("bad") ? "bad" : String(root.value2 ?? "").endsWith("good") ? "good" : "idle"
            }

            // ---- lockEnabled : vrai = cadenas fermé, bureau masqué ----
            LockFace {
                visible: root.kind === "lockEnabled"
                locked: root.flag
                field: false
            }

            // ---- lockIdle : vrai = après un temps sans activité, l'écran se verrouille ----
            Item {
                visible: root.kind === "lockIdle"
                anchors.fill: parent

                MiniWin {
                    x: 8
                    y: 8
                    width: 44
                    height: 34
                }
                MaterialIcon {
                    x: 56
                    y: 18
                    text: "arrow_forward"
                    color: root.pal.m3outline
                    font.pointSize: Appearance.font.size.normal
                }
                Rectangle {
                    x: 76
                    y: 8
                    width: 34
                    height: 34
                    radius: Appearance.rounding.small
                    color: root.tone(root.pal.m3scrim, root.flag ? 0.5 : 0.1)
                    border.width: 1
                    border.color: root.flag ? root.pal.m3primary : Qt.alpha(root.pal.m3outline, 0.5)

                    MaterialIcon {
                        anchors.centerIn: parent
                        text: root.flag ? "lock" : "lock_open"
                        color: root.flag ? root.pal.m3primary : root.pal.m3outline
                        font.pointSize: Appearance.font.size.large
                    }
                }
                Ico {
                    x: 8
                    anchors.bottom: parent.bottom
                    anchors.bottomMargin: 4
                    text: "schedule"
                    color: root.flag ? root.pal.m3primary : root.pal.m3outline
                    font.pointSize: Appearance.font.size.normal
                }
            }

            // ---- lockIdleTime : value = délai en secondes ----
            Item {
                visible: root.kind === "lockIdleTime"
                anchors.fill: parent

                StyledText {
                    anchors.horizontalCenter: parent.horizontalCenter
                    y: 10
                    text: root.idleText(root.num)
                    font.pointSize: Appearance.font.size.larger
                    font.weight: Font.Bold
                    color: root.pal.m3primary
                }
                Rectangle {
                    x: 12
                    y: 40
                    width: parent.width - 24
                    height: 6
                    radius: 3
                    color: root.tone(root.pal.m3onSurface, 0.2)

                    Rectangle {
                        width: parent.width * root.clamp(Math.log(Math.max(root.num, 5) / 5) / Math.log(7200 / 5), 0.04, 1)
                        height: parent.height
                        radius: 3
                        color: root.pal.m3primary

                        Behavior on width {
                            enabled: root.animated

                            Anim {
                                duration: Appearance.anim.durations.small
                            }
                        }
                    }
                }
                MaterialIcon {
                    x: 8
                    y: 49
                    text: "lock_open"
                    color: root.pal.m3outline
                    font.pointSize: Appearance.font.size.small
                }
                MaterialIcon {
                    anchors.right: parent.right
                    anchors.rightMargin: 8
                    y: 49
                    text: "lock"
                    color: root.pal.m3primary
                    font.pointSize: Appearance.font.size.small
                }
            }

            // ---- lockShortcut : value = combinaison (ex. Win+L), touches puis écran verrouillé ----
            Item {
                visible: root.kind === "lockShortcut"
                anchors.fill: parent

                Row {
                    x: 8
                    anchors.verticalCenter: parent.verticalCenter
                    spacing: 3

                    Repeater {
                        model: String(root.value ?? "") === "" ? [] : String(root.value).split("+").filter(p => p !== "").slice(0, 3)

                        Rectangle {
                            required property string modelData

                            width: Math.max(14, keyText.implicitWidth + 6)
                            height: 16
                            radius: 4
                            color: root.tone(root.pal.m3onSurface, 0.22)
                            border.width: 1
                            border.color: Qt.alpha(root.pal.m3outline, 0.6)

                            StyledText {
                                id: keyText

                                anchors.centerIn: parent
                                text: parent.modelData
                                font.pointSize: Appearance.font.size.smaller
                            }
                        }
                    }
                }
                MaterialIcon {
                    anchors.right: lockBox.left
                    anchors.rightMargin: 3
                    anchors.verticalCenter: parent.verticalCenter
                    text: "arrow_forward"
                    color: root.pal.m3outline
                    font.pointSize: Appearance.font.size.small
                }
                Rectangle {
                    id: lockBox

                    anchors.right: parent.right
                    anchors.rightMargin: 8
                    anchors.verticalCenter: parent.verticalCenter
                    width: 28
                    height: 28
                    radius: Appearance.rounding.small
                    color: root.tone(root.pal.m3scrim, 0.5)

                    MaterialIcon {
                        anchors.centerIn: parent
                        text: "lock"
                        color: root.pal.m3primary
                        font.pointSize: Appearance.font.size.normal
                    }
                }
            }

            // ---- lockNow : action, l'écran se verrouille aussitôt ----
            LockFace {
                visible: root.kind === "lockNow"
                locked: true
                field: true
            }
        }
    }
}
