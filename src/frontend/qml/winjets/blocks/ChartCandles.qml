import QtQuick
import QtQuick.Shapes
import qs.components
import qs.services
import qs.config
import ".."

// Variante `candles` du bloc chart : séries de [t, o, h, l, c]. Les bougies (mèches comprises) sont tracées une seule fois par
// changement de données, dans deux ShapePath (haussières m3primary, baissières m3error). Le réticule du survol n'est qu'un
// couple d'Items déplacés en QML (aucun retracé du graphique) et ne bouge que lorsque la bougie la plus proche change.
// Survol (`hover: true`) : la clôture de la bougie survolée est écrite dans l'état éphémère du moteur (`state._hover`, jamais envoyé au
// backend ; null à la sortie).
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property string valueFormat: spec ? String(Bridge.winjets.resolve(spec.valueFormat, ctx) ?? "number") : "number"
    readonly property bool hoverOn: spec ? Bridge.winjets.resolve(spec.hover, ctx) === true : false
    readonly property bool live: visible && (!host || (host.live && !host.editing))

    // Série normalisée : recalculée seulement si le contenu change (ctx est réévalué à chaque écriture d'état, dont le survol).
    readonly property string signature: {
        const raw = spec ? Bridge.winjets.resolve(spec.series, ctx) : null;
        return raw && typeof raw === "object" && raw.length > 0 ? JSON.stringify(Array.from(raw, p => Array.from(p))) : "";
    }
    property var candles: []
    onSignatureChanged: {
        const out = [];
        if (signature !== "")
            for (const p of JSON.parse(signature)) {
                if (!Array.isArray(p) || p.length < 5)
                    continue;
                const c = p.slice(0, 5).map(Number);
                if (c.every(isFinite))
                    out.push(c);
            }
        candles = out.length > 300 ? out.slice(out.length - 300) : out;
    }
    Component.onCompleted: signatureChanged()

    readonly property real lo0: candles.length ? Math.min(...candles.map(c => c[3])) : 0
    readonly property real hi0: candles.length ? Math.max(...candles.map(c => c[2])) : 1
    readonly property real pad: (hi0 > lo0 ? hi0 - lo0 : Math.max(1, Math.abs(hi0))) * 0.06
    readonly property real lo: lo0 - pad
    readonly property real hi: hi0 + pad
    readonly property real span: hi - lo

    readonly property real gutter: 74 * sc
    readonly property real plotW: Math.max(1, width - gutter)
    readonly property real plotH: Math.max(1, height)
    readonly property real slot: candles.length ? plotW / candles.length : plotW
    readonly property real bodyW: Math.max(1, slot * 0.68)

    function yOf(v: real): real {
        return (hi - v) / span * plotH;
    }

    // Deux chemins SVG (haussier / baissier) : un sous-chemin par bougie (mèche puis corps).
    readonly property var paths: {
        let up = "", down = "";
        const w = bodyW;
        for (let i = 0; i < candles.length; i++) {
            const c = candles[i];
            const cx = (i + 0.5) * slot;
            const top = yOf(Math.max(c[1], c[4]));
            const bottom = Math.max(top + 1, yOf(Math.min(c[1], c[4])));
            const d = `M${cx} ${yOf(c[2])}V${yOf(c[3])}M${cx - w / 2} ${top}h${w}V${bottom}h${-w}Z`;
            if (c[4] >= c[1])
                up += d;
            else
                down += d;
        }
        return [up, down];
    }

    // --- Survol ---------------------------------------------------------------------------------------------------------
    property int hoverIndex: -1
    readonly property var hovered: hoverIndex >= 0 && hoverIndex < candles.length ? candles[hoverIndex] : null
    readonly property real hoverX: (hoverIndex + 0.5) * slot
    readonly property real hoverY: hovered ? yOf(hovered[4]) : 0

    function format(v: real): string {
        return Bridge.winjets.fmt(v, valueFormat);
    }
    function stamp(t: real): string {
        return Qt.formatDateTime(new Date(t > 1e11 ? t : t * 1000), "dd/MM HH:mm");
    }
    function publish(index: int): void {
        if (index === hoverIndex)
            return;
        hoverIndex = index;
        if (host)
            host.setState("_hover", hovered ? hovered[4] : null);
    }
    onLiveChanged: if (!live)
        publish(-1)
    onCandlesChanged: {
        if (hoverIndex >= candles.length)
            publish(-1);
        else if (hoverIndex >= 0 && host)
            host.setState("_hover", hovered ? hovered[4] : null);
    }
    Component.onDestruction: if (host)
        host.setState("_hover", null)

    implicitWidth: 240 * sc
    implicitHeight: 120 * sc

    // Grille discrète : 4 traits m3outlineVariant très atténués, sans étiquette.
    Repeater {
        model: block.candles.length ? 4 : 0

        Rectangle {
            required property int index

            x: 0
            y: Math.min(block.plotH - 1, block.yOf(block.hi - (index + 1) / 5 * block.span))
            width: block.plotW
            height: 1
            color: Qt.alpha(Colours.palette.m3outlineVariant, 0.25)
        }
    }

    // Dernier cours : trait pointillé m3primary et pastille sur le bord droit.
    readonly property bool hasLast: candles.length > 0
    readonly property real lastY: hasLast ? Math.max(0, Math.min(plotH - 1, yOf(candles[candles.length - 1][4]))) : 0
    Item {
        visible: block.hasLast
        y: block.lastY
        width: block.plotW
        height: 1

        Repeater {
            model: block.hasLast ? Math.ceil(block.plotW / (6 * block.sc)) : 0

            Rectangle {
                required property int index

                x: index * 6 * block.sc
                width: 2 * block.sc
                height: 1
                color: Qt.alpha(Colours.palette.m3primary, 0.6)
            }
        }
    }
    Rectangle {
        id: lastPill

        visible: block.hasLast
        x: block.plotW + 2 * block.sc
        y: Math.max(0, Math.min(block.plotH - height, block.lastY - height / 2))
        width: block.gutter - 2 * block.sc
        height: lastText.implicitHeight + 4 * block.sc
        radius: height / 2
        color: Colours.palette.m3primary

        StyledText {
            id: lastText

            anchors.centerIn: parent
            text: block.hasLast ? block.format(block.candles[block.candles.length - 1][4]) : ""
            color: Colours.palette.m3onPrimary
            font.pointSize: Appearance.font.size.small * block.sc
        }
    }
    // Prix survolé : petit texte discret dans la colonne de droite.
    StyledText {
        visible: block.hovered !== null && Math.abs(block.hoverY - block.lastY) > implicitHeight
        x: block.plotW + 6 * block.sc
        y: Math.max(0, Math.min(block.plotH - implicitHeight, block.hoverY - implicitHeight / 2))
        text: block.hovered ? block.format(block.hovered[4]) : ""
        color: Colours.palette.m3onSurfaceVariant
        opacity: 0.8
        font.pointSize: Appearance.font.size.small * block.sc
    }

    Shape {
        width: block.plotW
        height: block.plotH
        visible: block.candles.length > 0
        preferredRendererType: Shape.CurveRenderer

        ShapePath {
            fillColor: Colours.palette.m3primary
            strokeColor: Colours.palette.m3primary
            strokeWidth: Math.max(1, block.sc)
            joinStyle: ShapePath.MiterJoin
            PathSvg {
                path: block.paths[0]
            }
        }
        ShapePath {
            fillColor: Colours.palette.m3error
            strokeColor: Colours.palette.m3error
            strokeWidth: Math.max(1, block.sc)
            joinStyle: ShapePath.MiterJoin
            PathSvg {
                path: block.paths[1]
            }
        }
    }

    // Réticule : deux traits pointillés (petits Rectangles) dans des Items qu'on déplace, rien d'autre ne change.
    Item {
        id: vline

        visible: block.hovered !== null
        x: block.hoverX
        width: 1
        height: block.plotH

        Repeater {
            model: Math.ceil(block.plotH / (6 * block.sc))

            Rectangle {
                required property int index

                y: index * 6 * block.sc
                width: 1
                height: 3 * block.sc
                color: Qt.alpha(Colours.palette.m3onSurfaceVariant, 0.7)
            }
        }
    }
    Item {
        id: hline

        visible: block.hovered !== null
        y: block.hoverY
        width: block.plotW
        height: 1

        Repeater {
            model: Math.ceil(block.plotW / (6 * block.sc))

            Rectangle {
                required property int index

                x: index * 6 * block.sc
                width: 3 * block.sc
                height: 1
                color: Qt.alpha(Colours.palette.m3onSurfaceVariant, 0.7)
            }
        }
    }

    // Infobulle compacte : une ligne (prix · heure) au bord haut du tracé, suit le réticule, bornée au tracé.
    Rectangle {
        id: tip

        visible: block.hovered !== null
        x: Math.max(0, Math.min(block.plotW - width, block.hoverX - width / 2))
        y: 2 * block.sc
        width: tipText.implicitWidth + 12 * block.sc
        height: tipText.implicitHeight + 4 * block.sc
        radius: height / 2
        color: Colours.palette.m3surfaceContainerHighest
        border.width: 1
        border.color: Colours.palette.m3outlineVariant

        StyledText {
            id: tipText

            anchors.centerIn: parent
            text: block.hovered ? block.format(block.hovered[4]) + " · " + block.stamp(block.hovered[0]) : ""
            color: Colours.palette.m3onSurface
            font.pointSize: Appearance.font.size.small * block.sc
        }
    }

    MouseArea {
        id: pointer

        anchors.fill: parent
        enabled: block.hoverOn && block.live && block.candles.length > 0
        hoverEnabled: true
        acceptedButtons: Qt.NoButton
        onPositionChanged: mouse => {
            const i = Math.floor(mouse.x / block.slot);
            block.publish(mouse.x > block.plotW ? -1 : Math.max(0, Math.min(block.candles.length - 1, i)));
        }
        onExited: block.publish(-1)
        onEnabledChanged: if (!enabled)
            block.publish(-1)
    }
}
