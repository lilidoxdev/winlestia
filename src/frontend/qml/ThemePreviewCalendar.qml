// Carte calendrier de la maquette.
// Extrait de ThemePreviewMock.qml (pur découpage) : `mock` désigne l'élément parent.
import QtQuick
import QtQuick.Shapes
import qs.components
import qs.config
import qs.services

Rectangle {
    required property var mock
    objectName: "previewCalendar"
    x: 108
    y: 246
    width: 288
    height: 192
    radius: Appearance.rounding.large
    color: mock.p.m3surfaceContainer

    MaterialIcon {
        renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
        x: 10
        y: 6
        text: "chevron_left"
        color: mock.p.m3tertiary
        font.pointSize: Appearance.font.size.larger
    }

    StyledText {
        renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
        y: 8
        width: parent.width
        horizontalAlignment: Text.AlignHCenter
        text: mock.loc.toString(new Date(), "MMMM yyyy")
        color: mock.p.m3primary
        font.pointSize: Appearance.font.size.normal
        font.weight: Font.Medium
    }

    MaterialIcon {
        renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
        x: parent.width - width - 10
        y: 6
        text: "chevron_right"
        color: mock.p.m3tertiary
        font.pointSize: Appearance.font.size.larger
    }

    Grid {
        x: 8
        y: 36
        columns: 7
        columnSpacing: 0
        rowSpacing: 0

        Repeater {
            model: 7

            Item {
                id: dn

                required property int index

                width: 38
                height: 20

                StyledText {
                    renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                    anchors.centerIn: parent
                    text: mock.loc.dayName((dn.index + 1) % 7, Locale.NarrowFormat)
                    color: dn.index >= 5 ? mock.p.m3secondary : mock.p.m3onSurfaceVariant
                    font.pointSize: Appearance.font.size.small
                }
            }
        }

        Repeater {
            model: mock.cells

            Item {
                id: cell

                required property int modelData
                required property int index
                readonly property bool isToday: modelData === mock.today

                width: 38
                height: 24

                Rectangle {
                    objectName: cell.isToday ? "previewToday" : ""
                    anchors.centerIn: parent
                    width: 24
                    height: 22
                    radius: Appearance.rounding.smaller
                    visible: cell.isToday
                    color: mock.p.m3primary
                }

                StyledText {
                    renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                    anchors.centerIn: parent
                    visible: cell.modelData > 0
                    text: cell.modelData
                    color: cell.isToday ? mock.p.m3onPrimary : cell.index % 7 >= 5 ? mock.p.m3secondary : mock.p.m3onSurfaceVariant
                    font.pointSize: Appearance.font.size.small
                }
            }
        }
    }
}
