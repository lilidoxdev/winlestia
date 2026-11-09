import QtQuick
import qs.components
import qs.config
import qs.services
import ".."

// Bloc `clock` : analog|digital, showSeconds, format, timezone. Une minute par minute ; la seconde seulement si les secondes
// sont affichées (et la carte visible).
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null

    readonly property bool analog: !!spec && Bridge.winjets.resolve(spec.variant, ctx) === "analog"
    readonly property bool seconds: !!spec && Bridge.winjets.resolve(spec.showSeconds, ctx) === true
    readonly property string pattern: spec && spec.format ? spec.format : (seconds ? "HH:mm:ss" : "HH:mm")
    property double stamp: Time.date.getTime()
    readonly property var parts: Bridge.winjets.clockInfo(spec && spec.timezone ? Bridge.winjets.renderText(spec.timezone, ctx) : "", pattern, stamp)
    readonly property real size: Math.min(width, height)

    implicitWidth: analog ? 96 : label.implicitWidth
    implicitHeight: analog ? 96 : label.implicitHeight

    Connections {
        target: Time

        function onDateChanged(): void {
            if (!block.seconds)
                block.stamp = Time.date.getTime();
        }
    }

    Timer {
        interval: 1000
        repeat: true
        running: block.seconds && (!block.host || block.host.active)
        triggeredOnStart: true
        onTriggered: block.stamp = Date.now()
    }

    StyledText {
        id: label

        visible: !block.analog
        anchors.fill: parent
        text: block.parts.text
        color: WinjetTheme.textPrimary
        font.pointSize: Math.max(Appearance.font.size.normal, Math.min(Appearance.font.size.extraLarge + 12, block.height * 0.375))
        font.weight: Font.Medium
        horizontalAlignment: block.spec && block.spec.align === "center" ? Text.AlignHCenter : Text.AlignLeft
        verticalAlignment: Text.AlignVCenter
        fontSizeMode: Text.HorizontalFit
        minimumPixelSize: 12
        elide: Text.ElideNone
    }

    Item {
        id: face

        visible: block.analog
        anchors.centerIn: parent
        width: block.size
        height: block.size

        Rectangle {
            anchors.fill: parent
            radius: width / 2
            color: "transparent"
            border.width: 1
            border.color: Qt.alpha(WinjetTheme.textSecondary, 0.5)
        }

        Repeater {
            model: 12

            Rectangle {
                required property int index

                width: index % 3 === 0 ? 3 : 2
                height: index % 3 === 0 ? face.height * 0.09 : face.height * 0.05
                radius: width / 2
                color: WinjetTheme.textSecondary
                x: face.width / 2 - width / 2
                y: face.height * 0.06
                transform: Rotation {
                    origin.x: width / 2
                    origin.y: face.height / 2 - face.height * 0.06
                    angle: index * 30
                }
            }
        }

        Rectangle {
            width: Math.max(3, face.width * 0.04)
            height: face.height * 0.26
            radius: width / 2
            color: WinjetTheme.textPrimary
            x: face.width / 2 - width / 2
            y: face.height / 2 - height
            transform: Rotation {
                origin.x: width / 2
                origin.y: height
                angle: (block.parts.hour % 12 + block.parts.minute / 60) * 30
            }
        }

        Rectangle {
            width: Math.max(2, face.width * 0.028)
            height: face.height * 0.38
            radius: width / 2
            color: WinjetTheme.textPrimary
            x: face.width / 2 - width / 2
            y: face.height / 2 - height
            transform: Rotation {
                origin.x: width / 2
                origin.y: height
                angle: block.parts.minute * 6
            }
        }

        Rectangle {
            visible: block.seconds
            width: 1.5
            height: face.height * 0.42
            color: WinjetTheme.accent
            x: face.width / 2 - width / 2
            y: face.height / 2 - height
            transform: Rotation {
                origin.x: width / 2
                origin.y: height
                angle: block.parts.second * 6
            }
        }

        Rectangle {
            width: face.width * 0.06
            height: width
            radius: width / 2
            color: WinjetTheme.accent
            anchors.centerIn: parent
        }
    }

    ActionArea {
        spec: block.spec
        host: block.host
    }
}
