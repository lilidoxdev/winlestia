import QtQuick
import qs.config
import ".."

// Bloc `dots` : matrice 2D de 0..1 (`matrix`) ou motif (`pattern` pulse|ripple|torus). Le motif n'avance (≈ 15 images/s) que
// tant que la carte est visible, hors effets réduits ; sinon une image fixe.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property var matrix: {
        const m = spec && spec.matrix ? Bridge.winjets.resolve(spec.matrix, ctx) : null;
        return m && typeof m === "object" && m.length && m[0] && typeof m[0] === "object" && m[0].length ? m : null;
    }
    readonly property string pattern: spec && spec.pattern ? String(Bridge.winjets.resolve(spec.pattern, ctx)) : "pulse"
    readonly property bool square: !!spec && Bridge.winjets.resolve(spec.shape, ctx) === "square"
    readonly property real cell: 12 * sc
    readonly property int cols: matrix ? matrix[0].length : Math.max(1, Math.min(40, Math.floor(width / cell)))
    readonly property int rows: matrix ? matrix.length : Math.max(1, Math.min(40, Math.floor(height / cell)))
    readonly property real pitch: Math.min(width / cols, height / rows)
    property real phase: 0
    readonly property bool animated: !matrix && !Appearance.reducedEffects && !!Window.window && Window.window.visible && (!host || host.live) && !(host && host.editing)

    implicitWidth: 96 * sc
    implicitHeight: 96 * sc

    Timer {
        interval: 66
        repeat: true
        running: block.animated
        onTriggered: block.phase = (block.phase + 0.05) % 1
    }

    function level(c: int, r: int): real {
        if (matrix) {
            const row = matrix[r];
            return row ? Math.max(0, Math.min(1, Number(row[c]) || 0)) : 0;
        }
        const dx = (c + 0.5) / cols - 0.5;
        const dy = (r + 0.5) / rows - 0.5;
        const d = Math.sqrt(dx * dx + dy * dy);
        const t = phase * 2 * Math.PI;
        switch (pattern) {
        case "ripple":
            return 0.5 + 0.5 * Math.sin(d * 18 - t);
        case "torus":
            return Math.max(0, 1 - Math.abs(d - 0.32 - 0.05 * Math.sin(t)) * 7);
        default:
            return Math.max(0, 1 - d * 2) * (0.6 + 0.4 * Math.sin(t));
        }
    }

    Grid {
        anchors.centerIn: parent
        columns: block.cols
        spacing: 0

        Repeater {
            model: block.cols * block.rows

            Item {
                id: dot

                required property int index
                readonly property real v: block.level(index % block.cols, Math.floor(index / block.cols))

                width: block.pitch
                height: block.pitch

                Rectangle {
                    anchors.centerIn: parent
                    width: Math.max(2, block.pitch * (0.25 + 0.5 * dot.v))
                    height: width
                    radius: block.square ? 1 : width / 2
                    color: WinjetTheme.accent
                    opacity: 0.2 + 0.8 * dot.v
                }
            }
        }
    }

    ActionArea {
        spec: block.spec
        host: block.host
    }
}
