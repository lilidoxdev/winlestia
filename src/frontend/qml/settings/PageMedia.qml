import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import ".."
import "."

// Indicateurs à l'écran et dictée vocale.
ColumnLayout {
    id: page

    property var host

    spacing: Appearance.spacing.larger

    SettingsSection {
        sectionId: "osd"
        label: qsTr("Affichage à l’écran")

        SettingsRow {
            rowId: "osd"
            target: osd

            SettingsSwitch {
                id: osd

                objectName: "osdToggle"
                value: Bridge.settings.osd ?? true
                onToggled: Bridge.action("preference", {
                    key: "osd",
                    enabled: checked
                })
            }
        }
    }

    SettingsSection {
        sectionId: "dictation"
        label: qsTr("Dictée vocale")

        SettingsRow {
            rowId: "dictationEnabled"
            target: dictation

            SettingsSwitch {
                id: dictation

                objectName: "dictationToggle"
                value: Bridge.settings.dictationEnabled ?? false
                onToggled: Bridge.action("preference", {
                    key: "dictationEnabled",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "dictationKey"
            sub: true
            shown: Bridge.settings.dictationEnabled ?? false

            DictationKeyButton {
                objectName: "dictationKey"
            }
        }
    }
}
