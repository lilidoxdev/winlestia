import QtQuick
import QtQuick.Shapes
import qs.components
import qs.config
import ".."

// Bloc `progress` : value 0..1, variant ring|bar, label. Pas d'animation : la valeur saute à la mise à jour des données.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null

    readonly property real ratio: {
        const v = spec ? Number(Bridge.winjets.resolve(spec.value, ctx)) : 0;
        return isFinite(v) ? Math.max(0, Math.min(1, v)) : 0;
    }
    readonly property bool ring: spec ? Bridge.winjets.resolve(spec.variant, ctx) !== "bar" : true
    readonly property string caption: spec && spec.label ? Bridge.winjets.renderText(spec.label, ctx) : ""
    readonly property real diameter: Math.min(width, height)

    implicitWidth: ring ? 72 : 160
    implicitHeight: ring ? 72 : 24

    Shape {
        visible: block.ring
        anchors.centerIn: parent
        width: block.diameter
        height: block.diameter
        preferredRendererType: Shape.CurveRenderer

        ShapePath {
            fillColor: "transparent"
            strokeColor: WinjetTheme.track
            strokeWidth: 6
            PathAngleArc {
                centerX: block.diameter / 2
                centerY: block.diameter / 2
                radiusX: Math.max(1, block.diameter / 2 - 4)
                radiusY: radiusX
                startAngle: 0
                sweepAngle: 360
            }
        }
        ShapePath {
            fillColor: "transparent"
            strokeColor: WinjetTheme.accent
            strokeWidth: 6
            capStyle: ShapePath.RoundCap
            PathAngleArc {
                centerX: block.diameter / 2
                centerY: block.diameter / 2
                radiusX: Math.max(1, block.diameter / 2 - 4)
                radiusY: radiusX
                startAngle: -90
                sweepAngle: block.ratio * 359.9
            }
        }
    }

    StyledText {
        visible: block.ring
        anchors.centerIn: parent
        width: block.diameter - 20
        horizontalAlignment: Text.AlignHCenter
        text: block.caption !== "" ? block.caption : Math.round(block.ratio * 100) + " %"
        color: WinjetTheme.textPrimary
        font.pointSize: Appearance.font.size.small
        elide: Text.ElideRight
    }

    Column {
        visible: !block.ring
        anchors.fill: parent
        spacing: 4

        StyledText {
            visible: block.caption !== ""
            width: parent.width
            text: block.caption
            color: WinjetTheme.textSecondary
            font.pointSize: Appearance.font.size.small
            elide: Text.ElideRight
        }
        Rectangle {
            width: parent.width
            height: 8
            radius: 4
            color: WinjetTheme.track

            Rectangle {
                width: parent.width * block.ratio
                height: parent.height
                radius: 4
                color: WinjetTheme.accent
            }
        }
    }

    ActionArea {
        spec: block.spec
        host: block.host
    }
}
