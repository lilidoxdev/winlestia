import QtQuick
import QtQuick.Shapes
import qs.components
import qs.config
import ".."

// Bloc `chart` : series (nombres ou paires [t, v]), variant line|area|bars|candles|spark (aire en dégradé d'accent + point final), axes,
// period, color (rôle de palette),
// valueFormat (currency|percent|number : étiquettes d'axe). `candles` (séries [t,o,h,l,c], hover) est délégué à ChartCandles.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property string variant: spec ? String(Bridge.winjets.resolve(spec.variant, ctx) ?? "line") : "line"
    readonly property bool axes: spec ? Bridge.winjets.resolve(spec.axes, ctx) === true : false
    readonly property bool candles: variant === "candles"
    readonly property bool filled: variant === "area" || variant === "spark"
    readonly property string valueFormat: spec && spec.valueFormat ? String(Bridge.winjets.resolve(spec.valueFormat, ctx)) : ""
    readonly property color tint: WinjetTheme.role(spec && spec.color ? String(Bridge.winjets.resolve(spec.color, ctx)) : "accent")
    readonly property var values: {
        const raw = spec && !candles ? Bridge.winjets.resolve(spec.series, ctx) : null;
        const out = [];
        if (raw && raw.length)
            for (const p of raw) {
                const v = p && p.length ? Number(p[p.length - 1]) : Number(p);
                if (isFinite(v))
                    out.push(v);
            }
        return out;
    }
    readonly property real lo: values.length ? Math.min(...values) : 0
    readonly property real hi: values.length ? Math.max(...values) : 1
    readonly property real span: hi > lo ? hi - lo : 1
    readonly property real gutter: axes ? 36 * sc : 0
    readonly property real footer: axes && !!spec.period ? 16 * sc : 0
    readonly property real plotW: Math.max(1, width - gutter)
    readonly property real plotH: Math.max(1, height - footer)
    readonly property var points: {
        const out = [];
        const n = values.length;
        for (let i = 0; i < n; i++)
            out.push(Qt.point(gutter + (n > 1 ? i / (n - 1) : 0.5) * plotW, 2 + (1 - (values[i] - lo) / span) * (plotH - 4)));
        return out;
    }
    readonly property var areaPoints: points.length
        ? [Qt.point(points[0].x, plotH)].concat(points, [Qt.point(points[points.length - 1].x, plotH)])
        : []

    implicitWidth: 200 * sc
    implicitHeight: 80 * sc

    Shape {
        anchors.fill: parent
        visible: block.variant !== "bars" && !block.candles && block.values.length > 1
        preferredRendererType: Shape.CurveRenderer

        ShapePath {
            fillColor: "transparent"
            strokeWidth: -1
            fillGradient: LinearGradient {
                x1: 0
                y1: 0
                x2: 0
                y2: block.plotH
                GradientStop {
                    position: 0
                    color: Qt.alpha(block.tint, block.variant === "spark" ? 0.42 : 0.30)
                }
                GradientStop {
                    position: 1
                    color: Qt.alpha(block.tint, 0)
                }
            }
            PathPolyline {
                path: block.filled ? block.areaPoints : []
            }
        }
        ShapePath {
            fillColor: "transparent"
            strokeColor: block.tint
            strokeWidth: (block.variant === "spark" ? 1.5 : 2) * block.sc
            capStyle: ShapePath.RoundCap
            joinStyle: ShapePath.RoundJoin
            PathPolyline {
                path: block.points
            }
        }
    }

    Rectangle {
        visible: block.variant === "spark" && block.points.length > 1
        width: 7 * block.sc
        height: width
        radius: width / 2
        x: (block.points.length ? block.points[block.points.length - 1].x : 0) - width / 2
        y: (block.points.length ? block.points[block.points.length - 1].y : 0) - height / 2
        color: block.tint
        border.width: 2 * block.sc
        border.color: Qt.alpha(WinjetTheme.textPrimary, 0.25)
    }

    Repeater {
        model: block.variant === "bars" ? block.values.length : 0

        Rectangle {
            required property int index
            readonly property real slot: block.plotW / block.values.length
            readonly property real barH: Math.max(2, (block.values[index] - block.lo) / block.span * (block.plotH - 4))

            x: block.gutter + index * slot + slot * 0.15
            width: slot * 0.7
            height: barH
            y: block.plotH - barH
            radius: Math.min(3 * block.sc, width / 2)
            color: block.tint
        }
    }

    StyledText {
        visible: block.axes && block.values.length > 0
        x: 0
        y: 0
        width: block.gutter - 4
        text: block.valueFormat ? Bridge.winjets.fmt(block.hi, block.valueFormat) : Number(block.hi).toLocaleString(Qt.locale(), "f", 0)
        color: WinjetTheme.textSecondary
        font.pointSize: (Appearance.font.size.small) * block.sc
        horizontalAlignment: Text.AlignRight
        elide: Text.ElideLeft
    }
    StyledText {
        visible: block.axes && block.values.length > 0
        x: 0
        y: block.plotH - implicitHeight
        width: block.gutter - 4
        text: block.valueFormat ? Bridge.winjets.fmt(block.lo, block.valueFormat) : Number(block.lo).toLocaleString(Qt.locale(), "f", 0)
        color: WinjetTheme.textSecondary
        font.pointSize: (Appearance.font.size.small) * block.sc
        horizontalAlignment: Text.AlignRight
        elide: Text.ElideLeft
    }
    StyledText {
        visible: block.footer > 0
        x: block.gutter
        y: block.height - implicitHeight
        text: block.spec && block.spec.period ? Bridge.winjets.renderText(block.spec.period, block.ctx) : ""
        color: WinjetTheme.textSecondary
        font.pointSize: (Appearance.font.size.small) * block.sc
    }

    Loader {
        anchors.fill: parent
        active: block.candles
        source: active ? Qt.resolvedUrl("ChartCandles.qml") : ""
        onLoaded: {
            item.spec = Qt.binding(() => block.spec);
            item.ctx = Qt.binding(() => block.ctx);
            item.host = Qt.binding(() => block.host);
        }
    }

    ActionArea {
        spec: block.spec
        host: block.host
    }
}
