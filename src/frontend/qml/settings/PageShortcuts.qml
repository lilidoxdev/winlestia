import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

// Every keyboard shortcut, grouped. Configurable ones open the capture dialog; fixed ones show why they are fixed.
ColumnLayout {
    id: page

    property var host
    readonly property var all: Bridge.settings.shortcuts ?? []
    // Panel-local keys are handled in QML, so the backend does not list them.
    readonly property var local: [
        {id: "local-tabs", group: qsTr("Dans le panneau"), label: qsTr("Changer d’onglet"), combo: "Ctrl+1 … Ctrl+4"},
        {id: "local-history", group: qsTr("Dans le panneau"), label: qsTr("Historique du presse-papiers"), combo: "Ctrl+H"},
        {id: "local-search", group: qsTr("Dans le panneau"), label: qsTr("Rechercher"), combo: "Ctrl+F"},
        {id: "local-close", group: qsTr("Dans le panneau"), label: qsTr("Fermer le panneau"), combo: "Échap"}
    ]
    readonly property var groups: {
        const names = [];
        for (const s of all)
            if (names.indexOf(s.group) < 0)
                names.push(s.group);
        return names;
    }

    function openDialog(id: string, label: string): void {
        if (host)
            host.openShortcutDialog(id, label);
    }

    spacing: Appearance.spacing.larger

    StyledText {
        Layout.fillWidth: true
        Layout.leftMargin: Appearance.padding.larger
        wrapMode: Text.WordWrap
        text: qsTr("Clique sur « Modifier », puis appuie sur la nouvelle combinaison. Un raccourci déjà pris est refusé avec une explication.")
        font.pointSize: Appearance.font.size.small
        color: Colours.palette.m3outline
    }

    Repeater {
        model: page.groups

        SettingsSection {
            id: sec

            required property string modelData

            sectionId: "group-" + modelData
            label: qsTr(modelData)

            Repeater {
                model: page.all.filter(s => s.group === sec.modelData)

                SettingsRow {
                    id: sc

                    required property var modelData
                    readonly property bool custom: modelData.combo !== modelData.defaultCombo

                    rowId: "sc:" + modelData.id
                    title: qsTr(modelData.label)
                    readonly property bool inactive: modelData.configurable && modelData.active === false
                    hintColor: inactive ? Colours.palette.m3error : Colours.palette.m3outline
                    hint: modelData.configurable ? (inactive ? qsTr("Inactif : une autre application utilise cette combinaison.") : "") : qsTr(modelData.reason ?? "")

                    SettingsKeyCaps {
                        combo: sc.modelData.combo
                        muted: !sc.modelData.configurable
                    }

                    MaterialIcon {
                        visible: !sc.modelData.configurable
                        text: "lock"
                        font.pointSize: Appearance.font.size.large
                        color: Colours.palette.m3outline
                    }

                    IconButton {
                        visible: sc.modelData.configurable && sc.custom
                        objectName: "shortcutReset-" + sc.modelData.id
                        icon: "restart_alt"
                        type: IconButton.Text
                        onClicked: Bridge.action("shortcut-reset", {id: sc.modelData.id})
                    }

                    TextButton {
                        visible: sc.modelData.configurable
                        objectName: "shortcutEdit-" + sc.modelData.id
                        text: qsTr("Modifier")
                        type: TextButton.Tonal
                        onClicked: page.openDialog(sc.modelData.id, qsTr(sc.modelData.label))
                    }
                }
            }
        }
    }

    SettingsSection {
        sectionId: "group-local"
        label: qsTr("Dans le panneau")

        Repeater {
            model: page.local

            SettingsRow {
                id: lc

                required property var modelData

                rowId: "sc:" + modelData.id
                title: modelData.label
                hint: qsTr("Actif seulement quand le panneau est ouvert.")

                SettingsKeyCaps {
                    combo: lc.modelData.combo.indexOf(" … ") >= 0 ? "" : lc.modelData.combo
                    emptyText: lc.modelData.combo
                    muted: true
                }

                MaterialIcon {
                    text: "lock"
                    font.pointSize: Appearance.font.size.large
                    color: Colours.palette.m3outline
                }
            }
        }
    }
}
