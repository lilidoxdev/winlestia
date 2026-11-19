import QtQuick
import QtQuick.Shapes
import QtQuick.Effects
import qs.services

// Polygone arrondi (points normalisés, coins adoucis par une courbe quadratique), avec calque enfant découpé à la forme.
// Prédéfinis : pentagon, slanted, gem, clamShell, circle.
Item {
    id: root

    property string preset: "circle"
    property color fillColour: "transparent"
    // Rayon des coins relatif au plus petit côté ; un prédéfini le fixe si la valeur reste négative.
    property real cornerRadius: -1
    // Les enfants déclarés ici sont découpés à la forme.
    default property alias content: clipLayer.data

    readonly property var presets: ({
            "pentagon": {
                "points": [[0.5, 0.0], [1.0, 0.38], [0.82, 1.0], [0.18, 1.0], [0.0, 0.38]],
                "radius": 0.16
            },
            "slanted": {
                "points": [[0.1, 0.0], [1.0, 0.06], [0.9, 1.0], [0.0, 0.94]],
                "radius": 0.2
            },
            "gem": {
                "points": [[0.27, 0.0], [0.73, 0.0], [1.0, 0.38], [0.5, 1.0], [0.0, 0.38]],
                "radius": 0.14
            },
            "clamShell": {
                "points": [[0.26, 0.0], [0.74, 0.0], [1.0, 0.5], [0.74, 1.0], [0.26, 1.0], [0.0, 0.5]],
                "radius": 0.2
            },
            "circle": {
                "points": [[0, 0], [1, 0], [1, 1], [0, 1]],
                "radius": 0.5
            }
        })
    readonly property var def: presets[preset] ?? presets.circle
    readonly property real radiusPx: (cornerRadius >= 0 ? cornerRadius : def.radius) * Math.min(width, height)
    readonly property string svgPath: buildPath(def.points, width, height, radiusPx)

    function buildPath(points: var, w: real, h: real, r: real): string {
        const n = points.length;
        if (n < 3 || w <= 0 || h <= 0)
            return "";
        const pts = points.map(p => ({
                    "x": p[0] * w,
                    "y": p[1] * h
                }));
        let d = "";
        for (let i = 0; i < n; i++) {
            const prev = pts[(i + n - 1) % n], cur = pts[i], next = pts[(i + 1) % n];
            const l1 = Math.hypot(prev.x - cur.x, prev.y - cur.y), l2 = Math.hypot(next.x - cur.x, next.y - cur.y);
            const u1 = {
                "x": (prev.x - cur.x) / l1,
                "y": (prev.y - cur.y) / l1
            }, u2 = {
                "x": (next.x - cur.x) / l2,
                "y": (next.y - cur.y) / l2
            };
            const cos = Math.max(-1, Math.min(1, u1.x * u2.x + u1.y * u2.y));
            const half = Math.acos(cos) / 2;
            // Longueur de tangente pour un arc de rayon r, bornée à la moitié des côtés voisins.
            const t = Math.min(r / Math.tan(half), l1 / 2, l2 / 2);
            const a = {
                "x": cur.x + u1.x * t,
                "y": cur.y + u1.y * t
            }, b = {
                "x": cur.x + u2.x * t,
                "y": cur.y + u2.y * t
            };
            d += `${i === 0 ? "M" : "L"}${a.x} ${a.y} Q${cur.x} ${cur.y} ${b.x} ${b.y} `;
        }
        return d + "Z";
    }

    implicitWidth: 120
    implicitHeight: 120

    Shape {
        anchors.fill: parent
        preferredRendererType: Shape.CurveRenderer

        ShapePath {
            fillColor: root.fillColour
            strokeColor: "transparent"
            strokeWidth: 0

            PathSvg {
                path: root.svgPath
            }
        }
    }

    Item {
        id: clipLayer

        anchors.fill: parent
        layer.enabled: true
        layer.effect: MultiEffect {
            maskEnabled: true
            maskSource: maskItem
            maskThresholdMin: 0.5
            maskSpreadAtMin: 1
        }
    }

    Item {
        id: maskItem

        anchors.fill: parent
        visible: false
        layer.enabled: true

        Shape {
            anchors.fill: parent
            preferredRendererType: Shape.CurveRenderer

            ShapePath {
                fillColor: Colours.palette.m3onSurface
                strokeColor: "transparent"
                strokeWidth: 0

                PathSvg {
                    path: root.svgPath
                }
            }
        }
    }
}
