// Port de l'OSD Caelestia (modules/osd) : curseur vertical volume / luminosité / micro sur le bord droit.
// Laisse passer les clics, ne prend jamais le focus, masqué (ni timer ni rendu) au repos.
import QtQuick
import QtQuick.Window
import qs.components
import qs.components.controls
import qs.services
import qs.config

Window {
    id: root
    objectName: "osd"
    title: "Panneaux · OSD"
    transientParent: null
    color: "transparent"
    flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint | Qt.WindowDoesNotAcceptFocus
        | Qt.WindowTransparentForInput
    readonly property bool reduced: Appearance.reducedEffects
    property bool shown: false
    property real volume: 0
    property bool muted: false
    property string kind: "volume"
    property real reveal: shown ? 1 : 0
    Behavior on reveal {
        enabled: !root.reduced
        Anim {
            duration: Appearance.anim.durations.small
            easing.bezierCurve: Appearance.anim.curves.emphasizedDecel
        }
    }
    readonly property real sliderWidth: 30
    readonly property real sliderHeight: 150
    width: sliderWidth + Appearance.padding.large * 2
    height: sliderHeight + Appearance.padding.large * 2
    x: Bridge.screenX + Bridge.screenWidth - width - Math.max(0, Bridge.frameThickness)
    y: Bridge.screenY + (Bridge.screenHeight - height) / 2
    visible: shown || reveal > 0.01
    function volumeIcon() {
        if (kind === "brightness")
            return "brightness_medium";
        if (kind === "mic")
            return muted ? "mic_off" : "mic";
        return muted ? "no_sound" : volume <= 0 ? "volume_mute" : volume < 0.5 ? "volume_down" : "volume_up";
    }
    function show(percent, mute, type) {
        kind = type || "volume";
        volume = Math.max(0, Math.min(1, percent / 100));
        muted = mute;
        shown = true;
        hideTimer.restart();
    }
    Timer {
        id: hideTimer
        interval: 1800
        onTriggered: root.shown = false
    }
    Connections {
        target: Bridge
        function onShellEvent(event) {
            if (!event.startsWith("osd:") || !(Bridge.settings.osd ?? true))
                return;
            var p = event.split(":");
            if (p[1] !== "volume" && p[1] !== "brightness" && p[1] !== "mic")
                return;
            root.show(Number(p[2]), p[3] === "1", p[1]);
        }
    }
    Rectangle {
        anchors.fill: parent
        radius: Appearance.rounding.large
        color: Qt.alpha(Colours.palette.m3surfaceContainer, Bridge.glassEnabled ? .94 : 1)
        border.width: 1
        border.color: Colours.palette.m3outlineVariant
        opacity: root.reveal
        scale: root.reduced ? 1 : .94 + .06 * root.reveal
        transformOrigin: Item.Right
        FilledSlider {
            anchors.centerIn: parent
            width: root.sliderWidth
            height: root.sliderHeight
            enabled: false
            showValue: false
            icon: root.volumeIcon()
            value: root.muted ? 0 : root.volume
        }
    }
}
