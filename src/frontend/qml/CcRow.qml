// Ligne de liste groupée (grammaire Caelestia) : coins extérieurs 16 sur la première/dernière ligne, 4 entre les lignes.
import QtQuick
import qs.components
import qs.services
import qs.config

StyledRect {
    id: root
    property bool first: false
    property bool last: false
    property bool selected: false
    property bool interactive: true
    property real headerHeight: height
    signal clicked

    color: root.selected ? Colours.palette.m3secondaryContainer : Colours.palette.m3surfaceContainer
    radius: 4
    topLeftRadius: first ? 16 : 4
    topRightRadius: first ? 16 : 4
    bottomLeftRadius: last ? 16 : 4
    bottomRightRadius: last ? 16 : 4
    StateLayer {
        anchors.fill: undefined
        width: root.width
        height: root.headerHeight
        disabled: !root.interactive
        color: Colours.palette.m3onSurface
        onClicked: root.clicked()
    }
}
