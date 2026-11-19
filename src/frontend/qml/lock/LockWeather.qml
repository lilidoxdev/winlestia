import QtQuick
import qs.components
import qs.services
import qs.config

// Météo centrée : description, grande température et icône. Pas de ressenti ni de min/max : le backend ne les fournit pas.
LockCard {
    id: root

    readonly property real bigSize: Appearance.font.size.extraLarge * 2.2

    Column {
        width: parent.width
        spacing: Appearance.spacing.small

        StyledText {
            anchors.horizontalCenter: parent.horizontalCenter
            width: parent.width
            horizontalAlignment: Text.AlignHCenter
            elide: Text.ElideRight
            text: Weather.description
            color: Colours.palette.m3onSurfaceVariant
            font.pointSize: Appearance.font.size.larger
        }

        Row {
            anchors.horizontalCenter: parent.horizontalCenter
            spacing: Appearance.spacing.normal

            StyledText {
                anchors.verticalCenter: parent.verticalCenter
                text: Weather.temp
                color: Colours.palette.m3primary
                font.pointSize: root.bigSize
                font.weight: Font.Bold
            }

            MaterialIcon {
                anchors.verticalCenter: parent.verticalCenter
                text: Weather.icon
                fill: 1
                color: Colours.palette.m3secondary
                font.pointSize: root.bigSize
            }
        }
    }
}
