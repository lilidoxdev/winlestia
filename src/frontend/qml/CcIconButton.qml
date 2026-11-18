// Bouton rond tonal 36 px avec info-bulle.
import QtQuick
import qs.components.controls
import qs.services

IconButton {
    id: root
    property string tip: ""
    type: IconButton.Tonal
    implicitHeight: 36
    inactiveColour: Colours.palette.m3surfaceContainerHigh
    inactiveOnColour: Colours.palette.m3onSurfaceVariant
    HoverHandler {
        id: hover
    }
    ShellToolTip {
        visible: hover.hovered && root.tip !== ""
        delay: 400
        text: root.tip
    }
}
