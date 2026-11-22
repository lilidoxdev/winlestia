import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."
import "../DockItems.js" as DockItems

// Visibilité du dock et des panneaux de bord, barre des tâches, tuilage automatique.
ColumnLayout {
    id: page

    property var host
    readonly property bool tiling: Bridge.settings.autoTile ?? false
    readonly property var exclusions: Bridge.settings.tileExclusions ?? []

    // Listes barre/tiroir courantes ; la clé texte évite de reconstruire les lignes quand d’autres réglages changent.
    readonly property string layoutKey: JSON.stringify(DockItems.normalize(Bridge.settings.dockBar, Bridge.settings.dockDrawer))
    readonly property var layout: JSON.parse(layoutKey)
    readonly property bool peripheralsOn: Bridge.settings.peripheralBattery ?? false
    readonly property var layoutRows: DockItems.rows(layout.bar, layout.drawer)
    readonly property var chipBar: ({
            label: qsTr("Barre"),
            value: "bar"
        })
    readonly property var chipDrawer: ({
            label: qsTr("Tiroir"),
            value: "drawer"
        })
    readonly property var chipHidden: ({
            label: qsTr("Masqué"),
            value: "hidden"
        })

    function apply(lists) {
        Bridge.action("dockLayout", {
            bar: lists.bar,
            drawer: lists.drawer
        });
    }

    spacing: Appearance.spacing.larger

    SettingsSection {
        sectionId: "visibility"
        label: qsTr("Visibilité")

        SettingsRow {
            rowId: "dockVisible"
            preview: Component {
                SystemPreview {
                    kind: "dock"
                    value: dock.checked
                }
            }
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
            preview: Component {
                SystemPreview {
                    kind: "rail"
                    value: bookmarks.checked
                    value2: "left"
                }
            }
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
            preview: Component {
                SystemPreview {
                    kind: "rail"
                    value: social.checked
                    value2: "right"
                }
            }
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
            preview: Component {
                SystemPreview {
                    kind: "dock"
                    value: taskbar.checked
                }
            }
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
            id: iconRow

            rowId: "iconSize"
            preview: Component {
                SystemPreview {
                    kind: "iconSize"
                    value: iconRow.slider.value
                }
            }
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
            preview: Component {
                SystemPreview {
                    kind: "autoTile"
                    value: tile.checked
                }
            }
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
            preview: Component {
                SystemPreview {
                    kind: "tiles"
                    value: "focus"
                }
            }
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
            preview: Component {
                SystemPreview {
                    kind: "tiles"
                    value: "exclude"
                }
            }
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

    SettingsSection {
        sectionId: "dockLayout"
        label: qsTr("Barre d’icônes")

        SettingsRow {
            rowId: "dockLayout"
            preview: Component {
                SystemPreview {
                    kind: "dockLayout"
                    value: page.layout.bar.length
                    value2: page.layout.drawer.length
                }
            }
            below: true

            ColumnLayout {
                Layout.fillWidth: true
                spacing: Appearance.spacing.small

                Repeater {
                    model: page.layoutRows

                    RowLayout {
                        id: layoutRow

                        required property string modelData
                        readonly property bool launcher: DockItems.isLauncher(modelData)
                        readonly property string where: DockItems.placement(page.layout.bar, page.layout.drawer, modelData)

                        Layout.fillWidth: true
                        spacing: Appearance.spacing.normal

                        MaterialIcon {
                            text: DockItems.icon(layoutRow.modelData)
                            font.pointSize: Appearance.font.size.large
                            color: layoutRow.where === "hidden" ? Colours.palette.m3outline : Colours.palette.m3onSurface
                        }

                        StyledText {
                            Layout.fillWidth: true
                            elide: Text.ElideRight
                            text: qsTr(DockItems.label(layoutRow.modelData))
                        }

                        SettingsChips {
                            namePrefix: "dockPlace-" + layoutRow.modelData + "-"
                            model: {
                                if (layoutRow.launcher)
                                    return [page.chipBar, page.chipHidden];
                                if (layoutRow.modelData === "settings")
                                    return [page.chipBar, page.chipDrawer];
                                return [page.chipBar, page.chipDrawer, page.chipHidden];
                            }
                            current: layoutRow.where
                            onChosen: value => page.apply(
                                DockItems.setPlacement(page.layout.bar, page.layout.drawer, layoutRow.modelData, value))
                        }

                        IconButton {
                            objectName: "dockUp-" + layoutRow.modelData
                            icon: "arrow_upward"
                            type: IconButton.Text
                            disabled: !DockItems.canMove(page.layout.bar, page.layout.drawer, layoutRow.modelData, -1)
                            opacity: disabled ? 0.38 : 1
                            Accessible.name: qsTr("Monter")
                            onClicked: page.apply(DockItems.move(page.layout.bar, page.layout.drawer, layoutRow.modelData, -1))
                        }

                        IconButton {
                            objectName: "dockDown-" + layoutRow.modelData
                            icon: "arrow_downward"
                            type: IconButton.Text
                            disabled: !DockItems.canMove(page.layout.bar, page.layout.drawer, layoutRow.modelData, 1)
                            opacity: disabled ? 0.38 : 1
                            Accessible.name: qsTr("Descendre")
                            onClicked: page.apply(DockItems.move(page.layout.bar, page.layout.drawer, layoutRow.modelData, 1))
                        }
                    }
                }
            }
        }

        SettingsRow {
            rowId: "dockLayoutReset"
            preview: Component {
                SystemPreview {
                    kind: "dockLayout"
                    value: DockItems.defaults().bar.length
                    value2: DockItems.defaults().drawer.length
                }
            }

            IconTextButton {
                objectName: "dockLayoutResetButton"
                text: qsTr("Réinitialiser")
                icon: "restart_alt"
                type: IconTextButton.Tonal
                onClicked: page.apply(DockItems.defaults())
            }
        }

        SettingsRow {
            rowId: "peripheralBattery"
            preview: Component {
                SystemPreview {
                    kind: "peripheral"
                    value: peripheralSwitch.checked
                    value2: 2
                }
            }
            target: peripheralSwitch

            SettingsSwitch {
                id: peripheralSwitch

                objectName: "peripheralBatteryToggle"
                value: Bridge.settings.peripheralBattery ?? false
                onToggled: Bridge.action("preference", {
                    key: "peripheralBattery",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "peripheralList"
            preview: Component {
                SystemPreview {
                    kind: "peripheral"
                    value: true
                    value2: peripheralList.shownDevices.filter(d => !(d.hidden ?? false)).length
                }
            }
            below: true
            shown: page.peripheralsOn

            ColumnLayout {
                id: peripheralList

                readonly property bool active: page.visible && page.peripheralsOn
                readonly property var paired: Bridge.peripherals?.paired ? Array.from(Bridge.peripherals.paired) : []
                readonly property var shownDevices: Bridge.peripherals?.devices ? Array.from(Bridge.peripherals.devices) : []

                Layout.fillWidth: true
                spacing: Appearance.spacing.normal

                // Relit les appareils appairés dès que la section devient visible.
                onActiveChanged: {
                    if (active)
                        Bridge.action("peripheralsRefresh", {});
                }
                Component.onCompleted: {
                    if (active)
                        Bridge.action("peripheralsRefresh", {});
                }

                StyledText {
                    objectName: "peripheralEmpty"
                    Layout.fillWidth: true
                    visible: peripheralList.paired.length === 0
                    text: qsTr("Aucun appareil appairé détecté")
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3outline
                }

                Repeater {
                    model: peripheralList.paired

                    ColumnLayout {
                        id: device

                        required property var modelData
                        readonly property var live: peripheralList.shownDevices.find(d => d.id === modelData.id)
                        readonly property bool connected: modelData.connected ?? false

                        objectName: "peripheralDevice-" + modelData.id
                        Layout.fillWidth: true
                        spacing: Appearance.spacing.small

                        RowLayout {
                            Layout.fillWidth: true
                            spacing: Appearance.spacing.normal

                            MaterialIcon {
                                text: DockItems.peripheralIcon(device.modelData.kind)
                                font.pointSize: Appearance.font.size.large
                                color: device.connected ? Colours.palette.m3onSurface : Colours.palette.m3outline
                            }

                            ColumnLayout {
                                Layout.fillWidth: true
                                spacing: 0

                                StyledText {
                                    Layout.fillWidth: true
                                    elide: Text.ElideRight
                                    text: device.modelData.kind === "laptop"
                                        ? qsTr("Batterie de l'ordinateur")
                                        : (device.modelData.name ?? "")
                                }

                                StyledText {
                                    Layout.fillWidth: true
                                    text: device.connected && typeof device.modelData.percent === "number"
                                        ? Math.round(device.modelData.percent) + " %"
                                        : qsTr("non connecté")
                                    font.pointSize: Appearance.font.size.small
                                    color: Colours.palette.m3outline
                                }
                            }

                            StyledText {
                                text: qsTr("Afficher dans le dock")
                                font.pointSize: Appearance.font.size.small
                                color: Colours.palette.m3onSurfaceVariant
                            }

                            SettingsSwitch {
                                objectName: "peripheralVisible-" + device.modelData.id
                                value: !(device.live?.hidden ?? false)
                                Accessible.name: qsTr("Afficher dans le dock")
                                onToggled: Bridge.action("peripheralVisibility", {
                                    id: device.modelData.id,
                                    visible: checked
                                })
                            }
                        }

                        SettingsChips {
                            visible: device.modelData.kind !== "laptop"
                            namePrefix: "peripheralKind-" + device.modelData.id + "-"
                            model: [
                                {
                                    label: qsTr("Auto"),
                                    value: "auto"
                                },
                                {
                                    label: qsTr("Casque"),
                                    value: "headset"
                                },
                                {
                                    label: qsTr("Manette"),
                                    value: "gamepad"
                                },
                                {
                                    label: qsTr("Souris"),
                                    value: "mouse"
                                },
                                {
                                    label: qsTr("Clavier"),
                                    value: "keyboard"
                                },
                                {
                                    label: qsTr("Autre"),
                                    value: "other"
                                }
                            ]
                            current: device.modelData.overridden ? device.modelData.kind : "auto"
                            onChosen: value => Bridge.action("peripheralKind", {
                                id: device.modelData.id,
                                kind: value
                            })
                        }
                    }
                }
            }
        }
    }
}
