import QtQuick
import QtQuick.Shapes
import qs.components

// Visuel d'une carte : surface m3surface comme les panneaux sur le verre natif, rendu du Winjet, habillage d'édition.
Item {
    id: card

    property var inst: ({})
    property bool editing: false
    property bool active: true
    property bool paused: false

    signal pressedAt(string handle)
    signal dragged(string handle, real dx, real dy, bool snap)
    signal released(string handle)

    // Liquid glass v2 : remplissage fumé, voile de corps, reflet, liseré diagonal 1,2 px + trait intérieur 1 px.
    // Événementiel uniquement (survol, pression) ; « effets réduits » = fond opaque, aucun mouvement.
    readonly property real cardRadius: WinjetTheme.radiusFor(width, height)
    readonly property bool lively: !WinjetTheme.solid && !editing
    readonly property bool hovered: lively && hover.hovered
    readonly property real rimK: hovered ? WinjetTheme.rimBoost : 1
    readonly property real rimW: 1.2

    scale: lively && press.pressed ? 0.985 : 1

    Behavior on scale {
        enabled: card.lively

        NumberAnimation {
            duration: 90
            easing.type: Easing.OutCubic
        }
    }

    HoverHandler {
        id: hover

        enabled: card.lively
    }

    TapHandler {
        id: press

        enabled: card.lively
        acceptedButtons: Qt.LeftButton
    }

    Rectangle {
        anchors.fill: parent
        radius: card.cardRadius
        color: WinjetTheme.glassFillAt(card.inst ? card.inst.opacity : null, card.hovered)

        Behavior on color {
            enabled: card.lively

            ColorAnimation {
                duration: WinjetTheme.motionFast
                easing.type: Easing.OutCubic
            }
        }
    }

    Rectangle {
        anchors.fill: parent
        radius: card.cardRadius
        visible: !WinjetTheme.solid
        gradient: Gradient {
            GradientStop { position: 0; color: WinjetTheme.bodyTop }
            GradientStop { position: 0.45; color: Qt.alpha(WinjetTheme.bodyTop, 0) }
            GradientStop { position: 1; color: WinjetTheme.bodyBottom }
        }
    }

    Rectangle {
        anchors.top: parent.top
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.margins: card.rimW
        height: parent.height * 0.35
        radius: Math.max(0, card.cardRadius - card.rimW)
        visible: !WinjetTheme.solid
        gradient: Gradient {
            GradientStop { position: 0; color: WinjetTheme.highlight }
            GradientStop { position: 1; color: Qt.alpha(WinjetTheme.highlight, 0) }
        }
    }

    // Liseré extérieur : anneau (deux sous-chemins, remplissage pair/impair) rempli d'un dégradé diagonal.
    Shape {
        id: rim

        anchors.fill: parent
        preferredRendererType: Shape.CurveRenderer

        function rr(x: real, y: real, w: real, h: real, r: real): string {
            return "M " + (x + r) + " " + y + " H " + (x + w - r) + " A " + r + " " + r + " 0 0 1 " + (x + w) + " " + (y + r)
                + " V " + (y + h - r) + " A " + r + " " + r + " 0 0 1 " + (x + w - r) + " " + (y + h)
                + " H " + (x + r) + " A " + r + " " + r + " 0 0 1 " + x + " " + (y + h - r)
                + " V " + (y + r) + " A " + r + " " + r + " 0 0 1 " + (x + r) + " " + y + " Z ";
        }

        ShapePath {
            strokeColor: "transparent"
            strokeWidth: 0
            fillRule: ShapePath.OddEvenFill
            fillGradient: LinearGradient {
                x1: 0
                y1: 0
                x2: card.width
                y2: card.height

                GradientStop { position: 0; color: Qt.alpha(WinjetTheme.rimTop, WinjetTheme.rimTop.a * card.rimK) }
                GradientStop { position: 0.5; color: Qt.alpha(WinjetTheme.rimMid, WinjetTheme.rimMid.a * card.rimK) }
                GradientStop { position: 1; color: Qt.alpha(WinjetTheme.rimBottom, WinjetTheme.rimBottom.a * card.rimK) }
            }

            PathSvg {
                path: rim.rr(0, 0, card.width, card.height, card.cardRadius)
                    + rim.rr(card.rimW, card.rimW, card.width - 2 * card.rimW, card.height - 2 * card.rimW,
                        Math.max(0, card.cardRadius - card.rimW))
            }
        }
    }

    // Trait intérieur de 1 px, juste sous le liseré.
    Rectangle {
        anchors.fill: parent
        anchors.margins: card.rimW
        radius: Math.max(0, card.cardRadius - card.rimW)
        color: "transparent"
        border.width: 1
        border.color: WinjetTheme.rimInner
    }

    // Double-clic sur le fond de la carte : entre en édition. Sous le rendu, donc les boutons et pastilles gardent leurs clics
    // (un double-clic sur un contrôle agit sur le contrôle, jamais sur le mode).
    MouseArea {
        anchors.fill: parent
        enabled: !card.editing
        acceptedButtons: Qt.LeftButton
        onDoubleClicked: Bridge.winjets.setEditMode(true)
    }

    WinjetRenderer {
        id: renderer

        anchors.fill: parent
        inst: card.inst
        editing: card.editing
        active: card.active
        paused: card.paused
    }

    Loader {
        id: editLoader

        anchors.fill: parent
        active: card.editing
        sourceComponent: EditChrome {
            onPressedAt: handle => card.pressedAt(handle)
            onDragged: (handle, dx, dy, snap) => card.dragged(handle, dx, dy, snap)
            onReleased: handle => card.released(handle)
            onRemoveRequested: Bridge.winjets.remove(card.inst.id)
            onSettingsRequested: Bridge.winjets.requestSettings(card.inst.id)
        }
    }
}
