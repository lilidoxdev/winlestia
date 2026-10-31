import qs.services
import qs.config
import QtQuick

// Audio bars of the default output. The backend captures only while wanted is true (dashboard open, Dash tab shown, not fullscreen, effects not reduced).
// One light item: a Repeater of rectangles whose height follows Bridge.audioBars (0-100). Nothing moves when the levels stop changing.
Item {
    id: root

    readonly property int count: 28
    readonly property bool wanted: !Appearance.reducedEffects && (Bridge.window?.visible ?? false) && Bridge.window.pageMode === "dashboard" && Bridge.window.dashboardTab === 0
    readonly property real gap: Math.max(1, Appearance.padding.small / 2)
    readonly property real barWidth: Math.max(1, (width - gap * (count - 1)) / count)

    readonly property bool shown: !Appearance.reducedEffects

    visible: shown
    implicitHeight: shown ? Appearance.padding.large * 3 : 0

    onWantedChanged: Bridge.setAudioBars(wanted)
    Component.onCompleted: Bridge.setAudioBars(wanted)
    Component.onDestruction: Bridge.setAudioBars(false)

    Repeater {
        model: root.count

        Rectangle {
            required property int index

            x: index * (root.barWidth + root.gap)
            width: root.barWidth
            height: Math.max(0, Math.min(1, (Bridge.audioBars[index] ?? 0) / 100)) * root.height
            y: root.height - height
            radius: Math.min(width / 2, Appearance.rounding.tiny)
            color: Colours.palette.m3primary
            visible: height >= 1
        }
    }
}
