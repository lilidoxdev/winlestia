import QtQuick
import qs.components
import qs.services
import qs.config

// Une combinaison dessinée en touches de clavier : « Ctrl+F2 » -> [Ctrl] [F2]. Noms de touches traduits, « Ctrl » reste « Ctrl ».
Flow {
    id: caps

    property string combo
    readonly property var parts: {
        const raw = String(combo ?? "");
        if (raw === "")
            return [];
        const list = raw.endsWith("+") ? raw.slice(0, -1).split("+").filter(p => p !== "").concat(["+"]) : raw.split("+");
        return list.map(p => p.trim()).filter(p => p !== "");
    }
    function label(part: string): string {
        if (part === "Backspace")
            return qsTr("Retour arrière");
        if (part === "Space")
            return qsTr("Espace");
        if (part === "Esc")
            return qsTr("Échap");
        return qsTr(part);
    }

    spacing: 5

    Repeater {
        model: caps.parts

        StyledRect {
            id: cap

            required property string modelData

            implicitWidth: Math.max(28, text.implicitWidth + Appearance.padding.normal * 2)
            implicitHeight: 28
            radius: Appearance.rounding.smaller
            color: Qt.alpha(Colours.palette.m3surfaceContainerHighest, 0.95)
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
                color: Qt.alpha(Colours.palette.m3outline, 0.55)
            }

            StyledText {
                id: text

                anchors.centerIn: parent
                text: caps.label(cap.modelData)
                font.pointSize: Appearance.font.size.small
                font.weight: Font.Medium
                color: Colours.palette.m3onSurface
            }
        }
    }
}
