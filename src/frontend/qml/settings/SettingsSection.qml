import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config
import "."

// Petit libellé discret, puis une carte arrondie contenant les lignes de la section.
ColumnLayout {
    id: section

    property string sectionId
    property string label
    property bool shown: true
    default property alias rows: body.data

    Layout.fillWidth: true
    visible: shown
    spacing: Appearance.spacing.small

    StyledText {
        Layout.fillWidth: true
        visible: section.label !== ""
        Layout.leftMargin: Appearance.padding.larger
        text: section.label
        font.pointSize: Appearance.font.size.small
        font.weight: Font.Medium
        color: Colours.palette.m3outline
    }

    StyledRect {
        Layout.fillWidth: true
        implicitHeight: body.implicitHeight + Appearance.padding.small * 2
        radius: Appearance.rounding.large
        color: Qt.alpha(Colours.palette.m3surfaceContainer, 0.62)
        border.width: 1
        border.color: Qt.alpha(Colours.palette.m3outlineVariant, 0.45)

        ColumnLayout {
            id: body

            anchors.left: parent.left
            anchors.right: parent.right
            anchors.top: parent.top
            anchors.margins: Appearance.padding.small
            spacing: 0
        }
    }
}
