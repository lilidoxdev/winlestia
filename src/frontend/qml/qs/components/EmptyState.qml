import qs.services
import qs.config
import qs.components.controls
import QtQuick
import QtQuick.Layouts

ColumnLayout {
    id: root

    property string icon: "search_off"
    property string title
    property string body
    property string actionText
    property string actionIcon

    signal actionTriggered

    spacing: Appearance.spacing.small

    StyledRect {
        Layout.alignment: Qt.AlignHCenter
        implicitWidth: 72
        implicitHeight: 72
        radius: Appearance.rounding.full
        color: Colours.palette.m3surfaceContainerHigh

        MaterialIcon {
            anchors.centerIn: parent
            text: root.icon
            color: Colours.palette.m3onSurfaceVariant
            font.pointSize: Appearance.font.size.extraLarge
        }
    }

    StyledText {
        Layout.alignment: Qt.AlignHCenter
        Layout.topMargin: Appearance.spacing.small
        visible: root.title !== ""
        text: root.title
        font.pointSize: Appearance.font.size.larger
    }

    StyledText {
        Layout.alignment: Qt.AlignHCenter
        Layout.maximumWidth: 360
        visible: root.body !== ""
        text: root.body
        horizontalAlignment: Text.AlignHCenter
        wrapMode: Text.WordWrap
        font.pointSize: Appearance.font.size.small
        color: Colours.palette.m3outline
    }

    IconTextButton {
        Layout.alignment: Qt.AlignHCenter
        Layout.topMargin: Appearance.spacing.small
        visible: root.actionText !== ""
        text: root.actionText
        icon: root.actionIcon
        type: IconTextButton.Tonal
        onClicked: root.actionTriggered()
    }
}
