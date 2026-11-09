// Carte média de la maquette.
// Extrait de ThemePreviewMock.qml (pur découpage) : `mock` désigne l'élément parent.
import QtQuick
import QtQuick.Shapes
import qs.components
import qs.config
import qs.services

Rectangle {
    required property var mock
    objectName: "previewMedia"
    x: 580
    y: 66
    width: 304
    height: 372
    radius: Appearance.rounding.large * 2
    color: mock.p.m3surfaceContainer

    Item {
        id: cover

        anchors.horizontalCenter: parent.horizontalCenter
        y: 28
        width: 176
        height: 176

        Rectangle {
            anchors.fill: parent
            anchors.margins: 12
            radius: width / 2
            color: mock.p.m3surfaceContainerHigh

            MaterialIcon {
                renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                anchors.centerIn: parent
                text: "art_track"
                color: mock.p.m3onSurfaceVariant
                font.pointSize: Appearance.font.size.extraLarge * 1.6
            }
        }

        Shape {
            anchors.fill: parent
            preferredRendererType: Shape.CurveRenderer

            ShapePath {
                fillColor: "transparent"
                strokeColor: mock.p.m3surfaceContainerHigh
                strokeWidth: 6
                PathAngleArc {
                    centerX: 88
                    centerY: 88
                    radiusX: 84
                    radiusY: 84
                    startAngle: 0
                    sweepAngle: 359.9
                }
            }

            ShapePath {
                fillColor: "transparent"
                strokeColor: mock.p.m3primary
                strokeWidth: 6
                capStyle: ShapePath.RoundCap
                PathAngleArc {
                    centerX: 88
                    centerY: 88
                    radiusX: 84
                    radiusY: 84
                    startAngle: -90
                    sweepAngle: 140
                }
            }
        }
    }

    Column {
        y: 224
        width: parent.width - 40
        anchors.horizontalCenter: parent.horizontalCenter
        spacing: 3

        StyledText {
            renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
            width: parent.width
            horizontalAlignment: Text.AlignHCenter
            elide: Text.ElideRight
            text: qsTr("Titre du morceau")
            color: mock.p.m3primary
            font.pointSize: Appearance.font.size.larger
            font.weight: Font.Medium
        }

        StyledText {
            renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
            width: parent.width
            horizontalAlignment: Text.AlignHCenter
            elide: Text.ElideRight
            text: qsTr("Album")
            color: mock.p.m3outline
            font.pointSize: Appearance.font.size.small
        }

        StyledText {
            renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
            width: parent.width
            horizontalAlignment: Text.AlignHCenter
            elide: Text.ElideRight
            text: qsTr("Artiste")
            color: mock.p.m3secondary
            font.pointSize: Appearance.font.size.normal
        }
    }

    Row {
        anchors.horizontalCenter: parent.horizontalCenter
        y: 312
        spacing: 22

        Repeater {
            model: ["skip_previous", "pause", "skip_next"]

            MaterialIcon {
                renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                required property string modelData

                text: modelData
                color: mock.p.m3onSurface
                font.pointSize: Appearance.font.size.extraLarge
            }
        }
    }
}
