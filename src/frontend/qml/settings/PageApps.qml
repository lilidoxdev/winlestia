import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

// Favoris, réseaux sociaux et historique du presse-papiers.
ColumnLayout {
    id: page

    property var host
    readonly property var browsers: (Bridge.bookmarks.browsers ?? []).filter(b => b.installed)
    readonly property bool sound: Bridge.social.sound ?? true
    readonly property int clipLimit: Bridge.settings.clipboardLimit ?? 30
    readonly property var limitModel: {
        const base = [8, 16, 24, 30, 40, 50, 60, 80];
        if (base.indexOf(clipLimit) < 0) {
            base.push(clipLimit);
            base.sort((a, b) => a - b);
        }
        return base.map(v => ({
                    label: String(v),
                    value: v
                }));
    }

    spacing: Appearance.spacing.larger

    SettingsSection {
        sectionId: "bookmarks"
        label: qsTr("Favoris")
        objectName: "bookmarksSettings"

        SettingsRow {
            rowId: "bookmarksBrowser"
            below: true

            SettingsOptionList {
                Layout.fillWidth: true
                namePrefix: "bookmarksBrowser-"
                model: page.browsers.map(b => ({
                            label: b.name + (b.isDefault ? qsTr(" (navigateur par défaut)") : ""),
                            value: b.id
                        }))
                current: Bridge.bookmarks.browser
                onChosen: v => Bridge.action("bookmarksBrowser", {
                        id: v
                    })
            }

            StyledText {
                objectName: "bookmarksNoBrowser"
                visible: page.browsers.length === 0
                text: qsTr("Aucun navigateur pris en charge n’a été détecté.")
                font.pointSize: Appearance.font.size.small
                color: Colours.palette.m3onSurfaceVariant
            }
        }

        SettingsRow {
            rowId: "bookmarksUnhide"
            title: qsTr("Réafficher les favoris retirés (%1)").arg(Bridge.bookmarks.hiddenCount ?? 0)

            TextButton {
                objectName: "bookmarksUnhideAll"
                enabled: (Bridge.bookmarks.hiddenCount ?? 0) > 0
                text: qsTr("Réafficher")
                type: TextButton.Tonal
                onClicked: Bridge.action("bookmarksUnhideAll", {})
            }
        }
    }

    SettingsSection {
        sectionId: "social"
        label: qsTr("Réseaux sociaux")
        objectName: "socialSettings"

        SettingsRow {
            rowId: "socialBackground"
            target: background

            SettingsSwitch {
                id: background

                objectName: "socialBackgroundToggle"
                value: Bridge.settings.socialBackground ?? false
                onToggled: Bridge.action("preference", {
                    key: "socialBackground",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "socialNetworks"
            below: true

            ColumnLayout {
                Layout.fillWidth: true
                spacing: Appearance.spacing.small

                Repeater {
                    model: Bridge.social.networks ?? []

                    RowLayout {
                        id: net

                        required property var modelData
                        readonly property bool connected: modelData.connected ?? false
                        readonly property bool enabledNet: modelData.enabled ?? false

                        objectName: "socialRow-" + modelData.id
                        Layout.fillWidth: true
                        spacing: Appearance.spacing.normal

                        Image {
                            Layout.preferredWidth: 32
                            Layout.preferredHeight: 32
                            sourceSize.width: 64
                            sourceSize.height: 64
                            fillMode: Image.PreserveAspectFit
                            source: Bridge.localUrl(Bridge.assetRoot + "/social/" + net.modelData.id + ".svg")
                        }

                        ColumnLayout {
                            Layout.fillWidth: true
                            Layout.minimumWidth: 80
                            spacing: 1

                            StyledText {
                                Layout.fillWidth: true
                                elide: Text.ElideRight
                                text: net.modelData.name
                                font.pointSize: Appearance.font.size.normal
                            }

                            StyledText {
                                objectName: "socialStatus-" + net.modelData.id
                                Layout.fillWidth: true
                                elide: Text.ElideRight
                                text: net.modelData.busy ? qsTr("Chargement…") : net.connected ? qsTr("Connecté")
                                    : qsTr("Non connecté")
                                font.pointSize: Appearance.font.size.small
                                color: net.connected ? Colours.palette.m3primary : Colours.palette.m3outline
                            }
                        }

                        TextButton {
                            visible: net.enabledNet && !net.connected
                            text: qsTr("Se connecter")
                            type: TextButton.Tonal
                            onClicked: Bridge.action("socialLogin", {
                                id: net.modelData.id
                            })
                        }

                        TextButton {
                            visible: net.connected
                            text: qsTr("Ouvrir")
                            type: TextButton.Tonal
                            onClicked: Bridge.action("socialOpen", {
                                id: net.modelData.id
                            })
                        }

                        IconButton {
                            visible: net.enabledNet
                            icon: net.modelData.muted ? "notifications_off" : "notifications"
                            type: net.modelData.muted ? IconButton.Tonal : IconButton.Text
                            onClicked: Bridge.action("socialMute", {
                                id: net.modelData.id,
                                muted: !(net.modelData.muted ?? false)
                            })
                        }

                        IconButton {
                            visible: net.connected
                            icon: "logout"
                            type: IconButton.Text
                            onClicked: Bridge.action("socialLogout", {
                                id: net.modelData.id
                            })
                        }

                        StyledSwitch {
                            objectName: "socialEnable-" + net.modelData.id
                            checked: net.enabledNet
                            onToggled: Bridge.action("socialEnable", {
                                id: net.modelData.id,
                                enabled: checked
                            })
                        }
                    }
                }
            }
        }
    }

    SettingsSection {
        sectionId: "notifications"
        label: qsTr("Notifications")

        SettingsRow {
            rowId: "socialGlobalMute"
            target: mute

            SettingsSwitch {
                id: mute

                objectName: "socialGlobalMute"
                value: Bridge.social.muted ?? false
                onToggled: Bridge.action("socialGlobalMute", {
                    muted: checked
                })
            }
        }

        SettingsRow {
            rowId: "socialSound"
            target: sound

            SettingsSwitch {
                id: sound

                objectName: "socialSound"
                value: page.sound
                onToggled: Bridge.action("socialSound", {
                    enabled: checked
                })
            }
        }

        SettingsSliderRow {
            rowId: "socialVolume"
            sub: true
            shown: page.sound
            from: 0
            to: 100
            value: Bridge.social.volume ?? 70
            valueText: Math.round(slider.value) + " %"
            commitOnRelease: true
            slider.objectName: "socialVolume"
            onMoved: v => Bridge.action("socialVolume", {
                    value: Math.round(v)
                })

            TextButton {
                text: qsTr("Tester")
                type: TextButton.Tonal
                onClicked: Bridge.action("socialTestSound", {})
            }
        }
    }

    SettingsSection {
        sectionId: "clipboard"
        label: qsTr("Presse-papiers")

        SettingsRow {
            rowId: "clipboardHistory"
            target: history

            SettingsSwitch {
                id: history

                value: Bridge.settings.clipboardHistory ?? false
                onToggled: Bridge.action("preference", {
                    key: "clipboardHistory",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "clipboardImages"
            sub: true
            shown: Bridge.settings.clipboardHistory ?? false
            target: images

            SettingsSwitch {
                id: images

                value: Bridge.settings.clipboardImages ?? true
                onToggled: Bridge.action("preference", {
                    key: "clipboardImages",
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "clipboardLimit"
            sub: true
            shown: Bridge.settings.clipboardHistory ?? false

            SettingsStepper {
                objectName: "clipboardLimit"
                model: page.limitModel
                current: page.clipLimit
                onChosen: v => Bridge.action("preference", {
                        key: "clipboardLimit",
                        value: v
                    })
            }
        }
    }
}
