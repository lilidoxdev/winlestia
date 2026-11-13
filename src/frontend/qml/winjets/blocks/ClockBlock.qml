import QtQuick
import qs.components
import qs.config
import qs.services
import ".."

// Bloc `clock` : analog|digital, seconds (alias showSeconds), format, timezone. Une minute par minute ; la seconde seulement si
// les secondes sont affichées, la carte visible et non suspendue. En numérique, les secondes sont en rôle secondaire.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null

    readonly property bool analog: !!spec && Bridge.winjets.resolve(spec.variant, ctx) === "analog"
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1
    readonly property bool seconds: !!spec && (Bridge.winjets.resolve(spec.seconds, ctx) === true || Bridge.winjets.resolve(spec.showSeconds, ctx) === true)
    // Secondes séparées (rôle secondaire) seulement sans `format` personnalisé.
    readonly property bool split: seconds && !analog && !(spec && spec.format)
    readonly property string pattern: spec && spec.format ? spec.format : (seconds && !split ? "HH:mm:ss" : "HH:mm")
    readonly property bool ticking: seconds && visible && (!host || host.live)
    property double stamp: Time.date.getTime()
    readonly property string zone: spec && spec.timezone ? Bridge.winjets.renderText(spec.timezone, ctx) : ""
    readonly property var parts: Bridge.winjets.clockInfo(zone, pattern, stamp)
    readonly property var secParts: Bridge.winjets.clockInfo(zone, "ss", stamp)
    readonly property real maxPt: Math.max(Appearance.font.size.normal, Appearance.font.size.extraLarge + 12) * sc
    readonly property real fitPt: Math.max(Appearance.font.size.normal * sc, Math.min(maxPt, block.height * 0.375))
    readonly property real size: Math.min(width, height)

    // Indépendant de la hauteur réelle (la taille de police en dépend) : évite la boucle de liaison.
    implicitWidth: analog ? 96 * sc : Appearance.font.size.normal * sc * (split ? 8 : 5)
    implicitHeight: analog ? 96 * sc : Appearance.font.size.normal * sc * 2

    Connections {
        target: Time

        function onDateChanged(): void {
            if (!block.seconds)
                block.stamp = Time.date.getTime();
        }
    }

    // Demande des secondes au rendu (ctx.time passe alors à la seconde) tant que ce bloc les affiche.
    onSecondsChanged: registerSeconds()
    Component.onCompleted: registerSeconds()
    Component.onDestruction: if (registered && host)
        host.secondsUsers -= 1
    property bool registered: false
    function registerSeconds(): void {
        if (!host || seconds === registered)
            return;
        registered = seconds;
        host.secondsUsers += seconds ? 1 : -1;
    }

    // 1000 ms, recalé sur la seconde pile à chaque tour ; arrêtée si cachée ou suspendue.
    Timer {
        id: secondTimer

        interval: 1000
        repeat: true
        running: block.ticking
        triggeredOnStart: true
        onTriggered: {
            block.stamp = Date.now();
            interval = 1000 - (block.stamp % 1000) + 5;
            if (block.host && block.host.tick)
                block.host.tick();
        }
    }

    StyledText {
        id: label

        visible: !block.analog && !block.split
        anchors.fill: parent
        text: block.parts.text
        color: WinjetTheme.textPrimary
        font.pointSize: block.fitPt
        font.weight: Font.Medium
        horizontalAlignment: block.spec && block.spec.align === "center" ? Text.AlignHCenter : Text.AlignLeft
        verticalAlignment: Text.AlignVCenter
        fontSizeMode: Text.HorizontalFit
        minimumPixelSize: 12 * block.sc
        elide: Text.ElideNone
    }

    // HH:MM en rôle principal, :SS en rôle secondaire ; réduit d'un bloc si la largeur manque (même effet que HorizontalFit).
    Item {
        id: splitRow

        visible: block.split
        implicitWidth: mainText.implicitWidth + secText.implicitWidth
        implicitHeight: mainText.implicitHeight
        width: implicitWidth
        height: implicitHeight
        readonly property real fit: implicitWidth > 0 ? Math.min(1, block.width / implicitWidth) : 1
        x: block.spec && block.spec.align === "center" ? (block.width - implicitWidth * fit) / 2 : 0
        y: (block.height - implicitHeight * fit) / 2
        transformOrigin: Item.TopLeft
        scale: fit

        StyledText {
            id: mainText

            text: block.parts.text
            color: WinjetTheme.textPrimary
            font.pointSize: block.fitPt
            font.weight: Font.Medium
        }
        StyledText {
            id: secText

            anchors.left: mainText.right
            anchors.baseline: mainText.baseline
            text: ":" + block.secParts.text
            color: WinjetTheme.textSecondary
            font.pointSize: block.fitPt * 0.6
            font.weight: Font.Medium
        }
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

                width: (index % 3 === 0 ? 3 : 2) * block.sc
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
            width: Math.max(3 * block.sc, face.width * 0.04)
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
            width: Math.max(2 * block.sc, face.width * 0.028)
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
            width: 1.5 * block.sc
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
