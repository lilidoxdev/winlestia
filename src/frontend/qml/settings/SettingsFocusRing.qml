import QtQuick
import qs.services
import qs.config

// Contour de focus clavier dessiné sur un élément focalisable (même rendu que le reste du shell).
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
