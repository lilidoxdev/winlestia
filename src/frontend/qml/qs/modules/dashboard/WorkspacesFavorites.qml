// Carte des favoris de l'onglet Espaces.
// Extrait de Workspaces.qml (pur découpage) : `root` désigne l'onglet parent.
import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes
import qs.components
import qs.components.controls
import qs.services
import qs.config

StyledRect {
    id: favCard
    required property var root
    Layout.fillWidth: true
    Layout.fillHeight: true
    Layout.preferredWidth: 5
    implicitHeight: 188
    radius: Appearance.rounding.large
    color: Colours.palette.m3surfaceContainer
    opacity: fe.alpha
    transform: Translate {
        y: fe.dy
    }
    WorkspacesEntrance {
        id: fe
        host: root
        order: 5
    }
    ColumnLayout {
        anchors.fill: parent
        anchors.margins: Appearance.padding.large
        spacing: Appearance.spacing.small
        RowLayout {
            Layout.fillWidth: true
            MaterialIcon {
                text: "star"
                fill: 1
                color: Colours.palette.m3primary
                font.pointSize: Appearance.font.size.large
            }
            StyledText {
                text: qsTr("Tes favoris")
                font.pointSize: Appearance.font.size.normal
                font.weight: 500
                Layout.fillWidth: true
            }
            StyledText {
                visible: root.favCount === 0
                text: qsTr("Ajoute des favoris depuis le clic droit d’un accès")
                font.pointSize: Appearance.font.size.small
                color: Colours.palette.m3onSurfaceVariant
            }
        }
        RowLayout {
            id: favFlow
            Layout.fillWidth: true
            Layout.fillHeight: true
            spacing: Appearance.spacing.small
            Repeater {
                // Modèle basé sur un compteur : les délégués se relient à Bridge.favorites[index] au lieu d'être reconstruits.
                model: root.favCount
                StyledRect {
                    id: tile
                    required property int index
                    readonly property var fav: (Bridge.favorites ?? [])[index] ?? ({})
                    readonly property bool hot: tl.containsMouse || activeFocus
                    property real hk: tl.pressed ? 0.96 : tl.containsMouse ? 1.05 : 1
                    Behavior on hk {
                        WorkspacesSpring {}
                    }
                    Layout.fillWidth: true
                    Layout.fillHeight: true
                    Layout.preferredWidth: 1
                    Layout.maximumWidth: 120
                    radius: Appearance.rounding.normal
                    color: Colours.palette.m3surfaceContainerHigh
                    z: hot ? 1 : 0
                    opacity: te.alpha
                    scale: te.k * hk
                    transform: [
                        Translate {
                            y: te.dy
                        },
                        Translate {
                            y: tile.hot ? -4 : 0
                            Behavior on y {
                                WorkspacesSpring {}
                            }
                        }
                    ]
                    WorkspacesEntrance {
                        id: te
                        host: root
                        order: 6 + tile.index
                    }
                    activeFocusOnTab: true
                    Accessible.role: Accessible.Button
                    Accessible.name: fav.name ?? ""
                    Keys.onReturnPressed: Bridge.action("launch", {
                        id: fav.id
                    })
                    Keys.onSpacePressed: Bridge.action("launch", {
                        id: fav.id
                    })
                    StateLayer {
                        id: tl
                        function onClicked(): void {
                            Bridge.action("launch", {
                                id: tile.fav.id
                            });
                        }
                    }
                    Column {
                        anchors.centerIn: parent
                        width: parent.width - 12
                        spacing: Appearance.spacing.small
                        WorkspacesAppIcon {
                            anchors.horizontalCenter: parent.horizontalCenter
                            size: 48
                            appId: tile.fav.id ?? ""
                            appName: tile.fav.name ?? ""
                        }
                        StyledText {
                            width: parent.width
                            text: tile.fav.name ?? ""
                            horizontalAlignment: Text.AlignHCenter
                            font.pointSize: Appearance.font.size.small
                            elide: Text.ElideRight
                        }
                    }
                    Rectangle {
                        anchors.fill: parent
                        radius: parent.radius
                        color: "transparent"
                        border.width: 2
                        border.color: Colours.palette.m3primary
                        visible: tile.activeFocus
                    }
                    Component.onCompleted: if (fav.id)
                        Bridge.action("icon", {
                            id: fav.id
                        })
                    onFavChanged: if (fav.id)
                        Bridge.action("icon", {
                            id: fav.id
                        })
                }
            }
            // Tuile d'ajout « + », stylée comme les favoris
            StyledRect {
                id: addTile
                readonly property bool hot: al.containsMouse || activeFocus
                property real hk: al.pressed ? 0.96 : al.containsMouse ? 1.05 : 1
                Behavior on hk {
                    WorkspacesSpring {}
                }
                visible: root.favCount < 6
                Layout.fillWidth: true
                Layout.fillHeight: true
                Layout.preferredWidth: 1
                Layout.maximumWidth: 120
                radius: Appearance.rounding.normal
                color: "transparent"
                border.width: 2
                border.color: addTile.hot ? Colours.palette.m3primary : Colours.palette.m3outlineVariant
                Behavior on border.color {
                    CAnim {}
                }
                z: hot ? 1 : 0
                opacity: ae.alpha
                scale: ae.k * hk
                transform: Translate {
                    y: ae.dy
                }
                WorkspacesEntrance {
                    id: ae
                    host: root
                    order: 6 + root.favCount
                }
                activeFocusOnTab: true
                Accessible.role: Accessible.Button
                Accessible.name: qsTr("Ajouter")
                Keys.onReturnPressed: Bridge.navigate("workspace:apps")
                Keys.onSpacePressed: Bridge.navigate("workspace:apps")
                StateLayer {
                    id: al
                    function onClicked(): void {
                        Bridge.navigate("workspace:apps");
                    }
                }
                Column {
                    anchors.centerIn: parent
                    spacing: Appearance.spacing.small
                    Item {
                        anchors.horizontalCenter: parent.horizontalCenter
                        width: 48
                        height: 48
                        WorkspacesBlob {
                            anchors.fill: parent
                            lobes: 8
                            amp: 0.07
                            color: Colours.palette.m3primaryContainer
                            rotation: addTile.hot ? 45 : 0
                            Behavior on rotation {
                                WorkspacesSpring {}
                            }
                        }
                        MaterialIcon {
                            anchors.centerIn: parent
                            text: "add"
                            color: Colours.palette.m3onPrimaryContainer
                            font.pointSize: Appearance.font.size.large
                        }
                    }
                    StyledText {
                        anchors.horizontalCenter: parent.horizontalCenter
                        text: qsTr("Ajouter")
                        font.pointSize: Appearance.font.size.small
                        color: Colours.palette.m3onSurfaceVariant
                    }
                }
            }
            Item {
                Layout.fillWidth: true
                Layout.preferredWidth: 0.01
            }
        }
    }
}
