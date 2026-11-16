import QtQuick
import qs.components
import qs.components.effects
import qs.services
import qs.config

// Carte « fetch » : puce « > » + titre, logo, lignes OS / USER / UP / BATT en monospace, pastilles de la palette.
LockCard {
    id: root

    readonly property real logoSize: Appearance.font.size.extraLarge * 3.2
    readonly property real dotSize: Appearance.font.size.larger
    readonly property var perf: Bridge.performance
    readonly property real battery: Number(perf.batteryPerc ?? -1)
    readonly property var lines: {
        const rows = [["OS", SysInfo.osPrettyName], ["USER", SysInfo.userName || "—"], ["UP", SysInfo.uptime]];
        if (isFinite(battery) && battery >= 0)
            rows.push(["BATT", `${Math.round(battery)}%${perf.batteryCharging === true ? " ⚡" : ""}`]);
        return rows;
    }
    readonly property var swatches: [Colours.palette.m3primary, Colours.palette.m3secondary, Colours.palette.m3tertiary, Colours.palette.m3error, Colours.palette.m3primaryContainer, Colours.palette.m3secondaryContainer, Colours.palette.m3surfaceContainerHigh]

    Row {
        id: head

        spacing: Appearance.spacing.normal

        StyledRect {
            anchors.verticalCenter: parent.verticalCenter
            implicitWidth: chip.implicitWidth + Appearance.padding.normal * 2
            implicitHeight: chip.implicitHeight + Appearance.padding.small * 2
            radius: Appearance.rounding.small
            color: Colours.palette.m3primary

            StyledText {
                id: chip

                anchors.centerIn: parent
                text: ">"
                color: Colours.palette.m3onPrimary
                font.family: Appearance.font.family.mono
                font.weight: Font.Bold
            }
        }

        StyledText {
            anchors.verticalCenter: parent.verticalCenter
            text: "winlestiafetch.sh"
            font.family: Appearance.font.family.mono
            font.pointSize: Appearance.font.size.larger
        }
    }

    Row {
        anchors.top: head.bottom
        anchors.topMargin: Appearance.spacing.large
        spacing: Appearance.spacing.large

        ColouredIcon {
            anchors.verticalCenter: parent.verticalCenter
            source: SysInfo.osLogo
            implicitSize: root.logoSize
            colour: Colours.palette.m3primary
        }

        Column {
            anchors.verticalCenter: parent.verticalCenter
            spacing: Appearance.spacing.small

            Repeater {
                model: root.lines

                StyledText {
                    required property var modelData

                    text: `${modelData[0]}  ${modelData[1]}`
                    font.family: Appearance.font.family.mono
                    font.pointSize: Appearance.font.size.normal
                    color: Colours.palette.m3onSurface
                }
            }

            Row {
                spacing: Appearance.spacing.small

                Repeater {
                    model: root.swatches

                    Rectangle {
                        required property color modelData

                        width: root.dotSize
                        height: root.dotSize
                        radius: width / 2
                        color: modelData
                    }
                }
            }
        }
    }
}
