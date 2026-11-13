import QtQuick
import qs.components
import qs.config
import ".."

// Bloc `text` : style (title|label|body|caption|display|mono), tone, align, maxLines.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property string style: spec && spec.style ? spec.style : "body"
    readonly property string tone: spec && spec.tone ? spec.tone : (style === "label" || style === "caption" ? "secondary" : "primary")
    readonly property int lines: spec && spec.maxLines >= 1 ? Math.floor(spec.maxLines) : 1
    readonly property string content: spec ? Bridge.winjets.renderText(spec.text ?? "", ctx) : ""

    readonly property real basePt: {
        switch (block.style) {
        case "display":
            return Appearance.font.size.extraLarge + 8;
        case "title":
            return Appearance.font.size.larger;
        case "label":
        case "caption":
            return Appearance.font.size.small;
        default:
            return Appearance.font.size.normal;
        }
    }

    // Première famille à chasse fixe réellement installée (Appearance.font.family.mono n'est pas garantie sur la machine).
    readonly property string monoFamily: {
        const all = Qt.fontFamilies();
        for (const f of [Appearance.font.family.mono, "Cascadia Mono", "Consolas", "Courier New"]) {
            if (all.indexOf(f) >= 0)
                return f;
        }
        return "monospace";
    }

    implicitWidth: label.implicitWidth
    implicitHeight: label.implicitHeight

    StyledText {
        id: label

        anchors.fill: parent
        text: block.content
        color: block.tone === "accent" ? WinjetTheme.accent : block.tone === "secondary" ? WinjetTheme.textSecondary : WinjetTheme.textPrimary
        font.pointSize: block.basePt * block.sc
        font.weight: block.style === "title" || block.style === "display" ? Font.Medium : Font.Normal
        font.family: block.style === "mono" ? block.monoFamily : Appearance.font.family.sans
        font.capitalization: block.style === "label" ? Font.AllUppercase : Font.MixedCase
        font.letterSpacing: block.style === "label" ? 0.8 : 0
        horizontalAlignment: block.spec && block.spec.align === "center" ? Text.AlignHCenter : block.spec && block.spec.align === "end" ? Text.AlignRight : Text.AlignLeft
        verticalAlignment: Text.AlignVCenter
        wrapMode: block.lines > 1 ? Text.WordWrap : Text.NoWrap
        maximumLineCount: block.lines
        elide: Text.ElideRight
        fontSizeMode: block.style === "display" ? Text.HorizontalFit : Text.FixedSize
        minimumPixelSize: 14 * block.sc
    }

    ActionArea {
        spec: block.spec
        host: block.host
    }
}
