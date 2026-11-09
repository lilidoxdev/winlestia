// Cookie / clover style blob: r(t) = 1 + amp * cos(lobes * t), polygon computed once per parameter change.
import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes
import qs.components
import qs.components.controls
import qs.services
import qs.config

Shape {
    id: blob
    property int lobes: 8
    property real amp: 0.07
    property color color: Colours.palette.m3primaryContainer
    readonly property var pts: {
        const n = 96, r0 = Math.min(width, height) / 2 / (1 + amp), out = [];
        for (let i = 0; i <= n; i++) {
            const t = i / n * Math.PI * 2, r = r0 * (1 + amp * Math.cos(lobes * t));
            out.push(Qt.point(width / 2 + r * Math.cos(t), height / 2 + r * Math.sin(t)));
        }
        return out;
    }
    preferredRendererType: Shape.CurveRenderer
    implicitWidth: 56
    implicitHeight: 56
    ShapePath {
        strokeWidth: -1
        fillColor: blob.color
        PathPolyline {
            path: blob.pts
        }
        Behavior on fillColor {
            CAnim {}
        }
    }
}
