import QtQuick
import qs.components

// Visuel d'une carte : voile de palette sur le verre natif, liseré 1 px, rendu du Winjet, habillage d'édition.
Item {
    id: card

    property var inst: ({})
    property bool editing: false
    property bool active: true

    signal pressedAt(string handle)
    signal dragged(string handle, real dx, real dy)
    signal released(string handle)

    Rectangle {
        anchors.fill: parent
        radius: WinjetTheme.radius
        color: WinjetTheme.veil
        border.width: 1
        border.color: WinjetTheme.border
    }

    WinjetRenderer {
        id: renderer

        anchors.fill: parent
        inst: card.inst
        editing: card.editing
        active: card.active
    }

    Loader {
        id: editLoader

        anchors.fill: parent
        active: card.editing
        sourceComponent: EditChrome {
            onPressedAt: handle => card.pressedAt(handle)
            onDragged: (handle, dx, dy) => card.dragged(handle, dx, dy)
            onReleased: handle => card.released(handle)
            onRemoveRequested: Bridge.winjets.remove(card.inst.id)
            onSettingsRequested: Bridge.winjets.requestSettings(card.inst.id)
        }
    }
}
