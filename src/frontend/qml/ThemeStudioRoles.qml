// Liste des rôles de couleur du studio de thème.
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
    label: qsTr("Couleurs")

    Repeater {
        model: studio.roles

        Rectangle {
            id: role

            required property var modelData
            required property int index
            readonly property bool current: studio.selected === index

            objectName: "custom-role-" + modelData.key
            Layout.fillWidth: true
            implicitHeight: 28
            radius: Appearance.rounding.smaller
            color: current ? Qt.alpha(Colours.palette.m3primary, 0.14) : Qt.alpha(Colours.palette.m3onSurface,
                hover.hovered ? 0.06 : 0)

            Behavior on color {
                enabled: !Appearance.reducedEffects
                ColorAnimation {
                    duration: Appearance.anim.durations.small
                }
            }

            HoverHandler {
                id: hover
            }

            TapHandler {
                onTapped: studio.selected = role.index
            }

            Rectangle {
                visible: role.current
                anchors.verticalCenter: parent.verticalCenter
                x: 0
                width: 3
                height: 18
                radius: 1
                color: Colours.palette.m3primary
            }

            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: Appearance.padding.normal
                anchors.rightMargin: Appearance.padding.normal
                spacing: Appearance.spacing.normal

                Rectangle {
                    objectName: "custom-swatch-" + role.modelData.key
                    Layout.alignment: Qt.AlignVCenter
                    implicitWidth: 22
                    implicitHeight: 22
                    radius: Appearance.rounding.tiny
                    color: studio.draft[role.modelData.key]
                    border.width: 1
                    border.color: Colours.palette.m3outline
                }

                StyledText {
                    Layout.fillWidth: true
                    Layout.alignment: Qt.AlignVCenter
                    text: role.modelData.label
                    elide: Text.ElideRight
                    font.pointSize: Appearance.font.size.normal
                    font.weight: role.current ? Font.Medium : Font.Normal
                }

                StyledText {
                    Layout.alignment: Qt.AlignVCenter
                    text: String(studio.draft[role.modelData.key]).toUpperCase()
                    font.pointSize: Appearance.font.size.small
                    color: role.current ? Colours.palette.m3primary : Colours.palette.m3outline
                }
            }
        }
    }
}
