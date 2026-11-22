import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

// Personnalisation : couleurs, mouvement, contours et cadre, bureau, ouverture du panneau.
// Chaque réglage montre un aperçu en direct (OptionPreview) de ce qu'il change.
ColumnLayout {
    id: page

    // Défini par l'hôte : { openCustomEditor(), openDetectionGuide() }
    property var host
    readonly property string mode: Bridge.themeMode
    readonly property real motion: Bridge.settings.motionScale ?? 1
    readonly property bool glassOk: mode === "auto" || (mode === "custom" && (Bridge.settings.customGlass ?? false))
    readonly property bool borders: Bridge.settings.accentBorders ?? true

    spacing: Appearance.spacing.larger

    SettingsSection {
        sectionId: "colors"
        label: qsTr("Couleurs")

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
                        model: [
                            {
                                mode: "light",
                                dark: false,
                                label: qsTr("Porcelaine")
                            },
                            {
                                mode: "dark",
                                dark: true,
                                label: qsTr("Nuit")
                            },
                            {
                                mode: "custom",
                                label: qsTr("Personnalisé")
                            },
                            {
                                mode: "auto",
                                label: qsTr("Automatique")
                            }
                        ]

                        StyledRect {
                            id: theme

                            required property var modelData
                            readonly property bool current: Bridge.themeMode === modelData.mode

                            objectName: "theme-" + modelData.mode
                            Layout.fillWidth: true
                            Layout.preferredWidth: 1
                            implicitHeight: 100
                            radius: Appearance.rounding.normal
                            color: Qt.alpha(Colours.palette.m3onSurface, current ? 0.1 : 0.05)
                            border.width: current ? 2 : 1
                            border.color: current ? Colours.palette.m3primary : Qt.alpha(Colours.palette.m3outlineVariant, 0.6)
                            activeFocusOnTab: true

                            // L'action recolore le modèle ci-dessus, ce qui détruit ce délégué : tout lire avant de l'appeler.
                            // Choisir « custom » ne change que le mode ; l'éditeur s'ouvre depuis la ligne « Ma palette » plus bas.
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
                                color: Colours.palette.m3onSurface

                                function onClicked(): void {
                                    theme.forceActiveFocus();
                                    theme.choose();
                                }
                            }

                            SettingsFocusRing {
                                target: theme
                            }

                            OptionPreview {
                                x: Appearance.padding.small
                                y: Appearance.padding.small
                                width: parent.width - Appearance.padding.small * 2
                                height: 64
                                kind: "theme"
                                value: theme.modelData.mode
                                label: theme.modelData.label
                            }

                            StyledText {
                                anchors.horizontalCenter: parent.horizontalCenter
                                y: 74
                                width: parent.width - 2
                                horizontalAlignment: Text.AlignHCenter
                                elide: Text.ElideRight
                                text: theme.modelData.label
                                color: theme.current ? Colours.palette.m3primary : Colours.palette.m3onSurface
                                font.pointSize: Appearance.font.size.smaller
                            }

                            MaterialIcon {
                                visible: theme.current
                                anchors.right: parent.right
                                anchors.top: parent.top
                                anchors.margins: Appearance.padding.normal
                                text: "check_circle"
                                color: Colours.palette.m3primary
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
            preview: Component {
                OptionPreview {
                    kind: "glass"
                    value: 100 - transparency.slider.value
                }
            }
            // Un seul curseur pour les deux thèmes : il écrit la clé que lit le thème courant.
            onMoved: v => Bridge.action("preference", {
                    key: page.mode === "custom" ? "customGlassOpacity" : "glassOpacity",
                    value: 1 - v / 100
                })
        }
    }

    SettingsSection {
        sectionId: "motion"
        label: qsTr("Mouvement")

        SettingsRow {
            rowId: "motion"
            below: true
            preview: Component {
                OptionPreview {
                    kind: "motion"
                    value: page.motion <= 0.01 ? 0 : page.motion <= 0.75 ? 0.5 : 1
                }
            }

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
            preview: Component {
                OptionPreview {
                    kind: "reducedEffects"
                    value: reduced.checked
                }
            }

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

    SettingsSection {
        sectionId: "borders"
        label: qsTr("Contours et cadre")

        SettingsRow {
            rowId: "accentBorders"
            target: bordersSwitch
            preview: Component {
                OptionPreview {
                    kind: "accentBorders"
                    value: bordersSwitch.checked
                }
            }

            SettingsSwitch {
                id: bordersSwitch

                value: page.borders
                onToggled: Bridge.action("preference", {
                    key: "accentBorders",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "activeBorderOnly"
            sub: true
            shown: page.borders
            target: activeOnly
            preview: Component {
                OptionPreview {
                    kind: "activeBorderOnly"
                    value: activeOnly.checked
                }
            }

            SettingsSwitch {
                id: activeOnly

                value: Bridge.settings.activeBorderOnly ?? false
                onToggled: Bridge.action("preference", {
                    key: "activeBorderOnly",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "screenFrame"
            target: frame
            preview: Component {
                OptionPreview {
                    kind: "screenFrame"
                    value: frame.checked
                    value2: Bridge.frameThickness
                }
            }

            SettingsSwitch {
                id: frame

                value: Bridge.frameEnabled
                onToggled: Bridge.action("preference", {
                    key: "screenFrame",
                    enabled: checked
                })
            }
        }

        SettingsSliderRow {
            id: thickness

            rowId: "frameThickness"
            sub: true
            shown: Bridge.frameEnabled
            from: 4
            to: 40
            value: Bridge.frameThickness
            valueText: Math.round(slider.value) + " px"
            slider.objectName: "frameThicknessSlider"
            preview: Component {
                OptionPreview {
                    kind: "screenFrame"
                    value: true
                    value2: thickness.slider.value
                }
            }
            onMoved: v => Bridge.action("preference", {
                    key: "frameThickness",
                    value: v
                })
        }
    }

    SettingsSection {
        sectionId: "desktop"
        label: qsTr("Bureau")

        SettingsRow {
            rowId: "desktopLogo"
            target: logo
            preview: Component {
                OptionPreview {
                    kind: "desktopLogo"
                    value: logo.checked
                }
            }

            SettingsSwitch {
                id: logo

                objectName: "desktopLogoToggle"
                value: Bridge.settings.desktopLogo ?? false
                onToggled: Bridge.action("preference", {
                    key: "desktopLogo",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "desktopIndicator"
            target: indicator
            preview: Component {
                OptionPreview {
                    kind: "desktopIndicator"
                    value: indicator.checked
                }
            }

            SettingsSwitch {
                id: indicator

                objectName: "desktopIndicatorToggle"
                value: Bridge.settings.desktopIndicator ?? true
                onToggled: Bridge.action("preference", {
                    key: "desktopIndicator",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "quietFullscreen"
            target: quiet
            preview: Component {
                OptionPreview {
                    kind: "quietFullscreen"
                    value: quiet.checked
                }
            }

            SettingsSwitch {
                id: quiet

                objectName: "quietFullscreenToggle"
                value: Bridge.settings.quietFullscreen ?? true
                onToggled: Bridge.action("preference", {
                    key: "quietFullscreen",
                    enabled: checked
                })
            }
        }
    }

    SettingsSection {
        sectionId: "panel"
        label: qsTr("Panneau du haut")

        SettingsRow {
            rowId: "topActivation"
            hint: SettingsIndex.desc("topActivation") + " (" + (Bridge.settings.topActivationMargin ?? 2) + " px)"
            preview: Component {
                OptionPreview {
                    kind: "topActivationMargin"
                    value: Bridge.settings.topActivationMargin ?? 2
                }
            }

            IconTextButton {
                objectName: "calibrateButton"
                text: qsTr("Calibrer")
                icon: "straighten"
                type: IconTextButton.Tonal
                onClicked: if (page.host)
                    page.host.openDetectionGuide()
            }
        }
    }
}
