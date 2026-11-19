// Carte des éléments récents de l'onglet Espaces.
// Extrait de Workspaces.qml (pur découpage) : `root` désigne l'onglet parent.
import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes
import qs.components
import qs.components.controls
import qs.services
import qs.config

StyledRect {
    id: recentCard
    required property var root
    Layout.fillWidth: true
    Layout.fillHeight: true
    Layout.preferredWidth: 3
    implicitHeight: 188
    radius: Appearance.rounding.large
    color: Colours.palette.m3surfaceContainer
    opacity: re.alpha
    transform: Translate {
        y: re.dy
    }
    WorkspacesEntrance {
        id: re
        host: root
        order: 6
    }
    ColumnLayout {
        anchors.fill: parent
        anchors.margins: Appearance.padding.large
        spacing: Appearance.spacing.small
        RowLayout {
            Layout.fillWidth: true
            MaterialIcon {
                text: "history"
                color: Colours.palette.m3primary
                font.pointSize: Appearance.font.size.large
            }
            StyledText {
                text: qsTr("Récents")
                font.pointSize: Appearance.font.size.normal
                font.weight: 500
                Layout.fillWidth: true
            }
            WorkspacesPill {
                host: root
                order: 7
                implicitHeight: 28
                icon: "arrow_forward"
                text: qsTr("Voir")
                onClicked: Bridge.navigate("workspace:recent")
            }
        }
        Repeater {
            model: root.recentCount
            StyledRect {
                id: rec
                required property int index
                readonly property var item: (Bridge.recent ?? [])[index] ?? ({})
                readonly property var board: root.boards.find(b => b.id === item.board)
                readonly property bool hot: rl.containsMouse || activeFocus
                Layout.fillWidth: true
                implicitHeight: 34
                radius: Appearance.rounding.normal
                color: "transparent"
                opacity: rce.alpha
                transform: Translate {
                    y: rce.dy
                }
                WorkspacesEntrance {
                    id: rce
                    host: root
                    order: 7 + rec.index
                }
                activeFocusOnTab: true
                Keys.onReturnPressed: Bridge.action("launch", {
                    id: item.id
                })
                StateLayer {
                    id: rl
                    function onClicked(): void {
                        Bridge.action("launch", {
                            id: rec.item.id
                        });
                    }
                }
                RowLayout {
                    anchors.fill: parent
                    anchors.leftMargin: Appearance.padding.small
                    anchors.rightMargin: Appearance.padding.small
                    spacing: Appearance.spacing.small
                    transform: Translate {
                        x: rec.hot ? 4 : 0
                        Behavior on x {
                            WorkspacesSpring {}
                        }
                    }
                    WorkspacesAppIcon {
                        Layout.preferredWidth: 28
                        Layout.preferredHeight: 28
                        size: 28
                        appId: rec.item.id ?? ""
                        appName: rec.item.name ?? ""
                    }
                    ColumnLayout {
                        Layout.fillWidth: true
                        spacing: 0
                        StyledText {
                            Layout.fillWidth: true
                            text: rec.item.name ?? ""
                            elide: Text.ElideRight
                            font.pointSize: Appearance.font.size.normal
                        }
                        RowLayout {
                            spacing: 4
                            visible: !!rec.board
                            MaterialIcon {
                                text: rec.board?.icon ?? ""
                                color: root.onTone(rec.board?.id ?? "")
                                font.pointSize: Appearance.font.size.small
                            }
                            StyledText {
                                text: rec.board?.name ?? ""
                                elide: Text.ElideRight
                                font.pointSize: Appearance.font.size.small
                                color: Colours.palette.m3outline
                            }
                        }
                    }
                    MaterialIcon {
                        text: "north_east"
                        color: Colours.palette.m3primary
                        font.pointSize: Appearance.font.size.normal
                        opacity: rec.hot ? 1 : 0
                        Behavior on opacity {
                            Anim {
                                duration: Appearance.anim.durations.small
                            }
                        }
                    }
                }
                Component.onCompleted: if (item.id)
                    Bridge.action("icon", {
                        id: item.id
                    })
                onItemChanged: if (item.id)
                    Bridge.action("icon", {
                        id: item.id
                    })
            }
        }
        RowLayout {
            visible: root.recentCount === 0
            Layout.fillWidth: true
            Layout.fillHeight: true
            spacing: Appearance.spacing.normal
            Item {
                implicitWidth: 48
                implicitHeight: 48
                WorkspacesBlob {
                    anchors.fill: parent
                    lobes: 6
                    amp: 0.1
                    color: Colours.palette.m3surfaceContainerHigh
                }
                MaterialIcon {
                    anchors.centerIn: parent
                    text: "history"
                    color: Colours.palette.m3outline
                }
            }
            StyledText {
                Layout.fillWidth: true
                text: qsTr("Tes derniers accès apparaîtront ici")
                wrapMode: Text.WordWrap
                font.pointSize: Appearance.font.size.small
                color: Colours.palette.m3onSurfaceVariant
            }
        }
        Item {
            Layout.fillHeight: true
            visible: root.recentCount > 0
        }
    }
}
