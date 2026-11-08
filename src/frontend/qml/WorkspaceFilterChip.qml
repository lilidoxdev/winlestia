// Pastille de filtre de l'en-tête du navigateur d'espace.
import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config

Item {
    id: chip
    property string label
    property int count: -1
    property bool selected
    signal triggered
    implicitHeight: 34
    implicitWidth: chipRow.implicitWidth + Appearance.padding.large * 2
    Behavior on implicitWidth {
        enabled: !Appearance.reducedEffects
        Anim {
            duration: Appearance.anim.durations.small
        }
    }
    Rectangle {
        id: chipBg
        anchors.fill: parent
        // Morphs from a rounded rectangle into a pill when selected.
        radius: chip.selected ? height / 2 : Appearance.rounding.small
        Behavior on radius {
            WorkspaceSpring {}
        }
        color: chip.selected ? Colours.palette.m3secondaryContainer : "transparent"
        border.width: chip.selected ? 0 : 1
        border.color: Colours.palette.m3outlineVariant
    }
    Rectangle {
        anchors.fill: parent
        radius: chipBg.radius
        color: Colours.palette.m3onSurface
        opacity: chipMouse.pressed ? .12 : chipMouse.containsMouse ? .07 : 0
        Behavior on opacity {
            enabled: !Appearance.reducedEffects
            Anim {
                duration: Appearance.anim.durations.small
            }
        }
    }
    MouseArea {
        id: chipMouse
        anchors.fill: parent
        hoverEnabled: true
        cursorShape: Qt.PointingHandCursor
        onClicked: chip.triggered()
    }
    Row {
        id: chipRow
        anchors.centerIn: parent
        spacing: Appearance.spacing.small
        MaterialIcon {
            visible: chip.selected
            anchors.verticalCenter: parent.verticalCenter
            text: "check"
            font.pointSize: Appearance.font.size.normal
            color: Colours.palette.m3onSecondaryContainer
        }
        StyledText {
            anchors.verticalCenter: parent.verticalCenter
            text: chip.label
            font.pointSize: Appearance.font.size.small
            color: chip.selected ? Colours.palette.m3onSecondaryContainer : Colours.palette.m3onSurface
        }
        StyledText {
            visible: chip.count >= 0
            anchors.verticalCenter: parent.verticalCenter
            text: chip.count
            font.pointSize: Appearance.font.size.small
            opacity: .6
            color: chip.selected ? Colours.palette.m3onSecondaryContainer : Colours.palette.m3outline
        }
    }
}
