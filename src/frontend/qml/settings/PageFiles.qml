import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

// Thème de l'Explorateur Windows et gestionnaire de fichiers Winlestia.
ColumnLayout {
    id: page

    property var host

    spacing: Appearance.spacing.larger

    SettingsSection {
        sectionId: "explorer"
        label: qsTr("Explorateur Windows")

        SettingsRow {
            rowId: "explorerTheme"
            below: true
            target: explorer

            RowLayout {
                Layout.fillWidth: true
                spacing: Appearance.spacing.normal

                Flow {
                    Layout.fillWidth: true
                    spacing: Appearance.spacing.small

                    Repeater {
                        model: ["desktop_windows", "download", "description", "image", "music_note", "movie"]

                        Rectangle {
                            required property string modelData

                            width: 44
                            height: 36
                            radius: Appearance.rounding.normal
                            color: Qt.alpha(Colours.palette.m3primary, 0.1)

                            MaterialIcon {
                                anchors.centerIn: parent
                                text: parent.modelData
                                color: Colours.palette.m3primary
                                font.pointSize: Appearance.font.size.larger
                            }
                        }
                    }
                }

                SettingsSwitch {
                    id: explorer

                    objectName: "explorerThemeToggle"
                    value: Bridge.settings.explorerTheme ?? false
                    onToggled: Bridge.action("preference", {
                        key: "explorerTheme",
                        enabled: checked
                    })
                }
            }
        }

        SettingsRow {
            rowId: "explorerFolders"
            below: true
            hint: (Bridge.settings.explorerStatus ?? qsTr("Prêt à personnaliser")) + " · "
                + qsTr("Icônes locales adaptatives. Le contenu et les menus restent ceux de Windows ; leur transparence ne change pas.")

            Flow {
                Layout.fillWidth: true
                spacing: Appearance.spacing.small

                IconTextButton {
                    text: qsTr("Ajouter un dossier")
                    icon: "create_new_folder"
                    type: IconTextButton.Tonal
                    onClicked: Bridge.action("explorerFolder", {})
                }

                IconTextButton {
                    text: qsTr("Ouvrir l’Explorateur")
                    icon: "open_in_new"
                    type: IconTextButton.Tonal
                    onClicked: Bridge.action("explorerOpen", {})
                }

                IconTextButton {
                    text: qsTr("Restaurer")
                    icon: "settings_backup_restore"
                    type: IconTextButton.Tonal
                    onClicked: Bridge.action("explorerRestore", {})
                }
            }
        }
    }

    SettingsSection {
        sectionId: "files"
        label: qsTr("Winlestia Fichiers")

        SettingsRow {
            rowId: "filesDefaultHandler"
            target: handler

            SettingsSwitch {
                id: handler

                objectName: "filesDefaultHandlerToggle"
                value: Bridge.settings.filesDefaultHandler ?? false
                onToggled: Bridge.action("preference", {
                    key: "filesDefaultHandler",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "filesPreload"
            target: preload

            SettingsSwitch {
                id: preload

                objectName: "filesPreloadToggle"
                value: Bridge.settings.filesPreload ?? false
                onToggled: Bridge.action("preference", {
                    key: "filesPreload",
                    enabled: checked
                })
            }
        }
    }
}
