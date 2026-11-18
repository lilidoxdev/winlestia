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

    readonly property var sensors: Bridge.settings.sensors ?? {}
    readonly property bool sensorsAvailable: sensors.available ?? false
    readonly property string sensorsState: sensors.state ?? "absent"
    readonly property string sensorsResult: sensors.result ?? ""
    readonly property bool sensorsOutdated: sensors.outdated ?? false
    readonly property bool sensorsBusy: sensorsResult === "busy"
    property bool sensorsConsenting: false

    readonly property string sensorsActionLabel: {
        if (sensorsState === "absent" || sensorsState === "noDriver")
            return qsTr("Installer");
        if (sensorsOutdated)
            return qsTr("Mettre à jour");
        if (sensorsState === "stopped")
            return qsTr("Réparer");
        return "";
    }
    readonly property string sensorsHint: {
        switch (sensorsResult) {
        case "busy":
            return qsTr("Installation en cours…");
        case "declined":
            return qsTr("Autorisation refusée");
        case "failed":
            return qsTr("Échec de l'installation");
        case "reboot":
            return qsTr("Redémarrage de Windows requis pour terminer");
        }
        switch (sensorsState) {
        case "noDriver":
            return qsTr("Pilote PawnIO manquant");
        case "stopped":
            return qsTr("Installé, en attente de démarrage");
        case "active":
            return qsTr("Actif");
        }
        return qsTr("Non installé : Windows ne donne pas la température sans droits administrateur");
    }
    readonly property color sensorsHintColor: {
        if (sensorsResult === "failed" || sensorsResult === "declined")
            return Colours.palette.m3error;
        if (sensorsResult === "ok" || (sensorsResult === "" && sensorsState === "active"))
            return Colours.palette.m3primary;
        return Colours.palette.m3outline;
    }

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
                    {
                        label: qsTr("Langue de Windows"),
                        value: "auto",
                        flag: "windows"
                    },
                    {
                        label: "Français",
                        value: "fr",
                        flag: "fr"
                    },
                    {
                        label: "English",
                        value: "en",
                        flag: "en"
                    },
                    {
                        label: "Español",
                        value: "es",
                        flag: "es"
                    }
                ].map(o => ({
                            label: o.label,
                            value: o.value,
                            flag: Bridge.localUrl(Bridge.assetRoot + "/flags/" + o.flag + ".svg")
                        }))
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
                onToggled: Bridge.action("preference", {
                    key: "autostart",
                    enabled: checked
                })
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
        sectionId: "sensors"
        label: qsTr("Capteur de température")
        shown: page.sensorsAvailable

        SettingsRow {
            rowId: "sensorsStatus"

            hint: page.sensorsHint
            hintColor: page.sensorsHintColor

            RowLayout {
                spacing: Appearance.spacing.small

                IconTextButton {
                    objectName: "sensorsInstallButton"
                    visible: page.sensorsActionLabel !== ""
                    enabled: !page.sensorsBusy
                    text: page.sensorsActionLabel
                    icon: "download"
                    onClicked: page.sensorsConsenting = true
                }

                IconTextButton {
                    objectName: "sensorsRemoveButton"
                    visible: page.sensorsState !== "absent"
                    enabled: !page.sensorsBusy
                    text: qsTr("Retirer")
                    icon: "delete"
                    type: IconTextButton.Tonal
                    onClicked: {
                        page.sensorsConsenting = false;
                        Bridge.action("sensorsRemove", {});
                    }
                }
            }
        }

        SettingsRow {
            rowId: "sensorsConsent"
            title: qsTr("Autorisation requise")
            hint: qsTr(
                "Winlestia va installer un petit service qui s'exécute avec les droits administrateur, ainsi que le pilote PawnIO "
                + "(pilote noyau tiers, téléchargé depuis sa page officielle) s'il est absent. Windows vous demandera une autorisation. "
                + "Ce pilote peut être refusé par certains anti-triche de jeux. Vous pouvez retirer le service à tout moment ; le pilote "
                + "PawnIO se désinstalle depuis les applications Windows.")
            below: true
            shown: page.sensorsConsenting && !page.sensorsBusy

            RowLayout {
                Layout.fillWidth: true
                spacing: Appearance.spacing.small

                Item {
                    Layout.fillWidth: true
                }

                IconTextButton {
                    objectName: "sensorsCancelButton"
                    text: qsTr("Annuler")
                    icon: "close"
                    type: IconTextButton.Tonal
                    onClicked: page.sensorsConsenting = false
                }

                IconTextButton {
                    objectName: "sensorsConfirmButton"
                    text: qsTr("Installer")
                    icon: "check"
                    onClicked: {
                        page.sensorsConsenting = false;
                        Bridge.action("sensorsInstall", {
                            driver: true
                        });
                    }
                }
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
