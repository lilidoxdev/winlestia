import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config
import "."

// Row of preset chips; the selected one is filled with the accent colour. Left/Right move the choice, Enter/Space pick.
Flow {
    id: chips

    // [{label, value, objectName?}]
    property var model: []
    property var current
    property string namePrefix

    signal chosen(var value)

    spacing: Appearance.spacing.small

    Repeater {
        model: chips.model

        StyledRect {
            id: chip

            required property var modelData
            required property int index
            readonly property bool selected: chips.current === modelData.value

            objectName: chips.namePrefix + modelData.value
            implicitWidth: label.implicitWidth + Appearance.padding.large * 2 + (selected ? checkIcon.width + Appearance.spacing.small : 0)
            implicitHeight: 34
            radius: Appearance.rounding.full
            color: selected ? Colours.palette.m3primary : Qt.alpha(Colours.palette.m3secondaryContainer, 1)
            activeFocusOnTab: true

            Behavior on implicitWidth {
                enabled: !Appearance.reducedEffects

                Anim {
                    duration: Appearance.anim.durations.small
                }
            }

            Keys.onPressed: event => {
                if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter || event.key === Qt.Key_Space) {
                    chips.chosen(modelData.value);
                    event.accepted = true;
                } else if (event.key === Qt.Key_Right && index < chips.model.length - 1) {
                    chips.chosen(chips.model[index + 1].value);
                    event.accepted = true;
                } else if (event.key === Qt.Key_Left && index > 0) {
                    chips.chosen(chips.model[index - 1].value);
                    event.accepted = true;
                }
            }

            StateLayer {
                color: chip.selected ? Colours.palette.m3onPrimary : Colours.palette.m3onSecondaryContainer

                function onClicked(): void {
                    chip.forceActiveFocus();
                    chips.chosen(chip.modelData.value);
                }
            }

            SettingsFocusRing {
                target: chip
            }

            Row {
                anchors.centerIn: parent
                spacing: Appearance.spacing.small

                MaterialIcon {
                    id: checkIcon

                    visible: chip.selected
                    anchors.verticalCenter: parent.verticalCenter
                    text: "check"
                    font.pointSize: Appearance.font.size.normal
                    color: Colours.palette.m3onPrimary
                }

                StyledText {
                    id: label

                    anchors.verticalCenter: parent.verticalCenter
                    text: chip.modelData.label
                    font.pointSize: Appearance.font.size.small
                    color: chip.selected ? Colours.palette.m3onPrimary : Colours.palette.m3onSecondaryContainer
                }
            }
        }
    }
}
