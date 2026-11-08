// Cartes de catégories (formes organiques + aperçu des icônes).
// Extrait de Workspaces.qml (pur découpage) : `root` désigne l'onglet parent.
import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes
import qs.components
import qs.components.controls
import qs.services
import qs.config

RowLayout {
    required property var root
    Layout.fillWidth: true
    spacing: Appearance.spacing.normal
    Repeater {
        model: root.boards
        StyledRect {
            id: space
            required property var modelData
            required property int index
            readonly property var info: root.boardInfo[modelData.id] ?? ({
                    count: 0,
                    top: []
                })
            readonly property bool hot: sl.containsMouse || activeFocus
            property real hk: sl.pressed ? 0.97 : sl.containsMouse ? 1.03 : 1
            Behavior on hk {
                WorkspacesSpring {}
            }
            objectName: "workspace-" + modelData.id
            Layout.fillWidth: true
            Layout.preferredWidth: 1
            implicitHeight: 118
            radius: Appearance.rounding.large
            color: Colours.palette.m3surfaceContainer
            z: hot ? 1 : 0
            opacity: ce.alpha
            scale: ce.k * hk
            transform: Translate {
                y: ce.dy
            }
            WorkspacesEntrance {
                id: ce
                host: root
                order: 1 + space.index
            }
            activeFocusOnTab: true
            Accessible.role: Accessible.Button
            Accessible.name: modelData.name
            Keys.onReturnPressed: Bridge.navigate("workspace:" + modelData.id)
            Keys.onSpacePressed: Bridge.navigate("workspace:" + modelData.id)
            StateLayer {
                id: sl
                function onClicked(): void {
                    Bridge.navigate("workspace:" + space.modelData.id);
                }
            }

            WorkspacesBlob {
                id: shape
                x: Appearance.padding.normal
                y: Appearance.padding.normal
                width: 62
                height: 62
                lobes: space.modelData.lobes
                amp: space.modelData.amp
                color: root.tone(space.modelData.id)
                rotation: space.hot ? 24 : 0
                scale: space.hot ? 1.08 : 1
                Behavior on rotation {
                    WorkspacesSpring {}
                }
                Behavior on scale {
                    WorkspacesSpring {}
                }
            }
            MaterialIcon {
                x: shape.x + (shape.width - width) / 2
                y: shape.y + (shape.height - height) / 2
                text: space.modelData.icon
                fill: space.hot ? 1 : 0
                color: root.onTone(space.modelData.id)
                font.pointSize: Appearance.font.size.extraLarge - 3
                Behavior on fill {
                    Anim {}
                }
            }
            Column {
                anchors.right: parent.right
                anchors.top: parent.top
                anchors.rightMargin: Appearance.padding.large
                anchors.topMargin: Appearance.padding.normal
                StyledText {
                    anchors.right: parent.right
                    text: space.info.count
                    font.pointSize: Appearance.font.size.extraLarge + 6
                    font.weight: 600
                }
                StyledText {
                    anchors.right: parent.right
                    text: qsTr("accès")
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3onSurfaceVariant
                }
            }
            StyledText {
                anchors.left: parent.left
                anchors.bottom: parent.bottom
                anchors.right: stack.left
                anchors.leftMargin: Appearance.padding.normal + 2
                anchors.bottomMargin: Appearance.padding.normal
                text: space.modelData.name
                elide: Text.ElideRight
                font.pointSize: Appearance.font.size.normal
                font.weight: 500
            }
            // Mini-aperçu superposé de jusqu'à 3 vraies icônes ; s'éventaille au survol.
            Item {
                id: stack
                anchors.right: parent.right
                anchors.bottom: parent.bottom
                anchors.rightMargin: Appearance.padding.normal
                anchors.bottomMargin: Appearance.padding.normal
                width: 26 + Math.max(0, space.info.top.length - 1) * (space.hot ? 20 : 15)
                height: 26
                Behavior on width {
                    WorkspacesSpring {}
                }
                Repeater {
                    model: space.info.top.length
                    Rectangle {
                        id: chip
                        required property int index
                        readonly property var app: space.info.top[index]
                        x: (space.info.top.length - 1 - index) * (space.hot ? 20 : 15)
                        width: 26
                        height: 26
                        radius: 13
                        color: Colours.palette.m3surfaceContainerHigh
                        border.width: 2
                        border.color: Colours.palette.m3surfaceContainer
                        z: 3 - index
                        Behavior on x {
                            WorkspacesSpring {}
                        }
                        WorkspacesAppIcon {
                            anchors.centerIn: parent
                            size: 18
                            appId: chip.app?.id ?? ""
                            appName: chip.app?.name ?? ""
                        }
                    }
                }
            }
            Rectangle {
                anchors.fill: parent
                radius: parent.radius
                color: "transparent"
                border.width: 2
                border.color: Colours.palette.m3primary
                visible: space.activeFocus
            }
        }
    }
}
