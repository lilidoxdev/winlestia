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
            preview: Component {
                SystemPreview {
                    kind: "osd"
                    value: osd.checked
                }
            }
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

        SettingsRow {
            rowId: "pill"
            preview: Component {
                SystemPreview {
                    kind: "pill"
                    value: pill.checked
                }
            }
            target: pill

            SettingsSwitch {
                id: pill

                objectName: "pillToggle"
                value: Bridge.settings.pill ?? true
                onToggled: Bridge.action("preference", {
                    key: "pill",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "lyrics"
            preview: Component {
                SystemPreview {
                    kind: "lyrics"
                    value: lyrics.checked
                }
            }
            target: lyrics

            SettingsSwitch {
                id: lyrics

                objectName: "lyricsToggle"
                value: Bridge.settings.lyrics ?? true
                onToggled: Bridge.action("preference", {
                    key: "lyrics",
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
            preview: Component {
                SystemPreview {
                    kind: "dictation"
                    value: dictation.checked
                    value2: ""
                }
            }
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
            preview: Component {
                SystemPreview {
                    kind: "dictation"
                    value: true
                    value2: Bridge.vkName(Bridge.settings.dictationKey ?? 0xA5)
                }
            }
            sub: true
            shown: Bridge.settings.dictationEnabled ?? false

            DictationKeyButton {
                objectName: "dictationKey"
            }
        }
    }
}
