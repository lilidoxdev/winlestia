import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config
import ".."
import "."

// One navigation entry: icon in a round tonal pill, title only (the subtitle is shown in the page header). Selected = full-width filled pill.
StyledRect {
    id: item

    property string icon
    property string title
    property string subtitle
    property bool selected: false
    property bool compact: false
    property bool focused: false
    property real iconSize: 40

    signal activated

    implicitHeight: compact ? 52 : 48
    radius: Appearance.rounding.full
    color: selected ? Qt.alpha(Colours.palette.m3secondaryContainer, 1) : "transparent"

    StateLayer {
        color: item.selected ? Colours.palette.m3onSecondaryContainer : Colours.palette.m3onSurface

        function onClicked(): void {
            item.activated();
        }
    }

    SettingsFocusRing {
        target: item
        visible: item.focused
    }

    HoverHandler {
        id: hover
    }

    ShellToolTip {
        visible: item.compact && hover.hovered
        delay: 350
        text: item.title
    }

    StyledRect {
        id: pill

        anchors.left: parent.left
        anchors.leftMargin: item.compact ? (item.width - width) / 2 : Appearance.padding.normal
        anchors.verticalCenter: parent.verticalCenter
        implicitWidth: item.iconSize
        implicitHeight: item.iconSize
        radius: Appearance.rounding.full
        color: item.selected ? Colours.palette.m3primary : Qt.alpha(Colours.palette.m3primary, 0.14)

        MaterialIcon {
            anchors.centerIn: parent
            text: item.icon
            font.pointSize: Appearance.font.size.large
            color: item.selected ? Colours.palette.m3onPrimary : Colours.palette.m3primary
        }
    }

    ColumnLayout {
        anchors.left: pill.right
        anchors.right: parent.right
        anchors.verticalCenter: parent.verticalCenter
        anchors.leftMargin: Appearance.spacing.normal
        anchors.rightMargin: Appearance.padding.large
        visible: !item.compact
        spacing: 0

        StyledText {
            Layout.fillWidth: true
            elide: Text.ElideRight
            text: item.title
            font.pointSize: Appearance.font.size.normal
            font.weight: item.selected ? Font.Medium : Font.Normal
            color: item.selected ? Colours.palette.m3onSecondaryContainer : Colours.palette.m3onSurface
        }
    }
}
