import QtQuick
import QtQuick.Shapes
import qs.components
import qs.services
import qs.config

// Jauge : forme matérielle dont le liquide ondulé monte du bas, icône au-dessus du pourcentage, pastille optionnelle.
Item {
    id: root

    property real value: 0
    property string icon
    property string badge
    property string shapePreset: "pentagon"
    property color shapeColour: Colours.palette.m3primaryContainer
    property color fillColour: Qt.alpha(Colours.palette.m3secondary, 0.3)
    property color textColour: Colours.palette.m3primary
    property color iconColour: Colours.palette.m3secondary

    readonly property real unit: width / 112
    readonly property real refWidth: 112
    readonly property real condensed: 0.62
    readonly property int waveSamples: 24
    readonly property real waveCycles: 1.5
    readonly property real waveAmplitude: 0.035
    readonly property int wavePeriodMs: 2600
    readonly property real clamped: Math.max(0, Math.min(1, value))
    // Niveau animé : suit `value` avec la courbe standard.
    property real level: clamped
    // La vague n'anime que tant que l'écran est verrouillé et visible.
    readonly property bool waving: visible && (Bridge.lock.locked ?? false)
    property real phase: 0

    function wavePath(w: real, h: real, lvl: real, ph: real): var {
        const amp = h * waveAmplitude, base = h * (1 - lvl);
        const pts = [];
        for (let i = 0; i <= waveSamples; i++) {
            const x = w * i / waveSamples;
            pts.push(Qt.point(x, base + amp * Math.sin(ph + (i / waveSamples) * waveCycles * 2 * Math.PI)));
        }
        pts.push(Qt.point(w, h + 1), Qt.point(0, h + 1));
        return pts;
    }

    implicitWidth: refWidth
    implicitHeight: implicitWidth

    Behavior on level {
        Anim {
            duration: Appearance.anim.durations.large
        }
    }

    NumberAnimation on phase {
        running: root.waving && root.level > 0
        from: 0
        to: 2 * Math.PI
        duration: root.wavePeriodMs
        loops: Animation.Infinite
    }

    LockShape {
        id: shape

        anchors.fill: parent
        preset: root.shapePreset
        fillColour: root.shapeColour

        Shape {
            anchors.fill: parent
            visible: root.level > 0.001
            preferredRendererType: Shape.CurveRenderer

            ShapePath {
                fillColor: root.fillColour
                strokeColor: "transparent"
                strokeWidth: 0

                PathPolyline {
                    path: root.wavePath(shape.width, shape.height, root.level, root.phase)
                }
            }
        }
    }

    Column {
        anchors.centerIn: parent
        spacing: -Appearance.spacing.small

        MaterialIcon {
            anchors.horizontalCenter: parent.horizontalCenter
            text: root.icon
            color: root.iconColour
            font.pointSize: Appearance.font.size.large * root.unit
        }

        StyledText {
            id: pct

            anchors.horizontalCenter: parent.horizontalCenter
            text: `${Math.round(root.clamped * 100)}%`
            color: root.textColour
            font.pointSize: Appearance.font.size.extraLarge * root.unit
            font.weight: Font.Black
            transform: Scale {
                origin.x: pct.width / 2
                xScale: root.condensed
            }
        }
    }

    StyledRect {
        id: badgeDot

        visible: root.badge !== ""
        anchors.top: parent.top
        anchors.right: parent.right
        anchors.topMargin: -Appearance.padding.small
        anchors.rightMargin: -Appearance.padding.small
        implicitHeight: Math.round(badgeText.implicitHeight * 2)
        implicitWidth: implicitHeight
        radius: Appearance.rounding.full
        color: Colours.solid(Colours.palette.m3secondaryContainer)

        StyledText {
            id: badgeText

            anchors.centerIn: parent
            text: root.badge
            color: Colours.palette.m3secondary
            font.pointSize: Appearance.font.size.small * root.unit
            font.weight: Font.Bold
        }
    }
}
