import QtQuick
import qs.components
import qs.services
import qs.config

// Heures très grandes, minutes plus petites à côté, pastille AM/PM seulement en mode 12 h (réglage de l'application).
Item {
    id: root

    required property real centerScale
    readonly property bool twelve: Config.services.useTwelveHourClock
    readonly property var parts: Time.format(twelve ? "hh:mm:AP" : "hh:mm").split(":")
    readonly property real hourSize: Appearance.font.size.extraLarge * 6.5 * centerScale
    readonly property real minuteSize: hourSize * (twelve ? 0.54 : 1)
    readonly property real amPmSize: Appearance.font.size.extraLarge * 1.2 * centerScale

    implicitWidth: hours.implicitWidth + minutes.implicitWidth + Appearance.spacing.small
    implicitHeight: hours.implicitHeight * 0.82

    StyledText {
        id: hours

        y: -hours.implicitHeight * 0.1
        text: root.parts[0]
        color: Colours.palette.m3primary
        font.pointSize: root.hourSize
        font.family: Appearance.font.family.clock
        font.weight: Font.Bold
    }

    StyledText {
        id: minutes

        anchors.right: parent.right
        y: -minutes.implicitHeight * 0.1 + (hours.implicitHeight - minutes.implicitHeight) * 0.5
        text: root.parts[1]
        color: Colours.palette.m3secondary
        font.pointSize: root.minuteSize
        font.family: Appearance.font.family.clock
        font.weight: Font.Bold
    }

    StyledRect {
        visible: root.twelve
        anchors.left: minutes.left
        anchors.bottom: parent.bottom
        color: Colours.palette.m3surfaceContainerHigh
        radius: Appearance.rounding.large
        implicitWidth: minutes.implicitWidth
        implicitHeight: amPm.implicitHeight + Appearance.padding.large * 2

        StyledText {
            id: amPm

            anchors.centerIn: parent
            text: root.parts[2] ?? ""
            color: Colours.palette.m3onSurface
            font.pointSize: root.amPmSize
            font.family: Appearance.font.family.clock
        }
    }
}
