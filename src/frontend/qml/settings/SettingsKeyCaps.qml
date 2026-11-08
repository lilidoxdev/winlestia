import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config
import "."

// A key combination drawn as keycaps: "Ctrl+Alt+K" -> [Ctrl] [Alt] [K].
Flow {
    id: caps

    property string combo
    property bool muted: false
    // Affiché à la place des touches quand la combinaison est vide.
    property string emptyText: qsTr("Aucun")
    readonly property var parts: {
        const raw = String(combo ?? "");
        if (raw === "")
            return [];
        // Les combinaisons du type « Ctrl++ » gardent un plus littéral comme dernière touche.
        const out = raw.endsWith("+") ? raw.slice(0, -1).split("+").filter(p => p !== "").concat(["+"]) : raw.split("+");
        const flat = [];
        for (const part of out.map(p => p.trim()).filter(p => p !== "")) {
            // « ←↑→↓ » fait quatre touches ; la police n'a pas de glyphes de flèches, elles sont donc dessinées en icônes.
            if (/^[←↑→↓]+$/.test(part))
                part.split("").forEach(a => flat.push(a));
            else
                flat.push(part);
        }
        return flat;
    }
    readonly property var arrowIcons: ({
            "←": "arrow_back",
            "↑": "arrow_upward",
            "→": "arrow_forward",
            "↓": "arrow_downward"
        })

    spacing: 5

    StyledText {
        visible: caps.parts.length === 0
        text: caps.emptyText
        font.pointSize: Appearance.font.size.small
        color: Colours.palette.m3outline
    }

    Repeater {
        model: caps.parts

        StyledRect {
            id: cap

            required property string modelData
            readonly property bool isArrow: caps.arrowIcons[modelData] !== undefined

            implicitWidth: Math.max(28, (isArrow ? 0 : label.implicitWidth) + Appearance.padding.normal * 2)
            implicitHeight: 28
            radius: Appearance.rounding.smaller
            color: Qt.alpha(Colours.palette.m3surfaceContainerHighest, caps.muted ? 0.55 : 0.95)
            border.width: 1
            border.color: Colours.palette.m3outlineVariant

            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.bottom: parent.bottom
                anchors.leftMargin: 3
                anchors.rightMargin: 3
                height: 2
                radius: 1
                color: Qt.alpha(Colours.palette.m3outline, caps.muted ? 0.3 : 0.55)
            }

            MaterialIcon {
                anchors.centerIn: parent
                visible: cap.isArrow
                text: caps.arrowIcons[cap.modelData] ?? ""
                font.pointSize: Appearance.font.size.normal
                color: caps.muted ? Colours.palette.m3onSurfaceVariant : Colours.palette.m3onSurface
            }

            StyledText {
                id: label

                anchors.centerIn: parent
                visible: !cap.isArrow
                // Jeton d'affichage uniquement : le backend garde sa propre graphie française (« Flèches », « Maj », « Espace »...).
                text: cap.isArrow ? "" : qsTr(cap.modelData)
                font.pointSize: Appearance.font.size.small
                font.weight: Font.Medium
                color: caps.muted ? Colours.palette.m3onSurfaceVariant : Colours.palette.m3onSurface
            }
        }
    }
}
