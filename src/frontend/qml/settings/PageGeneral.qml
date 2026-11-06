import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

// Language, start-up, services, restoration, about.
ColumnLayout {
    id: page

    property var host
    readonly property string version: Bridge.settings.version ?? ""
    readonly property string dataFolder: Bridge.settings.dataFolder ?? ""

    spacing: Appearance.spacing.larger

    SettingsSection {
        sectionId: "language"
        label: qsTr("Langue")

        SettingsRow {
            rowId: "language"
            below: true

            SettingsOptionList {
                Layout.fillWidth: true
                namePrefix: "language-"
                model: [
                    {label: qsTr("Langue de Windows"), value: "auto", flag: "windows"},
                    {label: "Français", value: "fr", flag: "fr"},
                    {label: "English", value: "en", flag: "en"},
                    {label: "Español", value: "es", flag: "es"}
                ].map(o => ({label: o.label, value: o.value, flag: Bridge.localUrl(Bridge.assetRoot + "/flags/" + o.flag + ".svg")}))
                current: Bridge.languageMode
                onChosen: v => Bridge.setLanguage(v)
            }
        }
    }

    SettingsSection {
        sectionId: "startup"
        label: qsTr("Démarrage")

        SettingsRow {
            rowId: "autostart"
            target: autostart

            SettingsSwitch {
                id: autostart

                value: Bridge.settings.autostart ?? false
                onToggled: Bridge.action("preference", {key: "autostart", enabled: checked})
            }
        }
    }

    SettingsSection {
        sectionId: "services"
        label: qsTr("Services")

        SettingsRow {
            rowId: "weatherCity"

            IconTextButton {
                text: qsTr("Choisir")
                icon: "location_on"
                type: IconTextButton.Tonal
                onClicked: Bridge.chooseCity()
            }
        }

        SettingsRow {
            rowId: "refreshCatalogue"

            IconTextButton {
                text: qsTr("Actualiser")
                icon: "refresh"
                type: IconTextButton.Tonal
                onClicked: Bridge.action("refreshCatalogue", {})
            }
        }
    }

    SettingsSection {
        sectionId: "restoration"
        label: qsTr("Restauration")

        SettingsRow {
            rowId: "restore"

            IconTextButton {
                objectName: "restoreTaskbarButton"
                text: qsTr("Rétablir")
                icon: "desktop_windows"
                type: IconTextButton.Tonal
                onClicked: Bridge.action("restoreTaskbar", {})
            }
        }
    }

    SettingsSection {
        sectionId: "about"
        label: qsTr("À propos")

        SettingsRow {
            rowId: "about"
            hint: page.dataFolder !== "" ? page.dataFolder : SettingsIndex.desc("about")

            StyledText {
                visible: page.version !== ""
                text: page.version
                font.pointSize: Appearance.font.size.small
                font.weight: Font.Medium
                color: Colours.palette.m3primary
            }
        }
    }
}
