import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

// Borders, screen frame, desktop extras, panel opening zone.
ColumnLayout {
    id: page

    property var host
    readonly property bool borders: Bridge.settings.accentBorders ?? true

    spacing: Appearance.spacing.larger

    SettingsSection {
        sectionId: "borders"
        label: qsTr("Contours")

        SettingsRow {
            rowId: "accentBorders"
            target: borders

            SettingsSwitch {
                id: borders

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

            SettingsSwitch {
                id: activeOnly

                value: Bridge.settings.activeBorderOnly ?? false
                onToggled: Bridge.action("preference", {
                    key: "activeBorderOnly",
                    enabled: checked
                })
            }
        }
    }

    SettingsSection {
        sectionId: "frame"
        label: qsTr("Cadre de l’écran")

        SettingsRow {
            rowId: "screenFrame"
            target: frame

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
            rowId: "frameThickness"
            sub: true
            shown: Bridge.frameEnabled
            from: 4
            to: 40
            value: Bridge.frameThickness
            valueText: Math.round(slider.value) + " px"
            slider.objectName: "frameThicknessSlider"
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
        label: qsTr("Ouverture du panneau")

        SettingsRow {
            rowId: "topActivation"
            hint: (Bridge.settings.topActivationMargin ?? 2) + " px · " + qsTr("bande en haut de l’écran qui ouvre le panneau")

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
