// Carte ressources (barres) de la maquette.
// Extrait de ThemePreviewMock.qml (pur découpage) : `mock` désigne l'élément parent.
import QtQuick
import QtQuick.Shapes
import qs.components
import qs.config
import qs.services

Rectangle {
    required property var mock
    x: 404
    y: 246
    width: 168
    height: 192
    radius: Appearance.rounding.normal
    color: mock.p.m3surfaceContainer

    Row {
        anchors.centerIn: parent
        spacing: 16

        Repeater {
            model: [
                {
                    icon: "memory",
                    tone: mock.p.m3primary,
                    v: 0.62
                },
                {
                    icon: "developer_board",
                    tone: mock.p.m3secondary,
                    v: 0.38
                },
                {
                    icon: "hard_drive",
                    tone: mock.p.m3tertiary,
                    v: 0.8
                }
            ]

            Column {
                id: res

                required property var modelData

                spacing: 8

                Rectangle {
                    width: 28
                    height: 120
                    radius: Appearance.rounding.smaller
                    color: mock.p.m3surfaceContainerHigh

                    Rectangle {
                        anchors.bottom: parent.bottom
                        width: parent.width
                        height: parent.height * res.modelData.v
                        radius: Appearance.rounding.smaller
                        color: res.modelData.tone
                    }
                }

                MaterialIcon {
                    renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                    anchors.horizontalCenter: parent.horizontalCenter
                    text: res.modelData.icon
                    color: res.modelData.tone
                    font.pointSize: Appearance.font.size.larger
                }
            }
        }
    }
}
