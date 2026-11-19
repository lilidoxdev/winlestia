// Carte horloge de la maquette.
// Extrait de ThemePreviewMock.qml (pur découpage) : `mock` désigne l'élément parent.
import QtQuick
import QtQuick.Shapes
import qs.components
import qs.config
import qs.services

Rectangle {
    required property var mock
    x: 16
    y: 246
    width: 84
    height: 192
    radius: Appearance.rounding.normal
    color: mock.p.m3surfaceContainer

    Column {
        anchors.centerIn: parent
        spacing: 2

        StyledText {
            renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
            anchors.horizontalCenter: parent.horizontalCenter
            text: "10"
            color: mock.p.m3secondary
            font.pointSize: Appearance.font.size.extraLarge
            font.weight: Font.Medium
        }

        StyledText {
            renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
            anchors.horizontalCenter: parent.horizontalCenter
            text: "•••"
            color: mock.p.m3primary
            font.pointSize: Appearance.font.size.small
        }

        StyledText {
            renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
            anchors.horizontalCenter: parent.horizontalCenter
            text: "42"
            color: mock.p.m3secondary
            font.pointSize: Appearance.font.size.extraLarge
            font.weight: Font.Medium
        }

        StyledText {
            renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
            anchors.horizontalCenter: parent.horizontalCenter
            text: "AM"
            color: mock.p.m3primary
            font.pointSize: Appearance.font.size.small
        }
    }
}
