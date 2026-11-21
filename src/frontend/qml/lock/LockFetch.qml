import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config

// Carte « fetch » : puce « > » + titre + curseur, marque Winlestia animée, lignes en monospace tapées à la volée, rangée de pastilles.
LockCard {
    id: root

    readonly property bool still: Appearance.reducedEffects
    readonly property bool live: visible && !still
    readonly property real dotSize: Appearance.font.size.title
    readonly property real waveRise: 3
    readonly property int keyWidth: 4
    readonly property int msPerChar: 28
    readonly property var perf: Bridge.performance
    readonly property real battery: Number(perf.batteryPerc ?? -1)
    readonly property real memTotalKib: SystemUsage.memPerc > 0 ? SystemUsage.memUsed / SystemUsage.memPerc : 0
    readonly property var lines: {
        const rows = [["OS", SysInfo.osPrettyName], ["USER", SysInfo.userName || "—"], ["UP", SysInfo.uptime],
            ["CPU", `${Math.round(SystemUsage.cpuPerc * 100)}%`]];
        if (memTotalKib > 0)
            rows.push(["RAM", `${(SystemUsage.memUsed / 1048576).toFixed(1)} / ${SystemUsage.fmtKib(memTotalKib, 0)}`]);
        if (isFinite(battery) && battery >= 0)
            rows.push(["BATT", `${perf.batteryCharging === true ? "(+) " : ""}${Math.round(battery)}%`]);
        return rows;
    }
    readonly property var swatches: [Colours.palette.m3surfaceContainerHighest, Colours.palette.m3primary, Colours.palette.m3secondary,
        Colours.palette.m3tertiary, Colours.palette.m3primaryContainer, Colours.palette.m3secondaryContainer,
        Colours.palette.m3inversePrimary]
    // Frappe : nombre de caractères révélés sur l'ensemble des lignes ; `typed` fige l'affichage complet.
    readonly property int totalChars: lines.reduce((sum, row) => sum + keyWidth + 1 + String(row[1]).length, 0)
    property real revealed: 0
    property bool typed: still

    implicitHeight: inset * 2 + head.height + linesColumn.implicitHeight + palette.height + Appearance.spacing.large * 2
    // Jamais plus petite que son contenu : sinon la colonne de lignes déborde sur l'en-tête.
    Layout.minimumHeight: implicitHeight

    SequentialAnimation {
        running: root.visible && !root.still && !root.typed

        PauseAnimation {
            duration: Appearance.anim.durations.expressiveDefaultSpatial
        }
        NumberAnimation {
            target: root
            property: "revealed"
            from: 0
            to: root.totalChars
            duration: root.totalChars * root.msPerChar
        }
        ScriptAction {
            script: root.typed = true
        }
    }

    Row {
        id: head

        spacing: Appearance.spacing.normal

        StyledRect {
            anchors.verticalCenter: parent.verticalCenter
            implicitWidth: chip.implicitWidth + Appearance.padding.large * 2
            implicitHeight: chip.implicitHeight + Appearance.padding.small * 2
            radius: Appearance.rounding.small
            color: Colours.palette.m3primary

            StyledText {
                id: chip

                anchors.centerIn: parent
                text: ">"
                color: Colours.palette.m3onPrimary
                font.family: Appearance.font.family.mono
                font.pointSize: Appearance.font.size.large
                font.weight: Font.Bold
            }
        }

        StyledText {
            anchors.verticalCenter: parent.verticalCenter
            text: "winlestiafetch.sh"
            font.family: Appearance.font.family.mono
            font.pointSize: Appearance.font.size.large
        }

        Rectangle {
            id: cursor

            anchors.verticalCenter: parent.verticalCenter
            width: Appearance.font.size.large * 0.6
            height: Appearance.font.size.large * 1.3
            radius: Appearance.rounding.tiny / 2
            color: Colours.palette.m3primary

            SequentialAnimation on opacity {
                running: root.live
                loops: Animation.Infinite

                PauseAnimation {
                    duration: Appearance.anim.durations.large
                }
                NumberAnimation {
                    to: 0
                    duration: Appearance.anim.durations.expressiveFastEffects
                }
                PauseAnimation {
                    duration: Appearance.anim.durations.large * 0.8
                }
                NumberAnimation {
                    to: 1
                    duration: Appearance.anim.durations.expressiveFastEffects
                }
            }
        }
    }

    Item {
        id: middle

        anchors.top: head.bottom
        anchors.topMargin: Appearance.spacing.large
        anchors.bottom: palette.top
        anchors.bottomMargin: Appearance.spacing.large
        anchors.left: parent.left
        anchors.right: parent.right
        clip: true

        LockMark {
            id: mark

            anchors.left: parent.left
            anchors.verticalCenter: parent.verticalCenter
            width: middle.width * 0.32
            height: middle.height
        }

        Column {
            id: linesColumn

            anchors.left: mark.right
            anchors.leftMargin: Appearance.spacing.large
            anchors.right: parent.right
            y: Math.max(0, (middle.height - height) / 2)
            spacing: Appearance.spacing.smaller

            Repeater {
                model: root.lines

                Row {
                    id: line

                    required property var modelData
                    required property int index

                    readonly property string keyFull: modelData[0].padEnd(root.keyWidth, " ") + ":"
                    readonly property string valueFull: String(modelData[1])
                    readonly property int offset: root.lines.slice(0, index).reduce(
                        (sum, row) => sum + root.keyWidth + 1 + String(row[1]).length, 0)
                    readonly property int shown: {
                        const full = keyFull.length + valueFull.length;
                        return root.typed ? full : Math.max(0, Math.min(full, Math.floor(root.revealed) - offset));
                    }

                    width: linesColumn.width
                    spacing: Appearance.spacing.small

                    StyledText {
                        text: line.keyFull.substring(0, line.shown)
                        color: Colours.palette.m3primary
                        font.family: Appearance.font.family.mono
                        font.pointSize: Appearance.font.size.larger
                        font.weight: Font.Bold
                    }

                    StyledText {
                        width: line.width - x
                        text: line.valueFull.substring(0, Math.max(0, line.shown - line.keyFull.length))
                        color: Colours.palette.m3onSurface
                        font.family: Appearance.font.family.mono
                        font.pointSize: Appearance.font.size.larger
                        elide: Text.ElideRight
                    }
                }
            }
        }
    }

    Item {
        id: palette

        anchors.bottom: parent.bottom
        anchors.left: parent.left
        anchors.right: parent.right
        height: root.dotSize + root.waveRise

        Repeater {
            model: root.swatches

            Rectangle {
                id: dot

                required property color modelData
                required property int index

                width: root.dotSize
                height: width
                radius: width / 2
                x: index * (palette.width - width) / (root.swatches.length - 1)
                y: root.waveRise
                color: Colours.solid(modelData)
                scale: root.still ? 1 : 0

                transform: Translate {
                    id: lift
                }

                SequentialAnimation {
                    running: root.typed && !root.still

                    PauseAnimation {
                        duration: dot.index * 60
                    }
                    NumberAnimation {
                        target: dot
                        property: "scale"
                        to: 1
                        duration: Appearance.anim.durations.expressiveFastSpatial
                        easing.bezierCurve: Appearance.anim.curves.expressiveFastSpatial
                    }
                }

                // Vague douce toutes les ~6 s : chaque pastille monte de quelques pixels puis revient.
                SequentialAnimation {
                    running: root.typed && root.live
                    loops: Animation.Infinite

                    PauseAnimation {
                        duration: Appearance.anim.durations.extraLarge * 2 + dot.index * 80
                    }
                    NumberAnimation {
                        target: lift
                        property: "y"
                        to: -root.waveRise
                        duration: Appearance.anim.durations.small
                        easing.type: Easing.OutQuad
                    }
                    NumberAnimation {
                        target: lift
                        property: "y"
                        to: 0
                        duration: Appearance.anim.durations.small * 1.5
                        easing.type: Easing.InOutQuad
                    }
                    PauseAnimation {
                        duration: Appearance.anim.durations.extraLarge * 4 - dot.index * 80 - Appearance.anim.durations.small * 2.5
                    }
                }
            }
        }
    }
}
