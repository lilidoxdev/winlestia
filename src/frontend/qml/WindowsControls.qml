// Centre de contrôle : page principale (tuiles, curseurs, raccourcis) et sous-pages Wi-Fi / Bluetooth / Son dans le même panneau.
// Navigation : `page` ("main", "wifi", "bluetooth", "sound") est pilotée par Main.qml (`requestPage`) ; la page entrante glisse de ±96 px
// en fondu pendant que la sortante s'efface. Aucune animation ni minuterie ne tourne au repos.
import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "CcSync.js" as CcSync

Item {
    id: root
    property string page: "main"
    signal requestPage(string name)
    readonly property real subHeight: Math.min(560, Math.max(380, Bridge.screenHeight - 100))
    // Sous-pages construites en asynchrone une seule fois, peu après la première apparition du panneau.
    property bool warmed: false
    property bool powerOpen: false
    property int dir: 1
    property string previous: "main"
    onPageChanged: {
        dir = page === "main" ? -1 : 1;
        previous = page;
    }
    implicitHeight: page === "main" ? mainCol.implicitHeight : subHeight
    Behavior on implicitHeight {
        enabled: !Appearance.reducedEffects && root.visible
        Anim {
            duration: Appearance.anim.durations.expressiveSlowEffects
            easing.bezierCurve: Appearance.anim.curves.expressiveSlowEffects
        }
    }
    Timer {
        interval: 250
        running: root.visible && !root.warmed
        repeat: false
        onTriggered: root.warmed = true
    }

    readonly property var paired: (Bridge.peripherals.paired ?? []).filter(
        d => d && d.connected && !String(d.id).startsWith("system:") && d.kind !== "laptop")
    readonly property string wifiSubtitle: {
        if (!(Bridge.controls.wifiOn ?? false))
            return qsTr("Désactivé");
        return Bridge.controls.network || qsTr("Non connecté");
    }
    readonly property string btSubtitle: {
        if (!(Bridge.controls.bluetoothOn ?? false))
            return qsTr("Désactivé");
        if (paired.length === 0)
            return qsTr("Aucun appareil");
        if (paired.length === 1)
            return paired[0].name || qsTr("1 connecté");
        return qsTr("%1 connectés").arg(paired.length);
    }
    readonly property bool muted: Bridge.controls.muted ?? false

    function volumeIcon(level, mute) {
        return mute || level <= 0 ? "volume_off" : level < 34 ? "volume_mute" : level < 67 ? "volume_down" : "volume_up";
    }

    component ShortcutTile: StyledRect {
        id: sc
        property string icon
        property string label
        signal activated
        Layout.fillWidth: true
        Layout.preferredWidth: 1
        implicitHeight: 52
        radius: 16
        color: Colours.palette.m3surfaceContainerHigh
        RowLayout {
            anchors.centerIn: parent
            spacing: Appearance.spacing.smaller
            MaterialIcon {
                text: sc.icon
                color: Colours.palette.m3onSurfaceVariant
            }
            StyledText {
                text: sc.label
                font.pointSize: Appearance.font.size.normal
                color: Colours.palette.m3onSurface
            }
        }
        StateLayer {
            color: Colours.palette.m3onSurface
            onClicked: sc.activated()
        }
    }

    // Un emplacement par page : fondu + glissement uniquement lors d'un changement de page.
    component Slot: Item {
        id: slot
        property bool shown: false
        property bool ready: false
        opacity: 0
        visible: opacity > 0
        enabled: shown
        onShownChanged: {
            if (!ready)
                return;
            fadeIn.stop();
            fadeOut.stop();
            if (shown) {
                x = root.dir * 96;
                opacity = 0;
                fadeIn.restart();
            } else {
                fadeOut.restart();
            }
        }
        Component.onCompleted: {
            opacity = shown ? 1 : 0;
            ready = true;
        }
        ParallelAnimation {
            id: fadeIn
            Anim {
                target: slot
                property: "x"
                to: 0
                duration: Appearance.anim.durations.expressiveSlowEffects
                easing.bezierCurve: Appearance.anim.curves.expressiveDefaultSpatial
            }
            Anim {
                target: slot
                property: "opacity"
                to: 1
                duration: Appearance.anim.durations.expressiveSlowEffects
                easing.bezierCurve: Appearance.anim.curves.expressiveSlowEffects
            }
        }
        Anim {
            id: fadeOut
            target: slot
            property: "opacity"
            to: 0
            duration: Appearance.anim.durations.small
        }
    }
    component SubPage: Slot {
        id: sub
        property Component content
        property bool want: false
        width: root.width
        height: root.subHeight
        Loader {
            anchors.fill: parent
            asynchronous: true
            active: sub.want || root.warmed
            sourceComponent: sub.content
        }
    }

    Slot {
        id: mainSlot
        objectName: "ccMain"
        shown: root.page === "main"
        width: root.width
        height: mainCol.implicitHeight
        ColumnLayout {
            id: mainCol
            width: parent.width
            spacing: 14

            RowLayout {
                Layout.fillWidth: true
                spacing: Appearance.spacing.smaller
                StyledText {
                    objectName: "ccTitle"
                    text: qsTr("Centre de contrôle")
                    font.pointSize: Appearance.font.size.title
                    Layout.fillWidth: true
                    elide: Text.ElideRight
                }
                CcIconButton {
                    objectName: "ccCustomize"
                    icon: "palette"
                    tip: qsTr("Personnaliser")
                    onClicked: Bridge.action("settings", {})
                }
                CcIconButton {
                    objectName: "ccTools"
                    icon: "construction"
                    tip: qsTr("Atelier")
                    onClicked: Bridge.action("tools", {})
                }
                CcIconButton {
                    objectName: "ccPower"
                    icon: "power_settings_new"
                    tip: qsTr("Session")
                    toggle: true
                    checked: root.powerOpen
                    onClicked: root.powerOpen = !root.powerOpen
                }
            }
            Item {
                objectName: "ccPowerMenu"
                Layout.fillWidth: true
                Layout.topMargin: root.powerOpen ? 0 : -14
                implicitHeight: root.powerOpen ? 44 : 0
                opacity: root.powerOpen ? 1 : 0
                visible: opacity > 0
                clip: true
                Behavior on implicitHeight {
                    enabled: !Appearance.reducedEffects
                    Anim {
                        duration: Appearance.anim.durations.small
                    }
                }
                Behavior on opacity {
                    enabled: !Appearance.reducedEffects
                    Anim {
                        duration: Appearance.anim.durations.small
                    }
                }
                RowLayout {
                    anchors.fill: parent
                    spacing: Appearance.spacing.smaller
                    IconTextButton {
                        Layout.fillWidth: true
                        type: IconTextButton.Tonal
                        icon: "desktop_windows"
                        text: qsTr("Barre Windows")
                        onClicked: Bridge.action("restoreTaskbar", {})
                    }
                    IconTextButton {
                        objectName: "ccQuit"
                        Layout.fillWidth: true
                        type: IconTextButton.Tonal
                        icon: "power_settings_new"
                        text: qsTr("Quitter Winlestia")
                        onClicked: Bridge.action("quit", {})
                    }
                }
            }

            GridLayout {
                Layout.fillWidth: true
                columns: 2
                columnSpacing: 8
                rowSpacing: 8
                CcTile {
                    objectName: "tileWifi"
                    Layout.fillWidth: true
                    Layout.preferredWidth: 1
                    icon: "wifi"
                    title: qsTr("Wi-Fi")
                    subtitle: root.wifiSubtitle
                    checked: Bridge.controls.wifiOn ?? false
                    chevron: true
                    onToggled: Bridge.action("radio", {
                        kind: "wifi"
                    })
                    onOpened: root.requestPage("wifi")
                }
                CcTile {
                    objectName: "tileBluetooth"
                    Layout.fillWidth: true
                    Layout.preferredWidth: 1
                    icon: "bluetooth"
                    title: qsTr("Bluetooth")
                    subtitle: root.btSubtitle
                    checked: Bridge.controls.bluetoothOn ?? false
                    chevron: true
                    onToggled: Bridge.action("radio", {
                        kind: "bluetooth"
                    })
                    onOpened: root.requestPage("bluetooth")
                }
                CcTile {
                    objectName: "tileDark"
                    Layout.fillWidth: true
                    Layout.preferredWidth: 1
                    icon: "dark_mode"
                    title: qsTr("Thème sombre")
                    subtitle: Bridge.dark ? qsTr("Activé") : qsTr("Désactivé")
                    checked: Bridge.dark
                    onToggled: Bridge.action("theme", {
                        dark: !Bridge.dark
                    })
                }
                CcTile {
                    objectName: "tileLight"
                    Layout.fillWidth: true
                    Layout.preferredWidth: 1
                    icon: "speed"
                    title: qsTr("Mode léger")
                    subtitle: qsTr("Moins d’effets")
                    tip: qsTr("Réduit le flou et les animations")
                    checked: Bridge.reducedEffects
                    onToggled: Bridge.action("preference", {
                        key: "reducedEffects",
                        enabled: !Bridge.reducedEffects
                    })
                }
            }

            RowLayout {
                Layout.fillWidth: true
                spacing: 8
                CcSlider {
                    objectName: "ccVolume"
                    Layout.fillWidth: true
                    icon: root.volumeIcon(value, root.muted)
                    source: Bridge.controls.volume ?? 0
                    onMoved: Bridge.action("volume", {
                        value: value
                    })
                    onIconClicked: Bridge.action("masterMute", {
                        muted: !root.muted
                    })
                }
                StyledRect {
                    objectName: "ccSoundOpen"
                    implicitWidth: 44
                    implicitHeight: 44
                    radius: 14
                    color: Colours.palette.m3secondaryContainer
                    MaterialIcon {
                        anchors.centerIn: parent
                        text: "tune"
                        color: Colours.palette.m3onSecondaryContainer
                    }
                    StateLayer {
                        color: Colours.palette.m3onSecondaryContainer
                        onClicked: root.requestPage("sound")
                    }
                    HoverHandler {
                        id: soundHover
                    }
                    ShellToolTip {
                        visible: soundHover.hovered
                        delay: 400
                        text: qsTr("Réglages du son")
                    }
                }
            }

            // Luminosité : une ligne par écran DDC ; la zone est repliée (hauteur 0) quand aucun écran ne la gère.
            Item {
                id: screens
                objectName: "ccBrightness"
                readonly property var list: Bridge.controls.displays ?? []
                Layout.fillWidth: true
                implicitHeight: screenModel.count > 0 ? screenCol.implicitHeight : 0
                opacity: screenModel.count > 0 ? 1 : 0
                visible: opacity > 0
                clip: true
                Behavior on implicitHeight {
                    enabled: !Appearance.reducedEffects && root.visible
                    Anim {
                        duration: Appearance.anim.durations.expressiveSlowEffects
                        easing.bezierCurve: Appearance.anim.curves.expressiveSlowEffects
                    }
                }
                Behavior on opacity {
                    enabled: !Appearance.reducedEffects
                    Anim {
                        duration: Appearance.anim.durations.expressiveSlowEffects
                    }
                }
                // Modèle stable : les lignes ne sont ajoutées/retirées que si la liste d'ids change ; sinon mise à jour en place.
                ListModel {
                    id: screenModel
                }
                function syncModel() {
                    CcSync.sync(screenModel, screens.list.filter(d => d.available).map(d => ({
                                screenId: d.id,
                                name: d.name || "",
                                value: d.value ?? 0
                            })), "screenId");
                }
                onListChanged: syncModel()
                Component.onCompleted: syncModel()
                ColumnLayout {
                    id: screenCol
                    width: parent.width
                    spacing: 8
                    Repeater {
                        model: screenModel
                        ColumnLayout {
                            id: screenRow
                            required property int screenId
                            required property string name
                            required property real value
                            Layout.fillWidth: true
                            spacing: 2
                            StyledText {
                                visible: screenModel.count > 1
                                text: screenRow.name || qsTr("Écran") + " " + screenRow.screenId
                                Layout.leftMargin: 4
                                font.pointSize: Appearance.font.size.small
                                color: Colours.palette.m3onSurfaceVariant
                            }
                            CcSlider {
                                Layout.fillWidth: true
                                icon: "brightness_6"
                                source: screenRow.value
                                onMoved: Bridge.action("brightness", {
                                    id: screenRow.screenId,
                                    value: value
                                })
                            }
                        }
                    }
                }
            }

            RowLayout {
                Layout.fillWidth: true
                spacing: 8
                ShortcutTile {
                    objectName: "ccCapture"
                    icon: "screenshot_region"
                    label: qsTr("Capture")
                    onActivated: Bridge.action("capture", {})
                }
                ShortcutTile {
                    objectName: "ccClipboard"
                    icon: "content_paste"
                    label: qsTr("Presse-papiers")
                    onActivated: Bridge.action("clipboard", {})
                }
            }
        }
    }

    SubPage {
        id: wifiSlot
        objectName: "ccWifi"
        shown: root.page === "wifi"
        want: shown
        content: Component {
            CcWifiPage {
                onBack: root.requestPage("main")
            }
        }
    }
    SubPage {
        id: btSlot
        objectName: "ccBluetooth"
        shown: root.page === "bluetooth"
        want: shown
        content: Component {
            CcBluetoothPage {
                onBack: root.requestPage("main")
            }
        }
    }
    SubPage {
        id: soundSlot
        objectName: "ccSound"
        shown: root.page === "sound"
        want: shown
        content: Component {
            CcSoundPage {
                onBack: root.requestPage("main")
            }
        }
    }
}
