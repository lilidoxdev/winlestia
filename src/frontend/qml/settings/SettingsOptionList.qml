import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config
import "."

// Liste d'options en ligne : la sélectionnée affiche une coche. Haut/Bas déplacent, Entrée/Espace valident.
ColumnLayout {
    id: list

    // [{label, value, hint?, flag?, icon?}] ; flag = image url, icon = material symbol
    property var model: []
    property var current
    property string namePrefix

    signal chosen(var value)

    spacing: 2

    Repeater {
        id: rep

        model: list.model

        StyledRect {
            id: opt

            required property var modelData
            required property int index
            readonly property bool selected: list.current === modelData.value

            objectName: list.namePrefix + modelData.value
            Layout.fillWidth: true
            implicitHeight: Math.max(40, content.implicitHeight + Appearance.padding.normal * 2)
            radius: Appearance.rounding.normal
            color: selected ? Qt.alpha(Colours.palette.m3secondaryContainer, 0.9) : "transparent"
            activeFocusOnTab: index === 0 || list.current === modelData.value

            Keys.onPressed: event => {
                if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter || event.key === Qt.Key_Space) {
                    list.chosen(modelData.value);
                    event.accepted = true;
                } else if (event.key === Qt.Key_Down && index < rep.count - 1) {
                    rep.itemAt(index + 1).forceActiveFocus();
                    event.accepted = true;
                } else if (event.key === Qt.Key_Up && index > 0) {
                    rep.itemAt(index - 1).forceActiveFocus();
                    event.accepted = true;
                }
            }

            StateLayer {
                color: opt.selected ? Colours.palette.m3onSecondaryContainer : Colours.palette.m3onSurface

                function onClicked(): void {
                    opt.forceActiveFocus();
                    list.chosen(opt.modelData.value);
                }
            }

            SettingsFocusRing {
                target: opt
            }

            RowLayout {
                id: content

                anchors.left: parent.left
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                anchors.leftMargin: Appearance.padding.large
                anchors.rightMargin: Appearance.padding.large
                spacing: Appearance.spacing.normal

                Image {
                    visible: (opt.modelData.flag ?? "") !== ""
                    Layout.preferredWidth: 21
                    Layout.preferredHeight: 14
                    sourceSize.width: 42
                    source: opt.modelData.flag ?? ""
                    fillMode: Image.PreserveAspectFit
                }

                MaterialIcon {
                    visible: (opt.modelData.icon ?? "") !== ""
                    text: opt.modelData.icon ?? ""
                    color: Colours.palette.m3primary
                }

                ColumnLayout {
                    Layout.fillWidth: true
                    spacing: 0

                    StyledText {
                        Layout.fillWidth: true
                        elide: Text.ElideRight
                        text: opt.modelData.label
                        font.pointSize: Appearance.font.size.normal
                        color: opt.selected ? Colours.palette.m3onSecondaryContainer : Colours.palette.m3onSurface
                    }

                    StyledText {
                        Layout.fillWidth: true
                        visible: text !== ""
                        elide: Text.ElideRight
                        text: opt.modelData.hint ?? ""
                        font.pointSize: Appearance.font.size.small
                        color: Colours.palette.m3outline
                    }
                }

                MaterialIcon {
                    opacity: opt.selected ? 1 : 0
                    text: "check"
                    color: Colours.palette.m3primary

                    Behavior on opacity {
                        enabled: !Appearance.reducedEffects

                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }
                }
            }
        }
    }
}
