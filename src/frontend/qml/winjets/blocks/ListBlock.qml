import QtQuick
import QtQuick.Layouts
import qs.components
import qs.config
import qs.services
import ".."

// Bloc `list` : `items` (tableau), title/subtitle/trailing = noms de champs de l'élément (ou gabarits avec `item`), max, bar (champ 0 à
// 100).
// Options : `group` (en-têtes de groupe), `icon` (pastille de glyphe), `relative` (+ `past`) : temps relatif à droite, `accent` (barre
// d'accent à gauche, d'opacité variable), `tiles` (colonnes : tuiles d'icône). Les lignes apparaissent en fondu, décalées de 30 ms,
// seulement quand le contenu affiché change réellement (jamais pour un simple re-sondage de données identiques).
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property var items: {
        const v = spec ? Bridge.winjets.resolve(spec.items, ctx) : null;
        // Tableau réel ou liste QVariantList seulement : un objet {length: 3} issu d'un JSON distant n'est pas une liste.
        const arrayLike = typeof v === "object" && v !== null && typeof v.length === "number" && typeof v.slice === "function";
        const isList = Array.isArray(v) || arrayLike;
        return isList ? Array.prototype.slice.call(v) : [];
    }
    readonly property int limit: spec && spec.max >= 1 ? Math.floor(spec.max) : 5
    readonly property bool hasSub: !!spec && !!spec.subtitle
    // `bar` : nom du champ (0 à 100) tracé en fine jauge sous le sous-titre de chaque ligne.
    readonly property bool hasBar: !!spec && !!spec.bar
    readonly property bool hasGroup: !!spec && !!spec.group
    readonly property bool hasIcon: !!spec && !!spec.icon
    readonly property bool hasRelative: !!spec && !!spec.relative
    readonly property bool accentBars: spec ? Bridge.winjets.resolve(spec.accent, ctx) === true : false
    readonly property bool pastToo: spec ? Bridge.winjets.resolve(spec.past, ctx) === true : false
    readonly property int tileCols: spec && spec.tiles >= 1 ? Math.min(6, Math.floor(spec.tiles)) : 0
    readonly property real rowHeight: ((hasSub ? 44 : 32) + (hasBar ? 10 : 0)) * sc
    readonly property real headHeight: 26 * sc
    readonly property bool motionOk: !Bridge.reducedEffects && visible && (!host || host.live)

    // Minute courante (Time.date évolue à la minute) : les temps relatifs se recalculent sans minuteur propre.
    readonly property double nowMs: Time.date.getTime()

    function field(item, name): string {
        if (!name)
            return "";
        if (String(name).indexOf("{{") >= 0)
            return Bridge.winjets.renderText(name, Object.assign({}, ctx, {
                item: item
            }));
        const v = item !== null && typeof item === "object" ? item[name] : item;
        return v === undefined || v === null || typeof v === "object" ? "" : String(v);
    }

    function relativeText(item): string {
        if (!hasRelative || item === null || typeof item !== "object")
            return "";
        const raw = item[spec.relative];
        const at = typeof raw === "number" ? raw * 1000 : Date.parse(String(raw ?? ""));
        if (!isFinite(at) || at <= 0)
            return "";
        const diff = at - nowMs;
        const min = Math.floor(Math.abs(diff) / 60000);
        if (diff >= 0) {
            // À venir : seulement le jour même.
            if (new Date(at).toDateString() !== new Date(nowMs).toDateString())
                return "";
            if (min < 1)
                return qsTr("maintenant");
            return min < 60 ? qsTr("dans %1 min").arg(min) : qsTr("dans %1 h").arg(Math.floor(min / 60));
        }
        if (!pastToo)
            return "";
        if (min < 1)
            return qsTr("à l'instant");
        if (min < 60)
            return qsTr("il y a %1 min").arg(min);
        if (min < 1440)
            return qsTr("il y a %1 h").arg(Math.floor(min / 60));
        const days = Math.floor(min / 1440);
        return days < 60 ? qsTr("il y a %1 j").arg(days) : qsTr("il y a %1 mois").arg(Math.floor(days / 30));
    }

    // Plan de pose : en-têtes de groupe et lignes (ou tuiles) qui tiennent dans la hauteur disponible.
    readonly property var plan: {
        const out = [];
        const n = Math.min(limit, items.length);
        if (tileCols > 0) {
            const gap = 8 * sc;
            const cols = tileCols;
            const rws = Math.max(1, Math.ceil(n / cols));
            const w = (width - gap * (cols - 1)) / cols;
            const h = Math.max(24 * sc, (height - gap * (rws - 1)) / rws);
            for (let i = 0; i < n; i++)
                out.push({
                    head: false,
                    idx: i,
                    x: (i % cols) * (w + gap),
                    y: Math.floor(i / cols) * (h + gap),
                    w: w,
                    h: h
                });
            return out;
        }
        let y = 0;
        let last = null;
        for (let i = 0; i < n; i++) {
            const g = hasGroup ? field(items[i], spec.group) : null;
            const head = hasGroup && g !== last && g !== "";
            if (y + (head ? headHeight : 0) + rowHeight > height + 0.5)
                break;
            if (head) {
                out.push({
                    head: true,
                    text: g,
                    idx: i,
                    x: 0,
                    y: y,
                    w: width,
                    h: headHeight
                });
                y += headHeight;
            }
            last = g;
            out.push({
                head: false,
                idx: i,
                x: 0,
                y: y,
                w: width,
                h: rowHeight
            });
            y += rowHeight;
        }
        return out;
    }

    // Empreinte du contenu affiché (sans le temps relatif ni la géométrie) : change seulement si les données changent vraiment.
    readonly property string signature: {
        const parts = [];
        for (const e of plan) {
            const it = items[e.idx];
            parts.push(e.head ? "#" + e.text : [field(it, spec.title), field(it, spec.subtitle), field(it, spec.trailing),
                field(it, spec.icon), field(it, spec.bar)].join("|"));
        }
        return parts.join("\n");
    }
    property int generation: 0
    property bool settling: false
    onSignatureChanged: {
        if (!motionOk || signature === "")
            return;
        settling = true;
        generation++;
        settleTimer.restart();
    }
    // Coup unique : ne tourne que juste après un changement de contenu.
    Timer {
        id: settleTimer

        interval: 600
        repeat: false
        onTriggered: block.settling = false
    }

    implicitWidth: 200 * sc
    implicitHeight: tileCols > 0
        ? 72 * sc * Math.max(1, Math.ceil(Math.min(limit, items.length) / tileCols))
        : rowHeight * Math.min(limit, Math.max(1, items.length))
    clip: true

    // Action par ligne : un champ `action` qui cite `item.` s'applique à la ligne cliquée (gabarits résolus avec l'élément).
    readonly property bool perItem: !!spec
        && !!spec.action
        && typeof spec.action === "object"
        && JSON.stringify(spec.action).indexOf("item.") >= 0

    function rowAction(entry): var {
        const out = {};
        for (const k in spec.action)
            out[k] = typeof spec.action[k] === "string" ? Bridge.winjets.renderText(spec.action[k], Object.assign({}, ctx, {
                item: entry
            })) : spec.action[k];
        return out;
    }

    Repeater {
        model: block.plan.length

        Item {
            id: cell

            required property int index
            readonly property var place: block.plan[index] ?? ({})
            readonly property bool head: place.head === true
            readonly property var entry: place.head === true || place.idx === undefined ? null : block.items[place.idx]
            readonly property int order: place.idx ?? 0
            readonly property bool tile: block.tileCols > 0
            readonly property string relative: block.relativeText(entry)

            x: place.x ?? 0
            y: place.y ?? 0
            width: place.w ?? block.width
            height: place.h ?? 0

            Component.onCompleted: if (block.settling)
                appear.restart()

            Connections {
                target: block

                function onGenerationChanged(): void {
                    appear.restart();
                }
            }

            // Apparition décalée de 30 ms par ligne : fondu et léger glissement.
            SequentialAnimation {
                id: appear

                PropertyAction {
                    target: cell
                    property: "opacity"
                    value: 0
                }
                PauseAnimation {
                    duration: Bridge.reducedEffects ? 0 : Math.min(cell.index, 12) * 30
                }
                ParallelAnimation {
                    NumberAnimation {
                        target: cell
                        property: "opacity"
                        to: 1
                        duration: WinjetTheme.motionMedium || 220
                        easing.type: Easing.OutCubic
                    }
                    NumberAnimation {
                        target: shift
                        property: "y"
                        from: 6 * block.sc
                        to: 0
                        duration: WinjetTheme.motionMedium || 220
                        easing.type: Easing.OutCubic
                    }
                }
            }
            transform: Translate {
                id: shift
            }

            // En-tête de groupe (« Aujourd'hui », « Demain »...).
            StyledText {
                visible: cell.head
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.bottom: parent.bottom
                anchors.bottomMargin: 4 * block.sc
                text: cell.place.text ?? ""
                color: Qt.alpha(WinjetTheme.textPrimary, 0.6)
                font.pointSize: (Math.round(11 * block.sc)) * 0.75
                font.weight: Font.DemiBold
                font.capitalization: Font.AllUppercase
                font.letterSpacing: 1.0
                elide: Text.ElideRight
            }

            // Ligne : barre d'accent, pastille d'icône, titre / sous-titre / jauge, temps relatif ou valeur à droite.
            RowLayout {
                visible: !cell.head && !cell.tile
                anchors.fill: parent
                anchors.topMargin: 2 * block.sc
                anchors.bottomMargin: 2 * block.sc
                spacing: 10 * block.sc

                Rectangle {
                    visible: block.accentBars
                    Layout.preferredWidth: 3 * block.sc
                    Layout.fillHeight: true
                    Layout.topMargin: 3 * block.sc
                    Layout.bottomMargin: 3 * block.sc
                    radius: width / 2
                    color: WinjetTheme.accent
                    opacity: [1, 0.78, 0.58, 0.42, 0.32][Math.min(cell.order, 4)]
                }
                Rectangle {
                    visible: block.hasIcon
                    Layout.preferredWidth: 32 * block.sc
                    Layout.preferredHeight: 32 * block.sc
                    Layout.alignment: Qt.AlignVCenter
                    radius: 10 * block.sc
                    color: Qt.alpha(WinjetTheme.accent, 0.16)

                    MaterialIcon {
                        anchors.centerIn: parent
                        text: block.field(cell.entry, block.spec.icon)
                        color: WinjetTheme.accent
                        font.pointSize: Math.max(8, 15 * block.sc)
                    }
                }
                ColumnLayout {
                    Layout.fillWidth: true
                    Layout.alignment: Qt.AlignVCenter
                    spacing: 0

                    StyledText {
                        Layout.fillWidth: true
                        text: block.field(cell.entry, block.spec.title)
                        color: WinjetTheme.textPrimary
                        font.pointSize: (Math.round(14 * block.sc)) * 0.75
                        font.weight: Font.DemiBold
                        elide: Text.ElideRight
                    }
                    StyledText {
                        Layout.fillWidth: true
                        visible: block.hasSub
                        // Un lieu vide laisserait « 10:00 · » : le séparateur final est retiré.
                        text: block.field(cell.entry, block.spec.subtitle).replace(/\s*·\s*$/, "")
                        color: Qt.alpha(WinjetTheme.textPrimary, 0.6)
                        font.pointSize: (Math.round(12 * block.sc)) * 0.75
                        elide: Text.ElideRight
                    }
                    Rectangle {
                        Layout.fillWidth: true
                        Layout.topMargin: Math.round(3 * block.sc)
                        Layout.preferredHeight: Math.max(2, Math.round(4 * block.sc))
                        visible: block.hasBar
                        radius: height / 2
                        color: WinjetTheme.track

                        Rectangle {
                            width: parent.width * Math.max(0, Math.min(1, Number(block.field(cell.entry, block.spec.bar)) / 100))
                            height: parent.height
                            radius: parent.radius
                            color: WinjetTheme.accent

                            Behavior on width {
                                enabled: !Bridge.reducedEffects
                                NumberAnimation {
                                    duration: WinjetTheme.motionSlow || 450
                                    easing.type: Easing.OutCubic
                                }
                            }
                        }
                    }
                }

                StyledText {
                    readonly property bool useRelative: cell.relative !== ""

                    Layout.alignment: Qt.AlignVCenter
                    visible: useRelative || !!block.spec.trailing
                    text: useRelative ? cell.relative : block.field(cell.entry, block.spec.trailing)
                    color: useRelative ? WinjetTheme.accent : Qt.alpha(WinjetTheme.textPrimary, 0.6)
                    font.pointSize: (Math.round(12 * block.sc)) * 0.75
                    font.weight: useRelative ? Font.DemiBold : Font.Normal
                }
            }

            // Tuile d'icône : pastille arrondie à liseré fin, glyphe d'accent et nom dessous.
            Rectangle {
                id: tileBody

                visible: cell.tile && !cell.head
                anchors.fill: parent
                radius: Math.min(18 * block.sc, height / 3)
                color: Qt.alpha(WinjetTheme.textPrimary, tap.containsMouse ? 0.11 : 0.06)
                border.width: 1
                border.color: Qt.alpha(WinjetTheme.textPrimary, tap.containsMouse ? 0.22 : 0.12)

                Behavior on color {
                    enabled: !Bridge.reducedEffects
                    ColorAnimation {
                        duration: WinjetTheme.motionFast || 160
                    }
                }
                Behavior on border.color {
                    enabled: !Bridge.reducedEffects
                    ColorAnimation {
                        duration: WinjetTheme.motionFast || 160
                    }
                }

                Column {
                    anchors.centerIn: parent
                    width: parent.width - 8 * block.sc
                    spacing: 4 * block.sc

                    MaterialIcon {
                        anchors.horizontalCenter: parent.horizontalCenter
                        text: block.field(cell.entry, block.spec.icon) || "folder"
                        color: WinjetTheme.accent
                        font.pointSize: Math.max(8, Math.min(cell.height * 0.3, 30 * block.sc))
                    }
                    StyledText {
                        width: parent.width
                        horizontalAlignment: Text.AlignHCenter
                        text: block.field(cell.entry, block.spec.title)
                        color: WinjetTheme.textPrimary
                        font.pointSize: (Math.round(12 * block.sc)) * 0.75
                        font.weight: Font.DemiBold
                        elide: Text.ElideRight
                    }
                }
            }

            MouseArea {
                id: tap

                anchors.fill: parent
                enabled: block.perItem && !cell.head && !(block.host && block.host.editing)
                visible: block.perItem && !cell.head
                hoverEnabled: true
                cursorShape: Qt.PointingHandCursor
                onClicked: if (block.host)
                    block.host.runAction(block.rowAction(cell.entry))
            }
        }
    }

    ActionArea {
        spec: block.perItem ? null : block.spec
        host: block.host
    }
}
