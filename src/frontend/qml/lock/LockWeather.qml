import QtQuick
import qs.components
import qs.services
import qs.config

// Météo : description (ville), grande température et icône. Pas de ressenti ni de min/max : le backend ne les fournit pas.
LockCard {
    id: root

    readonly property real bigSize: Appearance.font.size.extraLarge * 2.2

    StyledText {
        id: desc

        width: parent.width
        elide: Text.ElideRight
        text: Weather.description
        color: Colours.palette.m3onSurfaceVariant
        font.pointSize: Appearance.font.size.larger
    }

    StyledText {
        anchors.top: desc.bottom
        anchors.topMargin: Appearance.spacing.small
        text: Weather.temp
        color: Colours.palette.m3primary
        font.pointSize: root.bigSize
        font.weight: Font.Bold
    }

    MaterialIcon {
        anchors.right: parent.right
        anchors.top: desc.bottom
        anchors.topMargin: Appearance.spacing.small
        text: Weather.icon
        fill: 1
        color: Colours.palette.m3secondary
        font.pointSize: root.bigSize
    }
}
