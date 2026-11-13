import QtQuick
import qs.components
import qs.config
import qs.services

// Habillage du mode édition : contour, déplacement par le corps, 8 poignées de redimensionnement, boutons ⚙ et supprimer.
// Ne bouge rien lui-même : il émet des deltas en coordonnées écran (la fenêtre se déplace pendant le geste).
Item {
    id: chrome

    // Poignée active : "move", "n", "se", etc. ; vide hors geste.
    property string gesture: ""
    property bool allowSettings: true

    signal pressedAt(string handle)
    signal dragged(string handle, real dx, real dy, bool snap)
    signal released(string handle)
    signal removeRequested
    signal settingsRequested

    readonly property real grip: 10
    property point origin

    // Le curseur global vient du pont : la fenêtre se déplace sous la souris, mapToGlobal oscillerait d'une image à l'autre.
    function press(handle: string): void {
        const p = Bridge.winjets.cursorPos();
        origin = Qt.point(p.x, p.y);
        gesture = handle;
        pressedAt(handle);
    }

    function move(handle: string, modifiers: int): void {
        const p = Bridge.winjets.cursorPos();
        // Alt maintenu : pas d'aimantation.
        dragged(handle, p.x - origin.x, p.y - origin.y, (modifiers & Qt.AltModifier) === 0);
    }

    function release(handle: string): void {
        gesture = "";
        released(handle);
    }

    // Contour d'accent discret.
    Rectangle {
        anchors.fill: parent
        radius: WinjetTheme.radius
        color: "transparent"
        border.width: 2
        border.color: Qt.alpha(WinjetTheme.accent, 0.7)
    }

    // Corps : déplacement ; un double-clic quitte l'édition. Il recouvre le contenu, qui ne reçoit donc aucun clic.
    MouseArea {
        id: body

        anchors.fill: parent
        anchors.margins: chrome.grip
        cursorShape: pressed ? Qt.ClosedHandCursor : Qt.OpenHandCursor

        onPressed: chrome.press("move")
        onPositionChanged: mouse => {
            if (pressed)
                chrome.move("move", mouse.modifiers);
        }
        onReleased: chrome.release("move")
        onDoubleClicked: Bridge.winjets.setEditMode(false)
    }

    // Bords et coins : zones de saisie avec le curseur de redimensionnement ; seules les poignées visibles sont dessinées.
    Repeater {
        model: [
            {
                h: "n",
                cursor: Qt.SizeVerCursor
            },
            {
                h: "s",
                cursor: Qt.SizeVerCursor
            },
            {
                h: "w",
                cursor: Qt.SizeHorCursor
            },
            {
                h: "e",
                cursor: Qt.SizeHorCursor
            }
        ]

        MouseArea {
            id: edge

            required property var modelData
            readonly property bool horizontal: modelData.h === "n" || modelData.h === "s"

            x: modelData.h === "e" ? chrome.width - chrome.grip : 0
            y: modelData.h === "s" ? chrome.height - chrome.grip : 0
            width: horizontal ? chrome.width : chrome.grip
            height: horizontal ? chrome.grip : chrome.height
            cursorShape: modelData.cursor

            onPressed: chrome.press(modelData.h)
            onPositionChanged: mouse => {
                if (pressed)
                    chrome.move(modelData.h, mouse.modifiers);
            }
            onReleased: chrome.release(modelData.h)
        }
    }

    Repeater {
        model: [
            {
                h: "nw",
                ax: 0,
                ay: 0,
                cursor: Qt.SizeFDiagCursor
            },
            {
                h: "n",
                ax: 0.5,
                ay: 0,
                cursor: Qt.SizeVerCursor
            },
            {
                h: "ne",
                ax: 1,
                ay: 0,
                cursor: Qt.SizeBDiagCursor
            },
            {
                h: "e",
                ax: 1,
                ay: 0.5,
                cursor: Qt.SizeHorCursor
            },
            {
                h: "se",
                ax: 1,
                ay: 1,
                cursor: Qt.SizeFDiagCursor
            },
            {
                h: "s",
                ax: 0.5,
                ay: 1,
                cursor: Qt.SizeVerCursor
            },
            {
                h: "sw",
                ax: 0,
                ay: 1,
                cursor: Qt.SizeBDiagCursor
            },
            {
                h: "w",
                ax: 0,
                ay: 0.5,
                cursor: Qt.SizeHorCursor
            }
        ]

        MouseArea {
            id: handle

            required property var modelData

            x: modelData.ax * chrome.width - width / 2
            y: modelData.ay * chrome.height - height / 2
            width: 24
            height: 24
            cursorShape: modelData.cursor
            hoverEnabled: true

            Rectangle {
                anchors.centerIn: parent
                width: handle.containsMouse || handle.pressed ? 12 : 9
                height: width
                radius: width / 2
                color: Colours.palette.m3primary
                border.width: 2
                border.color: Colours.palette.m3onPrimary
            }

            onPressed: chrome.press(modelData.h)
            onPositionChanged: mouse => {
                if (pressed)
                    chrome.move(modelData.h, mouse.modifiers);
            }
            onReleased: chrome.release(modelData.h)
        }
    }

    // Boutons en haut à droite, à l'intérieur du contour.
    Row {
        anchors.top: parent.top
        anchors.right: parent.right
        anchors.margins: 14
        spacing: 6

        Repeater {
            model: chrome.allowSettings ? ["settings", "close"] : ["close"]

            Rectangle {
                id: button

                required property string modelData

                width: 28
                height: 28
                radius: 14
                color: modelData === "close" ? Colours.palette.m3errorContainer : Colours.palette.m3secondaryContainer

                MaterialIcon {
                    anchors.centerIn: parent
                    text: button.modelData
                    font.pointSize: Appearance.font.size.normal
                    color: button.modelData === "close" ? Colours.palette.m3onErrorContainer : Colours.palette.m3onSecondaryContainer
                }
                MouseArea {
                    anchors.fill: parent
                    cursorShape: Qt.PointingHandCursor
                    onClicked: button.modelData === "close" ? chrome.removeRequested() : chrome.settingsRequested()
                }
            }
        }
    }
}
