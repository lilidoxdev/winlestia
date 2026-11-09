import QtQuick
import QtQuick.Layouts
import qs.components
import qs.config
import qs.services
import ".."

// Bloc `stat` : label discret, grande valeur, delta signé coloré (positif / négatif de la palette).
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null

    readonly property var rawDelta: spec && spec.delta !== undefined ? Bridge.winjets.resolve(spec.delta, ctx) : null
    readonly property real deltaNumber: {
        const v = rawDelta;
        if (typeof v === "number")
            return v;
        if (typeof v === "string") {
            const n = parseFloat(v.replace(",", ".").replace(/[^0-9+\-.]/g, ""));
            return isNaN(n) ? 0 : (v.trim().charAt(0) === "-" ? -Math.abs(n) : n);
        }
        return 0;
    }
    readonly property string deltaText: {
        const v = rawDelta;
        if (v === null || v === undefined || v === "")
            return "";
        if (typeof v === "number")
            return (v > 0 ? "+" : "") + v.toFixed(1).replace(".", ",") + " %";
        return String(v);
    }

    implicitWidth: col.implicitWidth
    implicitHeight: col.implicitHeight

    ColumnLayout {
        id: col

        anchors.fill: parent
        spacing: 2

        StyledText {
            Layout.fillWidth: true
            visible: !!block.spec && !!block.spec.label
            text: block.spec && block.spec.label ? Bridge.winjets.renderText(block.spec.label, block.ctx) : ""
            color: WinjetTheme.textSecondary
            font.pointSize: Appearance.font.size.small
            elide: Text.ElideRight
        }
        StyledText {
            Layout.fillWidth: true
            text: block.spec ? Bridge.winjets.renderText(block.spec.value ?? "", block.ctx) : ""
            color: WinjetTheme.textPrimary
            font.pointSize: Appearance.font.size.extraLarge + 4
            font.weight: Font.Medium
            fontSizeMode: Text.HorizontalFit
            minimumPixelSize: 14
        }
        RowLayout {
            visible: block.deltaText !== ""
            spacing: 4

            MaterialIcon {
                text: block.deltaNumber < 0 ? "trending_down" : "trending_up"
                color: block.deltaNumber < 0 ? WinjetTheme.role("negative") : WinjetTheme.role("positive")
                font.pointSize: Appearance.font.size.normal
            }
            StyledText {
                text: block.deltaText
                color: block.deltaNumber < 0 ? WinjetTheme.role("negative") : WinjetTheme.role("positive")
                font.pointSize: Appearance.font.size.small
            }
        }
    }

    ActionArea {
        spec: block.spec
        host: block.host
    }
}
