import QtQuick
import qs.components
import qs.config
import ".."

// Bloc `text` : style (title|label|body|caption|display|mono|icon), tone (primary|secondary|accent|signed), align, maxLines.
// `icon` : le texte est un nom de Material Symbols (ex. `sunny`). `signed` : pastille colorée selon le signe du nombre affiché
// (accent si positif, rôle `negative` sinon), « + » ajouté devant un nombre positif.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property string style: spec && spec.style ? spec.style : "body"
    readonly property string tone: {
        if (spec && spec.tone)
            return spec.tone;
        if (style === "label" || style === "caption")
            return "secondary";
        return style === "eyebrow" ? "accent" : "primary";
    }
    readonly property int lines: spec && spec.maxLines >= 1 ? Math.floor(spec.maxLines) : 1
    readonly property string rendered: spec ? Bridge.winjets.renderText(spec.text ?? "", ctx) : ""
    readonly property bool signed: tone === "signed"
    readonly property bool negative: signed && rendered.trim().charAt(0) === "-"
    readonly property string content: signed && /^[0-9]/.test(rendered.trim()) ? "+" + rendered.trim() : rendered
    // Un seul accent : le « signe moins » passe en onSurface à 0,5 (aucune teinte rouge/verte).
    readonly property color toneColor: {
        if (signed)
            return negative ? Qt.alpha(WinjetTheme.textPrimary, 0.5) : WinjetTheme.accent;
        if (tone === "accent")
            return WinjetTheme.accent;
        if (tone === "faint")
            return Qt.alpha(WinjetTheme.textPrimary, 0.45);
        return tone === "secondary" ? Qt.alpha(WinjetTheme.textPrimary, 0.6) : WinjetTheme.textPrimary;
    }

    // Hiérarchie : display (chiffres légers), eyebrow (accent, capitales), title (gras), body 14 px / caption 12 px.
    readonly property real px: {
        switch (block.style) {
        case "display":
            return 84;
        case "icon":
            return 56;
        case "glyph":
            return 20;
        case "title":
            return 26;
        case "eyebrow":
            return 12;
        case "label":
            return 11;
        case "caption":
            return 12;
        default:
            return 14;
        }
    }
    readonly property int weight: {
        switch (block.style) {
        case "display":
            return Font.Light;
        case "title":
            return Font.Bold;
        case "eyebrow":
            return Font.DemiBold;
        case "body":
            return tone === "secondary" || tone === "faint" ? Font.Normal : Font.DemiBold;
        case "label":
            return Font.DemiBold;
        default:
            return Font.Normal;
        }
    }
    readonly property bool motionOk: !Bridge.reducedEffects && block.visible && (!block.host || block.host.live)
    readonly property int dMedium: Bridge.reducedEffects ? 0 : (WinjetTheme.motionMedium || 220)

    // Première famille à chasse fixe réellement installée (Appearance.font.family.mono n'est pas garantie sur la machine).
    readonly property string monoFamily: {
        const all = Qt.fontFamilies();
        for (const f of [Appearance.font.family.mono, "Cascadia Mono", "Consolas", "Courier New"]) {
            if (all.indexOf(f) >= 0)
                return f;
        }
        return "monospace";
    }

    readonly property real inset: signed ? 8 * sc : 0

    implicitWidth: label.implicitWidth + inset * 2
    implicitHeight: label.implicitHeight + (signed ? 2 * sc : 0)

    Rectangle {
        visible: block.signed
        anchors.fill: parent
        radius: height / 2
        color: Qt.alpha(block.negative ? WinjetTheme.textPrimary : WinjetTheme.accent, block.negative ? 0.08 : 0.16)
    }

    StyledText {
        id: label

        anchors.fill: parent
        anchors.leftMargin: block.inset
        anchors.rightMargin: block.inset
        text: block.content
        color: block.toneColor
        opacity: 1
        transform: Translate {
            id: slide
        }
        font.pointSize: (Math.max(1, Math.round(block.px * block.sc))) * 0.75
        font.weight: block.weight
        font.family: {
            if (block.style === "mono")
                return block.monoFamily;
            return block.style === "icon" || block.style === "glyph" ? Appearance.font.family.material : Appearance.font.family.sans;
        }
        font.capitalization: block.style === "label" || block.style === "eyebrow" ? Font.AllUppercase : Font.MixedCase
        font.letterSpacing: {
            if (block.style === "eyebrow")
                return 1.2;
            if (block.style === "label")
                return 0.8;
            return block.style === "display" ? -0.025 * block.px * block.sc : 0;
        }
        horizontalAlignment: {
            if (block.signed || (block.spec && block.spec.align === "center"))
                return Text.AlignHCenter;
            return block.spec && block.spec.align === "end" ? Text.AlignRight : Text.AlignLeft;
        }
        verticalAlignment: Text.AlignVCenter
        wrapMode: block.lines > 1 ? Text.WordWrap : Text.NoWrap
        maximumLineCount: block.lines
        elide: Text.ElideRight
        fontSizeMode: block.style === "display" ? Text.Fit : Text.FixedSize
        minimumPixelSize: 14 * block.sc
    }

    // Fondu enchaîné au changement de texte : l'ancien texte s'efface en glissant de 4 px, le nouveau arrive en sens inverse.
    property string previous: ""
    onContentChanged: {
        if (motionOk && previous !== "" && content !== previous) {
            ghost.text = previous;
            swap.restart();
        }
        previous = content;
    }
    Component.onCompleted: previous = content

    StyledText {
        id: ghost

        anchors.fill: label
        visible: opacity > 0
        opacity: 0
        color: label.color
        font: label.font
        horizontalAlignment: label.horizontalAlignment
        verticalAlignment: label.verticalAlignment
        elide: label.elide
        wrapMode: label.wrapMode
        maximumLineCount: label.maximumLineCount
        fontSizeMode: label.fontSizeMode
        minimumPixelSize: label.minimumPixelSize
        transform: Translate {
            id: ghostSlide
        }
    }

    ParallelAnimation {
        id: swap

        NumberAnimation {
            target: label
            property: "opacity"
            from: 0
            to: 1
            duration: block.dMedium
            easing.type: Easing.OutCubic
        }
        NumberAnimation {
            target: slide
            property: "y"
            from: 4 * block.sc
            to: 0
            duration: block.dMedium
            easing.type: Easing.OutCubic
        }
        NumberAnimation {
            target: ghost
            property: "opacity"
            from: 1
            to: 0
            duration: block.dMedium
            easing.type: Easing.OutCubic
        }
        NumberAnimation {
            target: ghostSlide
            property: "y"
            from: 0
            to: -4 * block.sc
            duration: block.dMedium
            easing.type: Easing.OutCubic
        }
    }

    ActionArea {
        spec: block.spec
        host: block.host
    }
}
