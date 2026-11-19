// Réglages verre et transparence du studio.
// Extrait de CustomThemeEditor.qml (pur découpage) : `studio` désigne l'élément parent.
import QtQuick
import QtQuick.Window
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.config
import qs.services
import "settings"

SettingsSection {
    required property var studio
    label: ""

    Rectangle {
        Layout.fillWidth: true
        implicitHeight: glassRow.implicitHeight + Appearance.padding.normal * 2
        radius: Appearance.rounding.small
        color: Qt.alpha(Colours.palette.m3onSurface, glassHover.hovered ? 0.06 : 0)

        HoverHandler {
            id: glassHover
        }

        TapHandler {
            onTapped: studio.glass = !studio.glass
        }

        RowLayout {
            id: glassRow

            anchors.left: parent.left
            anchors.right: parent.right
            anchors.verticalCenter: parent.verticalCenter
            anchors.leftMargin: Appearance.padding.larger
            anchors.rightMargin: Appearance.padding.larger
            spacing: Appearance.spacing.normal

            ColumnLayout {
                Layout.fillWidth: true
                spacing: 1

                StyledText {
                    Layout.fillWidth: true
                    text: qsTr("Verre transparent")
                    font.pointSize: Appearance.font.size.normal
                }

                StyledText {
                    Layout.fillWidth: true
                    wrapMode: Text.WordWrap
                    text: qsTr("Le fond laisse voir le bureau")
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3outline
                }
            }

            SettingsSwitch {
                objectName: "customGlassSwitch"
                value: studio.glass
                onToggled: studio.glass = checked
            }
        }
    }

    SettingsSliderRow {
        title: qsTr("Transparence")
        hint: ""
        shown: studio.glass
        from: 5
        to: 98
        value: studio.transparency
        valueText: Math.round(studio.transparency) + " %"
        slider.objectName: "customTransparency"
        onMoved: v => studio.transparency = v
    }
}
