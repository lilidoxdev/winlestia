import QtQuick
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

    Rectangle {
        anchors.fill: parent
        radius: WinjetTheme.radius
        color: WinjetTheme.fill(card.inst ? card.inst.opacity : null)
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
