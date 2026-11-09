// Capture d'une seule touche pour la dictée push-to-talk. Code de touche virtuelle Windows capturé par le backend (touches de modification
// seules autorisées).
import QtQuick
import QtQuick.Window
import qs.components
import qs.components.controls
import qs.services
import qs.config

IconTextButton {
    id: button
    property bool capturing: false
    readonly property int current: Bridge.settings.dictationKey ?? 0xA5
    text: capturing ? qsTr("Appuie sur une touche…") : qsTr(Bridge.vkName(current)) || qsTr("Choisir")
    icon: "keyboard"
    type: IconTextButton.Tonal
    onClicked: button.begin()
    // La capture se fait dans le backend (hook clavier bas niveau) : Qt ne reçoit pas le clavier dans ce shell.
    // Le backend renvoie dictation:key:<vk> ou dictation:keycancel (signal Bridge.dictationKeyCaptured), ou expire seul après 30 s.
    function begin() {
        if (capturing)
            return;
        capturing = true;
        Bridge.action("dictationCapture", {
            enabled: true
        });
        Bridge.editTheme(true);
        if (!cap)
            cap = capComp.createObject(button);
        cap.visible = true;
        expiry.restart();
    }
    function end() {
        if (!capturing)
            return;
        capturing = false;
        expiry.stop();
        if (cap)
            cap.visible = false;
        Bridge.action("dictationCapture", {
            enabled: false
        });
        Bridge.editTheme(false);
    }
    Timer {
        id: expiry
        interval: 31000
        onTriggered: button.end()
    }
    Connections {
        target: Bridge
        function onDictationKeyCaptured(vk) {
            button.end();
        }
    }
    // La fenêtre de capture n'est créée qu'au premier usage.
    property var cap: null
    Component {
        id: capComp
        Window {
            objectName: "dictationCapture"
            title: "Winlestia · Touche de dictée"
            transientParent: null
            color: "transparent"
            flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint
            width: 420
            height: 150
            x: Bridge.screenX + (Bridge.screenWidth - width) / 2
            y: Bridge.screenY + (Bridge.screenHeight - height) / 2
            onClosing: event => {
                event.accepted = false;
                button.end();
            }
            onVisibleChanged: if (!visible)
                button.end()
            Rectangle {
                anchors.fill: parent
                radius: Appearance.rounding.large
                color: Qt.alpha(Colours.surfaceBase, .98)
                border.width: 1
                border.color: Qt.alpha(Colours.palette.m3primary, .5)
                Column {
                    anchors.centerIn: parent
                    spacing: 10
                    StyledText {
                        anchors.horizontalCenter: parent.horizontalCenter
                        text: qsTr("Appuyez sur une touche…")
                        font.pointSize: 15
                    }
                    StyledText {
                        anchors.horizontalCenter: parent.horizontalCenter
                        text: qsTr("(Échap pour annuler)")
                        font.pointSize: 9
                        color: Colours.palette.m3outline
                    }
                }
                Rectangle {
                    anchors.horizontalCenter: parent.horizontalCenter
                    anchors.bottom: parent.bottom
                    anchors.bottomMargin: 12
                    width: 90
                    height: 28
                    radius: 14
                    color: Qt.alpha(Colours.palette.m3primary, .18)
                    StyledText {
                        anchors.centerIn: parent
                        text: qsTr("Annuler")
                        font.pointSize: 9
                    }
                    MouseArea {
                        anchors.fill: parent
                        cursorShape: Qt.PointingHandCursor
                        onClicked: button.end()
                    }
                }
            }
        }
    }
    Component.onDestruction: end()
}
