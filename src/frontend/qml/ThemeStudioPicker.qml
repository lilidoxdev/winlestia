// Sélecteur de couleur (teinte/intensité/clarté + hex) du studio.
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

    ColumnLayout {
        Layout.fillWidth: true
        Layout.margins: Appearance.padding.smaller
        spacing: Appearance.spacing.small

        // Hue across, intensity (saturation) down, both at the current clarity.
        Item {
            id: square

            objectName: "customColorPicker"
            Layout.fillWidth: true
            implicitHeight: 96

            Rectangle {
                anchors.fill: parent
                radius: Appearance.rounding.tiny
                gradient: Gradient {
                    orientation: Gradient.Horizontal

                    GradientStop {
                        position: 0
                        color: Qt.hsva(0, 1, studio.brightness, 1)
                    }
                    GradientStop {
                        position: 1 / 6
                        color: Qt.hsva(1 / 6, 1, studio.brightness, 1)
                    }
                    GradientStop {
                        position: 2 / 6
                        color: Qt.hsva(2 / 6, 1, studio.brightness, 1)
                    }
                    GradientStop {
                        position: 3 / 6
                        color: Qt.hsva(3 / 6, 1, studio.brightness, 1)
                    }
                    GradientStop {
                        position: 4 / 6
                        color: Qt.hsva(4 / 6, 1, studio.brightness, 1)
                    }
                    GradientStop {
                        position: 5 / 6
                        color: Qt.hsva(5 / 6, 1, studio.brightness, 1)
                    }
                    GradientStop {
                        position: 1
                        color: Qt.hsva(0, 1, studio.brightness, 1)
                    }
                }
            }

            Rectangle {
                anchors.fill: parent
                radius: Appearance.rounding.tiny
                gradient: Gradient {
                    GradientStop {
                        position: 0
                        color: Qt.hsva(0, 0, studio.brightness, 0)
                    }
                    GradientStop {
                        position: 1
                        color: Qt.hsva(0, 0, studio.brightness, 1)
                    }
                }
            }

            Rectangle {
                x: studio.hue * square.width - width / 2
                y: (1 - studio.saturation) * square.height - height / 2
                width: 16
                height: 16
                radius: 8
                color: "transparent"
                border.width: 2
                border.color: "#ffffff"

                Rectangle {
                    anchors.fill: parent
                    anchors.margins: -1
                    radius: 9
                    color: "transparent"
                    border.width: 1
                    border.color: "#80000000"
                    z: -1
                }
            }

            MouseArea {
                function choose(mouse: var): void {
                    const h = Math.max(0, Math.min(0.999, mouse.x / width));
                    const s = 1 - Math.max(0, Math.min(1, mouse.y / height));
                    studio.wheelColor(h, s, studio.brightness);
                }

                anchors.fill: parent
                onPressed: mouse => choose(mouse)
                onPositionChanged: mouse => {
                    if (pressed)
                        choose(mouse);
                }
            }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.topMargin: Appearance.spacing.smaller

            StyledText {
                text: qsTr("Clarté")
                font.pointSize: Appearance.font.size.normal
            }

            StyledText {
                Layout.fillWidth: true
                text: qsTr("Du sombre au lumineux")
                elide: Text.ElideRight
                font.pointSize: Appearance.font.size.small
                color: Colours.palette.m3outline
            }

            StyledText {
                text: Math.round(studio.brightness * 100) + " %"
                font.pointSize: Appearance.font.size.small
                font.weight: Font.Medium
                color: Colours.palette.m3primary
            }
        }

        // Black to the brightest version of the current colour.
        Item {
            id: clarity

            objectName: "customClarity"
            Layout.fillWidth: true
            implicitHeight: 22

            Rectangle {
                anchors.verticalCenter: parent.verticalCenter
                width: parent.width
                height: 10
                radius: Appearance.rounding.tiny
                gradient: Gradient {
                    orientation: Gradient.Horizontal

                    GradientStop {
                        position: 0
                        color: "#000000"
                    }
                    GradientStop {
                        position: 1
                        color: Qt.hsva(studio.hue, studio.saturation, 1, 1)
                    }
                }
            }

            Rectangle {
                anchors.verticalCenter: parent.verticalCenter
                x: studio.brightness * (clarity.width - width)
                width: 8
                height: 22
                radius: Appearance.rounding.tiny
                color: Colours.palette.m3onSurface
                border.width: 2
                border.color: Colours.surfaceBase
            }

            MouseArea {
                function choose(mouse: var): void {
                    studio.wheelColor(studio.hue, studio.saturation, Math.max(0, Math.min(1, mouse.x / width)));
                }

                anchors.fill: parent
                onPressed: mouse => choose(mouse)
                onPositionChanged: mouse => {
                    if (pressed)
                        choose(mouse);
                }
            }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.topMargin: 0
            spacing: Appearance.spacing.small

            StyledTextField {
                id: hex

                objectName: "customHex"
                Layout.fillWidth: true
                maximumLength: 7
                placeholderText: "#RRGGBB"
                onEditingFinished: {
                    if (Bridge.validHex(text))
                        studio.putColor(text, true);
                    else
                        text = studio.draft[studio.selectedKey];
                }

                Binding {
                    target: hex
                    property: "text"
                    value: studio.draft[studio.selectedKey]
                    when: !hex.activeFocus
                }
            }

            IconTextButton {
                objectName: "customEyedropper"
                Layout.alignment: Qt.AlignVCenter
                icon: "colorize"
                text: qsTr("Pipette")
                type: IconTextButton.Tonal
                onClicked: {
                    studio.visible = false;
                    Bridge.startColorPick();
                }
            }
        }
    }
}
