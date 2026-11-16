import QtQuick
import qs.components
import qs.services
import qs.config

// Jauge : forme arrondie dont le remplissage monte du bas, pourcentage au centre, icône et badge optionnel.
Item {
    id: root

    property real value: 0
    property string icon
    property string badge
    property color fillColour: Colours.palette.m3primary
    readonly property real shapeRadius: 0.34

    implicitWidth: 120
    implicitHeight: implicitWidth

    StyledClippingRect {
        anchors.fill: parent
        radius: root.width * root.shapeRadius
        color: Colours.layer(Colours.palette.m3surfaceContainerHigh, 2)

        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.bottom: parent.bottom
            height: parent.height * Math.max(0, Math.min(1, root.value))
            color: Qt.alpha(root.fillColour, 0.55)

            Behavior on height {
                Anim {
                    duration: Appearance.anim.durations.large
                }
            }
        }

        MaterialIcon {
            anchors.top: parent.top
            anchors.horizontalCenter: parent.horizontalCenter
            anchors.topMargin: Appearance.padding.normal
            text: root.icon
            color: Colours.palette.m3onSurface
            font.pointSize: Appearance.font.size.large
        }

        StyledText {
            anchors.centerIn: parent
            anchors.verticalCenterOffset: Appearance.padding.normal
            text: `${Math.round(root.value * 100)}%`
            color: Colours.palette.m3onSurface
            font.pointSize: Appearance.font.size.larger
            font.weight: Font.DemiBold
        }
    }

    StyledRect {
        visible: root.badge !== ""
        anchors.top: parent.top
        anchors.right: parent.right
        implicitWidth: badgeText.implicitWidth + Appearance.padding.normal * 2
        implicitHeight: badgeText.implicitHeight + Appearance.padding.small
        radius: Appearance.rounding.full
        color: Colours.palette.m3secondaryContainer

        StyledText {
            id: badgeText

            anchors.centerIn: parent
            text: root.badge
            color: Colours.palette.m3onSecondaryContainer
            font.pointSize: Appearance.font.size.small
        }
    }
}
