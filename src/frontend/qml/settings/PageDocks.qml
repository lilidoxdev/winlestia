import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

// Visibilité du dock et des panneaux de bord, barre des tâches, tuilage automatique.
ColumnLayout {
    id: page

    property var host
    readonly property bool tiling: Bridge.settings.autoTile ?? false
    readonly property var exclusions: Bridge.settings.tileExclusions ?? []

    spacing: Appearance.spacing.larger

    SettingsSection {
        sectionId: "visibility"
        label: qsTr("Visibilité")

        SettingsRow {
            rowId: "dockVisible"
            target: dock
            hint: qsTr("Masquer le dock réaffiche la barre des tâches de Windows.")

            SettingsSwitch {
                id: dock

                objectName: "dockVisibleToggle"
                value: Bridge.settings.dockVisible ?? true
                onToggled: Bridge.action("preference", {
                    key: "dockVisible",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "bookmarksRail"
            target: bookmarks

            SettingsSwitch {
                id: bookmarks

                objectName: "bookmarksRailToggle"
                value: Bridge.settings.bookmarksRailVisible ?? true
                onToggled: Bridge.action("preference", {
                    key: "bookmarksRailVisible",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "socialRail"
            target: social

            SettingsSwitch {
                id: social

                objectName: "socialRailToggle"
                value: Bridge.settings.socialRailVisible ?? true
                onToggled: Bridge.action("preference", {
                    key: "socialRailVisible",
                    enabled: checked
                })
            }
        }
    }

    SettingsSection {
        sectionId: "taskbar"
        label: qsTr("Barre des tâches")

        SettingsRow {
            rowId: "replaceTaskbar"
            target: taskbar

            SettingsSwitch {
                id: taskbar

                objectName: "replaceTaskbarToggle"
                value: Bridge.taskbarEnabled
                onToggled: Bridge.action("preference", {
                    key: "replaceTaskbar",
                    enabled: checked
                })
            }
        }

        SettingsSliderRow {
            rowId: "iconSize"
            from: 40
            to: 80
            stepSize: 2
            value: Bridge.settings.iconSize ?? 56
            valueText: Math.round(slider.value) + " px"
            onMoved: v => Bridge.action("preference", {
                    key: "iconSize",
                    value: v
                })
        }
    }

    SettingsSection {
        sectionId: "tiling"
        label: qsTr("Disposition automatique")

        SettingsRow {
            rowId: "autoTile"
            target: tile

            SettingsSwitch {
                id: tile

                objectName: "autoTileToggle"
                value: page.tiling
                onToggled: Bridge.action("preference", {
                    key: "autoTile",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "tileShortcuts"
            sub: true
            below: true
            shown: page.tiling

            ColumnLayout {
                Layout.fillWidth: true
                spacing: Appearance.spacing.small

                Repeater {
                    model: [
                        {
                            keys: "Ctrl+Alt+←↑→↓",
                            label: qsTr("Passer à la tuile voisine")
                        },
                        {
                            keys: "Ctrl+Alt+Maj+←↑→↓",
                            label: qsTr("Échanger avec la tuile voisine, y compris sur l’écran d’à côté")
                        },
                        {
                            keys: "Ctrl+Alt+Maj+F",
                            label: qsTr("Sortir la fenêtre du tuilage, ou l’y remettre")
                        },
                        {
                            keys: "Ctrl+Alt+Maj++",
                            label: qsTr("Agrandir ou réduire la tuile active (20 à 80 %)")
                        },
                        {
                            keys: "Ctrl+Alt+Maj+X",
                            label: qsTr("Exclure l’application de la fenêtre active, ou la réintégrer")
                        },
                        {
                            keys: "",
                            label: qsTr("Lâcher une fenêtre sur une autre tuile les échange")
                        }
                    ]

                    RowLayout {
                        required property var modelData

                        Layout.fillWidth: true
                        spacing: Appearance.spacing.normal

                        SettingsKeyCaps {
                            Layout.preferredWidth: 300
                            Layout.alignment: Qt.AlignTop
                            combo: modelData.keys
                            emptyText: qsTr("Souris")
                        }

                        StyledText {
                            Layout.fillWidth: true
                            wrapMode: Text.WordWrap
                            text: modelData.label
                            font.pointSize: Appearance.font.size.small
                            color: Colours.palette.m3outline
                        }
                    }
                }
            }
        }

        SettingsRow {
            rowId: "tileExclusions"
            sub: true
            below: true
            shown: page.tiling
            hint: page.exclusions.length === 0 ? qsTr("Aucune application exclue") : ""

            ColumnLayout {
                Layout.fillWidth: true
                spacing: 0

                Repeater {
                    model: page.exclusions

                    RowLayout {
                        required property var modelData

                        Layout.fillWidth: true
                        spacing: Appearance.spacing.small

                        StyledText {
                            Layout.fillWidth: true
                            elide: Text.ElideRight
                            text: modelData
                            font.pointSize: Appearance.font.size.small
                        }

                        IconButton {
                            icon: "close"
                            type: IconButton.Text
                            onClicked: Bridge.action("preference", {
                                key: "tileExclusionRemove",
                                value: modelData
                            })
                        }
                    }
                }
            }
        }
    }
}
