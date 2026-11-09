// Carte utilisateur et système de la maquette.
// Extrait de ThemePreviewMock.qml (pur découpage) : `mock` désigne l'élément parent.
import QtQuick
import QtQuick.Shapes
import qs.components
import qs.config
import qs.services

Rectangle {
    required property var mock
    x: 244
    y: 66
    width: 328
    height: 172
    radius: Appearance.rounding.large
    color: mock.p.m3surfaceContainer

    Rectangle {
        x: 16
        y: 16
        width: 140
        height: 140
        radius: Appearance.rounding.normal
        color: mock.p.m3surfaceContainerHigh

        MaterialIcon {
            renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
            anchors.centerIn: parent
            text: "person"
            color: mock.p.m3onSurfaceVariant
            font.pointSize: Appearance.font.size.extraLarge * 2
        }

        Rectangle {
            anchors.right: parent.right
            anchors.bottom: parent.bottom
            anchors.margins: 8
            width: 30
            height: 30
            radius: 15
            color: mock.p.m3primary

            MaterialIcon {
                renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                anchors.centerIn: parent
                text: "frame_person"
                color: mock.p.m3onPrimary
                font.pointSize: Appearance.font.size.normal
            }
        }
    }

    Column {
        x: 172
        anchors.verticalCenter: parent.verticalCenter
        spacing: 12

        Repeater {
            model: [
                {
                    icon: "desktop_windows",
                    text: "Windows 11",
                    tone: mock.p.m3primary
                },
                {
                    icon: "person",
                    text: "lilid",
                    tone: mock.p.m3primary
                },
                {
                    icon: "memory",
                    text: "Winlestia",
                    tone: mock.p.m3secondary
                },
                {
                    icon: "timer",
                    text: qsTr("actif depuis 3 h"),
                    tone: mock.p.m3tertiary
                }
            ]

            Row {
                id: info

                required property var modelData

                spacing: 8

                MaterialIcon {
                    renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                    anchors.verticalCenter: parent.verticalCenter
                    text: info.modelData.icon
                    color: info.modelData.tone
                    font.pointSize: Appearance.font.size.larger
                }

                StyledText {
                    renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                    anchors.verticalCenter: parent.verticalCenter
                    text: info.modelData.text
                    color: mock.p.m3onSurface
                    font.pointSize: Appearance.font.size.normal
                }
            }
        }
    }
}
