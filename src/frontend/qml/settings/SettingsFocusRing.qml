import QtQuick
import qs.services
import qs.config

// Keyboard focus outline drawn over a focusable item (same look as the rest of the shell).
Rectangle {
    id: root

    property Item target
    property real grow: 2

    anchors.fill: target
    anchors.margins: -grow
    radius: (target?.radius ?? Appearance.rounding.small) + grow
    color: "transparent"
    border.width: 2
    border.color: Colours.palette.m3primary
    visible: target?.activeFocus ?? false
}
