import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

// Theme, custom palette, transparency, animations.
ColumnLayout {
    id: page

    // Défini par l'hôte : { openCustomEditor() }
    property var host
    readonly property string mode: Bridge.themeMode
    readonly property real motion: Bridge.settings.motionScale ?? 1
    readonly property bool glassOk: mode === "auto" || (mode === "custom" && (Bridge.settings.customGlass ?? false))

    spacing: Appearance.spacing.larger

    SettingsSection {
        sectionId: "theme"
        label: qsTr("Thème")

        SettingsRow {
            rowId: "theme"
            below: true

            ColumnLayout {
                Layout.fillWidth: true
                spacing: Appearance.spacing.normal

                RowLayout {
                    Layout.fillWidth: true
                    spacing: Appearance.spacing.small

                    Repeater {
                        // Preview swatches keep fixed colours on purpose: Porcelaine and Nuit never follow the wallpaper.
                        model: [
                            {
                                mode: "light",
                                dark: false,
                                label: qsTr("Porcelaine"),
                                bg: "#fff8f6",
                                fg: "#895143"
                            },
                            {
                                mode: "dark",
                                dark: true,
                                label: qsTr("Nuit"),
                                bg: "#101518",
                                fg: "#afd4f0"
                            },
                            {
                                mode: "custom",
                                label: qsTr("Personnalisé"),
                                bg: Colours.surfaceBase,
                                fg: Colours.palette.m3primary
                            },
                            {
                                mode: "auto",
                                label: qsTr("Automatique"),
                                bg: Colours.palette.m3surfaceContainerHigh,
                                fg: Colours.palette.m3primary
                            }
                        ]

                        StyledRect {
                            id: theme

                            required property var modelData
                            readonly property bool current: Bridge.themeMode === modelData.mode

                            objectName: "theme-" + modelData.mode
                            Layout.fillWidth: true
                            Layout.preferredWidth: 1
                            implicitHeight: 92
                            radius: Appearance.rounding.normal
                            color: modelData.bg
                            border.width: current ? 2 : 0
                            border.color: Colours.palette.m3primary
                            activeFocusOnTab: true

                            // L'action recolore le modèle ci-dessus, ce qui détruit ce délégué : tout lire avant de l'appeler.
                            // Choosing "custom" only switches mode; the editor opens from the "Modifier ma palette" row below.
                            function choose(): void {
                                const m = modelData.mode, dark = modelData.dark ?? false;
                                Bridge.action("theme", {
                                    mode: m,
                                    dark: dark
                                });
                            }

                            Keys.onPressed: event => {
                                if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter || event.key === Qt.Key_Space) {
                                    choose();
                                    event.accepted = true;
                                }
                            }

                            StateLayer {
                                color: theme.modelData.fg

                                function onClicked(): void {
                                    theme.forceActiveFocus();
                                    theme.choose();
                                }
                            }

                            SettingsFocusRing {
                                target: theme
                            }

                            Rectangle {
                                anchors.horizontalCenter: parent.horizontalCenter
                                y: 12
                                width: 52
                                height: 32
                                radius: Appearance.rounding.smaller
                                color: Qt.alpha(theme.modelData.fg, 0.14)

                                Rectangle {
                                    x: 6
                                    y: 5
                                    width: parent.width - 12
                                    height: 4
                                    radius: Appearance.rounding.tiny / 2
                                    color: theme.modelData.fg
                                }
                                Rectangle {
                                    x: 6
                                    y: 13
                                    width: 17
                                    height: 13
                                    radius: Appearance.rounding.tiny
                                    color: Qt.alpha(theme.modelData.fg, 0.55)
                                }
                                Rectangle {
                                    x: 27
                                    y: 13
                                    width: 19
                                    height: 13
                                    radius: Appearance.rounding.tiny
                                    color: Qt.alpha(theme.modelData.fg, 0.25)
                                }
                            }

                            StyledText {
                                anchors.horizontalCenter: parent.horizontalCenter
                                y: 56
                                width: parent.width - 2
                                horizontalAlignment: Text.AlignHCenter
                                elide: Text.ElideRight
                                text: theme.modelData.label
                                color: theme.modelData.fg
                                font.pointSize: Appearance.font.size.smaller
                            }

                            MaterialIcon {
                                visible: theme.current
                                anchors.right: parent.right
                                anchors.top: parent.top
                                anchors.margins: 5
                                text: "check_circle"
                                color: theme.modelData.fg
                                font.pointSize: Appearance.font.size.normal
                            }
                        }
                    }
                }

                RowLayout {
                    visible: Bridge.themeMode === "auto"
                    Layout.fillWidth: true
                    spacing: Appearance.spacing.small

                    Repeater {
                        model: Bridge.wallpaperInfo.colors ?? []

                        Rectangle {
                            required property string modelData

                            width: 16
                            height: 16
                            radius: Appearance.rounding.smaller
                            color: modelData
                        }
                    }

                    StyledText {
                        Layout.fillWidth: true
                        text: qsTr("Couleurs tirées de ton fond d’écran")
                        font.pointSize: Appearance.font.size.small
                        color: Colours.palette.m3outline
                    }
                }

                StyledText {
                    Layout.fillWidth: true
                    wrapMode: Text.WordWrap
                    text: Bridge.themeMode === "light"
                        ? qsTr("Porcelaine : thème clair et chaleureux, fixe, indépendant du fond d’écran.")
                        : Bridge.themeMode === "dark" ? qsTr("Nuit : thème sombre et reposant, fixe, indépendant du fond d’écran.")
                        : Bridge.themeMode === "custom" ? qsTr("Personnalisé : tes propres couleurs, avec option de verre transparent.")
                        : qsTr("Automatique : les couleurs suivent ton fond d’écran, la transparence du verre est réglable.")
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3outline
                }
            }
        }

        SettingsRow {
            rowId: "customPalette"
            sub: true
            shown: Bridge.themeMode === "custom"

            IconTextButton {
                text: qsTr("Modifier ma palette")
                icon: "palette"
                type: IconTextButton.Tonal
                onClicked: if (page.host)
                    page.host.openCustomEditor()
            }
        }
    }

    SettingsSection {
        sectionId: "transparency"
        label: qsTr("Transparence")

        SettingsSliderRow {
            id: transparency

            rowId: "transparency"
            from: 5
            to: 98
            stepSize: 1
            value: (1 - Bridge.glassOpacity) * 100
            valueText: Math.round(slider.value) + " %"
            dim: !page.glassOk
            hint: page.glassOk ? SettingsIndex.desc("transparency") : page.mode === "custom"
                ? qsTr("Active « Verre transparent » dans Modifier ma palette")
                : qsTr("Réglable avec les thèmes Automatique et Personnalisé")
            slider.objectName: "glassTransparency"
            // Un seul curseur pour les deux thèmes : il écrit la clé que lit le thème courant.
            onMoved: v => Bridge.action("preference", {
                    key: page.mode === "custom" ? "customGlassOpacity" : "glassOpacity",
                    value: 1 - v / 100
                })
        }
    }

    SettingsSection {
        sectionId: "animations"
        label: qsTr("Animations")

        SettingsRow {
            rowId: "motion"
            below: true

            SettingsChips {
                Layout.fillWidth: true
                namePrefix: "motion-"
                model: [
                    {
                        label: qsTr("Sans"),
                        value: 0
                    },
                    {
                        label: qsTr("Rapides"),
                        value: 0.5
                    },
                    {
                        label: "Caelestia",
                        value: 1
                    }
                ]
                current: page.motion <= 0.01 ? 0 : page.motion <= 0.75 ? 0.5 : 1
                onChosen: v => Bridge.action("preference", {
                        key: "motion",
                        value: v
                    })
            }
        }

        SettingsRow {
            rowId: "reducedEffects"
            target: reduced

            SettingsSwitch {
                id: reduced

                value: Bridge.reducedEffects
                onToggled: Bridge.action("preference", {
                    key: "reducedEffects",
                    enabled: checked
                })
            }
        }
    }
}
