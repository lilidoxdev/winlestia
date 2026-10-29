import QtQuick
import "../qml"
import qs.services

Rectangle {
    id: root
    color: Colours.palette.m3surface
    property bool open: false
    property alias browser: b
    Item {
        x: 40; y: 18; width: parent.width - 80; height: Math.min(760, parent.height - 36)
        // Mirrors Main.qml's extraBody: clip + opacity driven by the open flag.
        clip: true
        opacity: root.open ? 1 : 0
        WorkspaceBrowser { id: b; width: parent.width; visible: root.open }
    }
}
