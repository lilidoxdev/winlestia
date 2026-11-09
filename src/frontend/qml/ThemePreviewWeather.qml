// Carte météo de la maquette du tableau de bord.
// Extrait de ThemePreviewMock.qml (pur découpage) : `mock` désigne l'élément parent.
import QtQuick
import QtQuick.Shapes
import qs.components
import qs.config
import qs.services

Rectangle {
    required property var mock
    objectName: "previewWeather"
    x: 16
    y: 66
    width: 220
    height: 172
    radius: Appearance.rounding.large * 1.5
    color: mock.p.m3surfaceContainer

    Column {
        x: 16
        y: 12
        width: parent.width - 32
        spacing: 2

        StyledText {
            renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
            text: "Paris"
            color: mock.p.m3onSurface
            font.pointSize: Appearance.font.size.larger
            font.weight: Font.Medium
        }

        StyledText {
            renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
            text: mock.loc.toString(new Date(), "dddd d MMMM")
            color: mock.p.m3onSurfaceVariant
            font.pointSize: Appearance.font.size.small
        }
    }

    Row {
        x: 16
        y: 64
        spacing: 10

        MaterialIcon {
            renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
            anchors.verticalCenter: parent.verticalCenter
            text: "wb_sunny"
            color: mock.p.m3secondary
            font.pointSize: Appearance.font.size.extraLarge
        }

        Column {
            anchors.verticalCenter: parent.verticalCenter

            StyledText {
                renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                text: "21°C"
                color: mock.p.m3primary
                font.pointSize: Appearance.font.size.larger
                font.weight: Font.Medium
            }

            StyledText {
                renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                text: qsTr("Ensoleillé")
                color: mock.p.m3onSurfaceVariant
                font.pointSize: Appearance.font.size.small
            }
        }
    }

    Row {
        x: 16
        y: 124
        spacing: 14

        Repeater {
            model: [
                {
                    icon: "water_drop",
                    tone: mock.p.m3tertiary,
                    v: "48%"
                },
                {
                    icon: "air",
                    tone: mock.p.m3secondary,
                    v: "12 km/h"
                },
                {
                    icon: "device_thermostat",
                    tone: mock.p.m3primary,
                    v: "19°"
                }
            ]

            Row {
                id: stat

                required property var modelData

                spacing: 3

                MaterialIcon {
                    renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                    anchors.verticalCenter: parent.verticalCenter
                    text: stat.modelData.icon
                    color: stat.modelData.tone
                    font.pointSize: Appearance.font.size.normal
                }

                StyledText {
                    renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                    anchors.verticalCenter: parent.verticalCenter
                    text: stat.modelData.v
                    color: mock.p.m3onSurface
                    font.pointSize: Appearance.font.size.small
                }
            }
        }
    }
}
