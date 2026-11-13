import QtQuick
import QtQuick.Window
import qs.components
import qs.config
import qs.services

// Mode édition, un par écran : grille de cellules sous les cartes (cliquable : un clic dans le vide quitte l'édition), repères
// d'alignement par-dessus (transparents aux clics) et barre « Terminé » en haut au centre. Tout est détruit à la sortie.
QtObject {
    id: overlay

    required property string screenName
    // Faux pendant la sortie : fondu, puis la couche détruit l'overlay.
    property bool shown: true
    readonly property var area: {
        Bridge.winjets.revision;
        return Bridge.winjets.workArea(screenName);
    }
    readonly property var screenGuides: Bridge.winjets.guides.filter(g => g.screen === screenName)
    readonly property var live: Bridge.winjets.gestureRect.screen === screenName ? Bridge.winjets.gestureRect : null
    readonly property int cell: Bridge.winjets.cellSize
    readonly property int gap: Bridge.winjets.cellGap
    readonly property int pitch: cell + gap
    readonly property int margin: Bridge.winjets.gridMargin
    readonly property int cols: Math.max(0, Math.floor(((area.w ?? 0) - 2 * margin + gap) / pitch))
    readonly property int rows: Math.max(0, Math.floor(((area.h ?? 0) - 2 * margin + gap) / pitch))

    // Nombre de cellules couvertes par une longueur en px (4 × 2 = 720 × 352).
    function cells(px: real): int {
        return Math.max(1, Math.round((px + gap) / pitch));
    }

    // Recouvrement suffisant pour éclairer une cellule (un bord aimanté qui frôle la voisine ne l'allume pas).
    function covers(cx: real, cy: real): bool {
        const l = overlay.live;
        if (!l)
            return false;
        return Math.min(cx + cell, l.x + l.w) - Math.max(cx, l.x) > 12 && Math.min(cy + cell, l.y + l.h) - Math.max(cy, l.y) > 12;
    }

    property var gridWindow: Window {
        id: grid

        visible: !!overlay.area && overlay.area.w > 0
        x: overlay.area.x ?? 0
        y: overlay.area.y ?? 0
        width: overlay.area.w ?? 1
        height: overlay.area.h ?? 1
        color: "transparent"
        flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowDoesNotAcceptFocus | Qt.NoDropShadowWindowHint
        onVisibleChanged: if (visible)
            Qt.callLater(() => Bridge.winjets.desktopLayer.registerBackdrop(grid))

        Item {
            anchors.fill: parent
            opacity: overlay.shown ? 1 : 0

            Behavior on opacity {
                NumberAnimation {
                    duration: 140
                    easing.type: Easing.OutCubic
                }
            }

            // Voile léger : il rend la fenêtre cliquable (un pixel transparent laisserait passer le clic) et fait ressortir la grille.
            Rectangle {
                anchors.fill: parent
                color: Qt.alpha(Colours.palette.m3scrim, 0.18)
            }
            MouseArea {
                anchors.fill: parent
                enabled: overlay.shown
                acceptedButtons: Qt.LeftButton
                onClicked: Bridge.winjets.setEditMode(false)
            }

            Repeater {
                model: overlay.cols * overlay.rows

                Rectangle {
                    required property int index
                    readonly property real cx: overlay.margin + (index % overlay.cols) * overlay.pitch
                    readonly property real cy: overlay.margin + Math.floor(index / overlay.cols) * overlay.pitch
                    readonly property bool lit: overlay.covers(cx, cy)

                    x: cx
                    y: cy
                    width: overlay.cell
                    height: overlay.cell
                    radius: 12
                    color: lit ? Qt.alpha(Colours.palette.m3primary, 0.14) : "transparent"
                    border.width: 1
                    border.color: lit ? Qt.alpha(Colours.palette.m3primary, 0.6) : Qt.alpha(Colours.palette.m3outlineVariant, 0.45)
                }
            }

            // Axes du centre de l'écran et petit anneau : repère de symétrie.
            Rectangle {
                x: (parent.width - width) / 2
                width: 1
                height: parent.height
                color: Qt.alpha(Colours.palette.m3primary, 0.45)
            }
            Rectangle {
                y: (parent.height - height) / 2
                width: parent.width
                height: 1
                color: Qt.alpha(Colours.palette.m3primary, 0.45)
            }
            Rectangle {
                anchors.centerIn: parent
                width: 20
                height: 20
                radius: 10
                color: Qt.alpha(Colours.palette.m3surface, 0.6)
                border.width: 2
                border.color: Qt.alpha(Colours.palette.m3primary, 0.8)
            }
        }
    }

    property var guideWindow: Window {
        visible: !!overlay.area && overlay.area.w > 0
        x: overlay.area.x ?? 0
        y: overlay.area.y ?? 0
        width: overlay.area.w ?? 1
        height: overlay.area.h ?? 1
        color: "transparent"
        flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowDoesNotAcceptFocus | Qt.WindowTransparentForInput | Qt.NoDropShadowWindowHint

        Repeater {
            model: overlay.screenGuides

            Rectangle {
                required property var modelData
                readonly property bool vertical: modelData.axis === "x"

                x: vertical ? modelData.pos - 1 : modelData.from
                y: vertical ? modelData.from : modelData.pos - 1
                width: vertical ? 2 : modelData.to - modelData.from
                height: vertical ? modelData.to - modelData.from : 2
                color: Colours.palette.m3primary
            }
        }

        // Taille en cellules pendant un geste.
        Rectangle {
            visible: !!overlay.live
            x: overlay.live ? Math.max(0, Math.min(overlay.live.x, parent.width - width)) : 0
            y: overlay.live ? Math.min(overlay.live.y + overlay.live.h + 8, parent.height - height) : 0
            width: sizeLabel.implicitWidth + 20
            height: 26
            radius: height / 2
            color: Colours.palette.m3primary

            StyledText {
                id: sizeLabel

                anchors.centerIn: parent
                text: overlay.live ? `${overlay.cells(overlay.live.w)} × ${overlay.cells(overlay.live.h)}` : ""
                color: Colours.palette.m3onPrimary
                font.pointSize: Appearance.font.size.small
                font.weight: Font.Medium
            }
        }
    }

    property var barWindow: Window {
        visible: !!overlay.area && overlay.area.w > 0
        width: bar.implicitWidth
        height: bar.implicitHeight
        x: (overlay.area.x ?? 0) + ((overlay.area.w ?? 0) - width) / 2
        y: (overlay.area.y ?? 0) + 16
        color: "transparent"
        // Seule fenêtre qui prend le focus, le temps de l'édition (une seule, sur le premier écran) : Échap quitte.
        flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint
        onVisibleChanged: if (visible && Bridge.winjets.screenNames[0] === overlay.screenName)
            requestActivate()

        Item {
            anchors.fill: parent
            focus: true
            Keys.onEscapePressed: Bridge.winjets.setEditMode(false)
        }

        Rectangle {
            id: bar

            implicitWidth: label.implicitWidth + done.implicitWidth + 48
            implicitHeight: 48
            anchors.fill: parent
            radius: height / 2
            color: Colours.palette.m3surfaceContainerHigh
            border.width: 1
            border.color: WinjetTheme.border

            StyledText {
                id: label

                anchors.left: parent.left
                anchors.leftMargin: 20
                anchors.verticalCenter: parent.verticalCenter
                text: qsTr("Modification des Winjets · double-clic ou Échap pour finir · Alt : sans aimantation")
                color: Colours.palette.m3onSurface
                font.pointSize: Appearance.font.size.normal
            }

            Rectangle {
                id: done

                anchors.right: parent.right
                anchors.rightMargin: 8
                anchors.verticalCenter: parent.verticalCenter
                implicitWidth: doneLabel.implicitWidth + 32
                width: implicitWidth
                height: 32
                radius: 16
                color: Colours.palette.m3primary

                StyledText {
                    id: doneLabel

                    anchors.centerIn: parent
                    text: qsTr("Terminé")
                    color: Colours.palette.m3onPrimary
                    font.pointSize: Appearance.font.size.normal
                    font.weight: Font.Medium
                }
                MouseArea {
                    anchors.fill: parent
                    cursorShape: Qt.PointingHandCursor
                    onClicked: Bridge.winjets.setEditMode(false)
                }
            }
        }
    }
}
