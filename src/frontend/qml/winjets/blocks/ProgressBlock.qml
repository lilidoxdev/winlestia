import QtQuick
import QtQuick.Shapes
import qs.components
import qs.config
import ".."

// Bloc `progress` : value 0..1, variant ring|bar, label, from (début de plage 0..1 pour la barre), thickness (épaisseur de l'anneau),
// big (grand nombre centré). Les valeurs s'animent en 450 ms (OutCubic) ; sans effets, elles sautent.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property real ratio: {
        const v = spec ? Number(Bridge.winjets.resolve(spec.value, ctx)) : 0;
        return isFinite(v) ? Math.max(0, Math.min(1, v)) : 0;
    }
    readonly property bool ring: spec ? Bridge.winjets.resolve(spec.variant, ctx) !== "bar" : true
    readonly property string caption: spec && spec.label ? Bridge.winjets.renderText(spec.label, ctx) : ""
    readonly property real fromRatio: {
        const v = spec && spec.from !== undefined ? Number(Bridge.winjets.resolve(spec.from, ctx)) : 0;
        return isFinite(v) ? Math.max(0, Math.min(1, v)) : 0;
    }
    readonly property bool range: !ring && spec && spec.from !== undefined
    readonly property int ringPx: Math.max(10, Math.round((big ? Math.min(34 * sc, diameter * 0.26) : 12 * sc)))
    readonly property real strokePx: Math.max(1, (spec && spec.thickness >= 1 ? Number(spec.thickness) : 6) * sc)
    readonly property bool big: spec ? Bridge.winjets.resolve(spec.big, ctx) === true : false
    property real shown: ratio
    property real shownFrom: fromRatio
    Behavior on shown {
        enabled: !Bridge.reducedEffects
        NumberAnimation {
            duration: WinjetTheme.motionSlow || 450
            easing.type: Easing.OutCubic
        }
    }
    Behavior on shownFrom {
        enabled: !Bridge.reducedEffects
        NumberAnimation {
            duration: WinjetTheme.motionSlow || 450
            easing.type: Easing.OutCubic
        }
    }
    readonly property real diameter: Math.min(width, height)

    implicitWidth: (ring ? 72 : 40) * sc
    implicitHeight: (ring ? 72 : 24) * sc

    Shape {
        visible: block.ring
        anchors.centerIn: parent
        width: block.diameter
        height: block.diameter
        preferredRendererType: Shape.CurveRenderer

        ShapePath {
            fillColor: "transparent"
            strokeColor: WinjetTheme.track
            strokeWidth: block.strokePx
            PathAngleArc {
                centerX: block.diameter / 2
                centerY: block.diameter / 2
                radiusX: Math.max(1, block.diameter / 2 - block.strokePx / 2 - 1)
                radiusY: radiusX
                startAngle: 0
                sweepAngle: 360
            }
        }
        ShapePath {
            fillColor: "transparent"
            strokeColor: WinjetTheme.accent
            strokeWidth: block.strokePx
            capStyle: ShapePath.RoundCap
            PathAngleArc {
                centerX: block.diameter / 2
                centerY: block.diameter / 2
                radiusX: Math.max(1, block.diameter / 2 - block.strokePx / 2 - 1)
                radiusY: radiusX
                startAngle: -90
                sweepAngle: block.shown * 359.9
            }
        }
    }

    StyledText {
        visible: block.ring
        anchors.centerIn: parent
        width: block.diameter - 20 * block.sc
        horizontalAlignment: Text.AlignHCenter
        text: block.caption !== "" ? block.caption : Math.round(block.ratio * 100) + " %"
        color: WinjetTheme.textPrimary
        font.pointSize: (block.ringPx) * 0.75
        font.weight: block.big ? Font.Light : Font.DemiBold
        font.letterSpacing: block.big ? -0.025 * block.ringPx : 0
        elide: Text.ElideRight
    }

    Column {
        visible: !block.ring
        anchors.fill: parent
        spacing: Math.round(4 * block.sc)

        StyledText {
            visible: block.caption !== ""
            width: parent.width
            text: block.caption
            color: WinjetTheme.textSecondary
            font.pointSize: (Appearance.font.size.small) * block.sc
            elide: Text.ElideRight
        }
        Rectangle {
            width: parent.width
            height: (block.range ? 5 : 8) * block.sc
            radius: height / 2
            color: WinjetTheme.track

            Rectangle {
                x: parent.width * block.shownFrom
                width: parent.width * Math.max(0, block.shown - block.shownFrom)
                height: parent.height
                radius: parent.radius
                color: WinjetTheme.accent
            }
        }
    }

    ActionArea {
        spec: block.spec
        host: block.host
    }
}
