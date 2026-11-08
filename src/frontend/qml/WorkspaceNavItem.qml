// Entrée de la colonne de navigation du navigateur d'espace.
import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config

Item {
    id: nav
    property string icon
    property string label
    property int count: -1
    property bool selected
    signal triggered
    implicitHeight: 48
    MouseArea {
        id: navMouse
        anchors.fill: parent
        hoverEnabled: true
        cursorShape: Qt.PointingHandCursor
        onClicked: nav.triggered()
    }
    Rectangle {
        anchors.fill: parent
        radius: Appearance.rounding.full
        color: Colours.palette.m3onSurface
        opacity: navMouse.pressed ? .12 : navMouse.containsMouse && !nav.selected ? .07 : 0
        Behavior on opacity {
            enabled: !Appearance.reducedEffects
            Anim {
                duration: Appearance.anim.durations.small
            }
        }
    }
    RowLayout {
        anchors.fill: parent
        anchors.leftMargin: Appearance.padding.larger
        anchors.rightMargin: Appearance.padding.normal
        spacing: Appearance.spacing.smaller
        MaterialIcon {
            text: nav.icon
            fill: nav.selected ? 1 : 0
            font.pointSize: Appearance.font.size.large
            color: nav.selected ? Colours.palette.m3onSecondaryContainer : Colours.palette.m3onSurfaceVariant
            scale: nav.selected && !Appearance.reducedEffects ? 1.12 : 1
            Behavior on scale {
                WorkspaceSpring {}
            }
        }
        StyledText {
            Layout.fillWidth: true
            text: nav.label
            elide: Text.ElideRight
            font.pointSize: Appearance.font.size.normal
            color: nav.selected ? Colours.palette.m3onSecondaryContainer : Colours.palette.m3onSurface
        }
        Rectangle {
            visible: nav.count >= 0
            implicitWidth: Math.max(24, badge.implicitWidth + 14)
            implicitHeight: 22
            radius: Appearance.rounding.full
            color: nav.selected ? Qt.alpha(Colours.palette.m3onSecondaryContainer, .14) : Colours.palette.m3surfaceContainerHighest
            StyledText {
                id: badge
                anchors.centerIn: parent
                text: nav.count
                font.pointSize: Appearance.font.size.small
                color: nav.selected ? Colours.palette.m3onSecondaryContainer : Colours.palette.m3onSurfaceVariant
            }
        }
    }
}
