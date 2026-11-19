// Page Bluetooth du Centre de contrôle : appareils connectés puis appairés, connexion d'un toucher.
pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.components.containers
import qs.services
import qs.config
import "CcSync.js" as CcSync

Item {
    id: root
    signal back
    readonly property var peripherals: Bridge.peripherals ?? ({})
    readonly property string busyId: peripherals.busy ?? ""
    readonly property bool radioOn: Bridge.controls.bluetoothOn ?? false
    readonly property int total: linked.count + paired.count

    function iconFor(kind) {
        const icons = {
            headset: "headphones",
            mouse: "mouse",
            keyboard: "keyboard",
            gamepad: "sports_esports",
            speaker: "speaker",
            phone: "smartphone"
        };
        return icons[kind] || "bluetooth";
    }
    function rowOf(d) {
        const p = d.percent;
        return {
            devId: String(d.id),
            name: d.name || "",
            kind: d.kind || "",
            connected: !!d.connected,
            percent: typeof p === "number" && p >= 0 ? Math.round(p) : -1,
            canConnect: d.canConnect !== false
        };
    }
    function syncModel() {
        const all = (peripherals.paired ?? []).filter(d => d && !String(d.id).startsWith("system:") && d.kind !== "laptop").map(rowOf);
        CcSync.sync(linked, all.filter(r => r.connected), "devId");
        CcSync.sync(paired, all.filter(r => !r.connected), "devId");
    }
    onPeripheralsChanged: syncModel()
    Component.onCompleted: syncModel()
    ListModel {
        id: linked
    }
    ListModel {
        id: paired
    }
    function toggle(id, connected, canConnect) {
        if (root.busyId === id)
            return;
        if (canConnect)
            Bridge.action("bluetoothDevice", {
                id: id,
                connect: !connected
            });
        else
            Bridge.action("openBluetooth", {});
    }

    component Section: StyledText {
        Layout.topMargin: Appearance.spacing.smaller
        Layout.leftMargin: 4
        font.pointSize: Appearance.font.size.small
        color: Colours.palette.m3onSurfaceVariant
    }
    component DeviceRow: CcRow {
        id: row
        required property int index
        required property string devId
        required property string name
        required property string kind
        required property bool connected
        required property int percent
        required property bool canConnect
        property int count: 0
        readonly property bool busy: root.busyId === devId
        Layout.fillWidth: true
        implicitHeight: 60
        first: index === 0
        last: index === count - 1
        onClicked: root.toggle(devId, connected, canConnect)
        RowLayout {
            anchors.fill: parent
            anchors.leftMargin: 12
            anchors.rightMargin: 16
            spacing: Appearance.spacing.normal
            Rectangle {
                Layout.preferredWidth: 36
                Layout.preferredHeight: 36
                radius: 18
                color: row.connected ? Colours.palette.m3primary : Colours.palette.m3secondaryContainer
                MaterialIcon {
                    anchors.centerIn: parent
                    text: root.iconFor(row.kind)
                    fill: row.connected ? 1 : 0
                    color: row.connected ? Colours.palette.m3onPrimary : Colours.palette.m3onSecondaryContainer
                }
            }
            ColumnLayout {
                Layout.fillWidth: true
                spacing: 0
                StyledText {
                    Layout.fillWidth: true
                    text: row.name
                    elide: Text.ElideRight
                    font.pointSize: Appearance.font.size.normal
                }
                StyledText {
                    objectName: "btSubtitle"
                    Layout.fillWidth: true
                    elide: Text.ElideRight
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3outline
                    text: row.busy ? qsTr("Connexion…") : row.connected ?
                        (row.percent >= 0 ? qsTr("Connecté") + " • " + row.percent + " %" : qsTr("Connecté")) : qsTr("Appairé")
                }
            }
        }
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: Appearance.spacing.normal
        CcHeader {
            Layout.fillWidth: true
            title: qsTr("Bluetooth")
            hasSwitch: true
            checked: root.radioOn
            onBack: root.back()
            onSwitched: Bridge.action("radio", {
                kind: "bluetooth"
            })
        }
        StyledFlickable {
            id: scroll
            Layout.fillWidth: true
            Layout.fillHeight: true
            clip: true
            contentWidth: width
            contentHeight: body.implicitHeight
            boundsBehavior: Flickable.StopAtBounds
            C.ScrollBar.vertical: StyledScrollBar {
                flickable: scroll
            }
            ColumnLayout {
                id: body
                width: scroll.width
                spacing: 2
                ColumnLayout {
                    objectName: "btEmpty"
                    visible: root.total === 0 || !root.radioOn
                    Layout.fillWidth: true
                    Layout.topMargin: Appearance.spacing.large
                    spacing: Appearance.spacing.normal
                    MaterialIcon {
                        Layout.alignment: Qt.AlignHCenter
                        text: root.radioOn ? "bluetooth_searching" : "bluetooth_disabled"
                        font.pointSize: Appearance.font.size.extraLarge
                        color: Colours.palette.m3outline
                    }
                    StyledText {
                        Layout.fillWidth: true
                        horizontalAlignment: Text.AlignHCenter
                        wrapMode: Text.Wrap
                        text: !root.radioOn ? qsTr("Le Bluetooth est désactivé.") : qsTr("Aucun appareil appairé")
                        color: Colours.palette.m3outline
                    }
                    TextButton {
                        objectName: "btEmptySettings"
                        visible: root.radioOn
                        Layout.alignment: Qt.AlignHCenter
                        type: TextButton.Tonal
                        text: qsTr("Ouvrir les paramètres Bluetooth")
                        onClicked: Bridge.action("openBluetooth", {})
                    }
                }
                Section {
                    visible: linked.count > 0 && root.radioOn
                    text: qsTr("Connectés")
                }
                Repeater {
                    model: root.radioOn ? linked : null
                    DeviceRow {
                        count: linked.count
                    }
                }
                Section {
                    visible: paired.count > 0 && root.radioOn
                    text: qsTr("Appairés")
                }
                Repeater {
                    model: root.radioOn ? paired : null
                    DeviceRow {
                        count: paired.count
                    }
                }
                CcRow {
                    Layout.fillWidth: true
                    Layout.topMargin: Appearance.spacing.normal
                    implicitHeight: 48
                    first: true
                    last: true
                    onClicked: Bridge.action("openBluetooth", {})
                    RowLayout {
                        anchors.fill: parent
                        anchors.leftMargin: 16
                        anchors.rightMargin: 16
                        spacing: Appearance.spacing.normal
                        MaterialIcon {
                            text: "settings"
                            color: Colours.palette.m3onSurfaceVariant
                        }
                        StyledText {
                            Layout.fillWidth: true
                            text: qsTr("Paramètres Bluetooth")
                            font.pointSize: Appearance.font.size.normal
                        }
                        MaterialIcon {
                            text: "open_in_new"
                            color: Colours.palette.m3outline
                        }
                    }
                }
            }
        }
    }
}
