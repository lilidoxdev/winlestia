import QtQuick
import QtQuick.Layouts
import qs.components
import qs.config
import ".."

// Bloc `list` : `items` (tableau), title/subtitle/trailing = noms de champs de l'élément (ou gabarits avec `item`), max.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property var items: {
        const v = spec ? Bridge.winjets.resolve(spec.items, ctx) : null;
        return Array.isArray(v) ? v : [];
    }
    readonly property int limit: spec && spec.max >= 1 ? Math.floor(spec.max) : 5
    readonly property bool hasSub: !!spec && !!spec.subtitle
    readonly property real rowHeight: (hasSub ? 40 : 26) * sc
    readonly property int shown: Math.max(0, Math.min(limit, items.length, Math.floor(height / rowHeight)))

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

    implicitWidth: 200 * sc
    implicitHeight: rowHeight * Math.min(limit, Math.max(1, items.length))
    clip: true

    Column {
        anchors.fill: parent

        Repeater {
            model: block.shown

            RowLayout {
                id: row

                required property int index
                readonly property var entry: block.items[index]

                width: parent.width
                height: block.rowHeight
                spacing: Appearance.spacing.small * block.sc

                ColumnLayout {
                    Layout.fillWidth: true
                    spacing: 0

                    StyledText {
                        Layout.fillWidth: true
                        text: block.field(row.entry, block.spec.title)
                        color: WinjetTheme.textPrimary
                        font.pointSize: (Appearance.font.size.normal) * block.sc
                        elide: Text.ElideRight
                    }
                    StyledText {
                        Layout.fillWidth: true
                        visible: block.hasSub
                        text: block.field(row.entry, block.spec.subtitle)
                        color: WinjetTheme.textSecondary
                        font.pointSize: (Appearance.font.size.small) * block.sc
                        elide: Text.ElideRight
                    }
                }

                StyledText {
                    visible: !!block.spec.trailing
                    text: block.field(row.entry, block.spec.trailing)
                    color: WinjetTheme.textSecondary
                    font.pointSize: (Appearance.font.size.normal) * block.sc
                }
            }
        }
    }

    ActionArea {
        spec: block.spec
        host: block.host
    }
}
