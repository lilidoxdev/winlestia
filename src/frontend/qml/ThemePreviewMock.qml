import QtQuick
import QtQuick.Shapes
import qs.components
import qs.config
import qs.services

// Static, non-interactive mock of the dashboard (qml/qs/modules/dashboard) painted with an edited custom palette.
// Fixed design size; the host scales it. Colour roles follow the real dashboard files:
//   Tabs: current = primary, others = onSurfaceVariant, separator = outlineVariant
//   Weather: city onSurface, date onSurfaceVariant, temp primary, icon secondary, chips primary/secondary/tertiary
//   User: avatar surfaceContainerHigh, rows primary / secondary / tertiary
//   DateTime: digits secondary, separator and period primary
//   Calendar: title primary, chevrons tertiary, weekend secondary, other days onSurfaceVariant, today primary + onPrimary
//   Resources: bars primary / secondary / tertiary on surfaceContainerHigh
//   Media: ring primary, title primary, album outline, artist secondary, controls onSurface
// The five colours are mapped to m3 roles exactly like custom_palette.py::_build.
Item {
    id: mock

    // { background, text, primary, secondary, tertiary } as #rrggbb
    property var colors: ({})
    property bool glass: false
    // Alpha of the panel when glass is on (0..1).
    property real glassAlpha: 0.12
    readonly property var p: derive(colors)
    readonly property alias panel: panelBg

    function rgb(c: string): var {
        const s = String(c);
        return [parseInt(s.substr(1, 2), 16) / 255, parseInt(s.substr(3, 2), 16) / 255, parseInt(s.substr(5, 2), 16) / 255];
    }

    function hex2(v: real): string {
        const n = Math.max(0, Math.min(255, Math.round(v * 255)));
        return (n < 16 ? "0" : "") + n.toString(16);
    }

    function blend(a: string, b: string, t: real): string {
        const x = rgb(a), y = rgb(b);
        return "#" + hex2(x[0] * (1 - t) + y[0] * t) + hex2(x[1] * (1 - t) + y[1] * t) + hex2(x[2] * (1 - t) + y[2] * t);
    }

    function lum(c: string): real {
        const v = rgb(c).map(k => k <= 0.04045 ? k / 12.92 : Math.pow((k + 0.055) / 1.055, 2.4));
        return v[0] * 0.2126 + v[1] * 0.7152 + v[2] * 0.0722;
    }

    function onColor(c: string): string {
        return lum(c) < 0.179 ? "#ffffff" : "#101518";
    }

    function derive(c: var): var {
        const bg = c.background ?? "#fff8f6", tx = c.text ?? "#261916", pr = c.primary ?? "#895143", se = c.secondary ?? "#775b52", te = c.tertiary ?? "#68613c";
        return {
            m3surface: bg,
            m3onSurface: tx,
            m3primary: pr,
            m3secondary: se,
            m3tertiary: te,
            m3onPrimary: onColor(pr),
            m3onSurfaceVariant: blend(bg, tx, 0.76),
            m3outline: blend(bg, tx, 0.54),
            m3outlineVariant: blend(bg, tx, 0.2),
            m3surfaceContainer: blend(bg, tx, 0.06),
            m3surfaceContainerHigh: blend(bg, tx, 0.10)
        };
    }

    function monthCells(): var {
        const now = new Date();
        const lead = (new Date(now.getFullYear(), now.getMonth(), 1).getDay() + 6) % 7;
        const count = new Date(now.getFullYear(), now.getMonth() + 1, 0).getDate();
        const out = [];
        for (let i = 0; i < Math.ceil((lead + count) / 7) * 7; i++)
            out.push(i >= lead && i < lead + count ? i - lead + 1 : 0);
        return out;
    }

    readonly property var cells: monthCells()
    readonly property int today: new Date().getDate()
    readonly property var loc: Qt.locale(Bridge.language || "en")

    implicitWidth: 900
    implicitHeight: 454
    width: implicitWidth
    height: implicitHeight

    Rectangle {
        id: panelBg

        anchors.fill: parent
        radius: Appearance.rounding.large
        color: Qt.alpha(mock.p.m3surface, mock.glass ? mock.glassAlpha : 1)
        border.width: 1
        border.color: Qt.alpha(mock.p.m3outlineVariant, 0.6)
    }

    // Tabs
    Row {
        x: 16
        y: 10
        height: 44

        Repeater {
            model: [
                {icon: "dashboard", label: qsTr("Tableau de bord")},
                {icon: "queue_music", label: qsTr("Média")},
                {icon: "speed", label: qsTr("Performances")},
                {icon: "workspaces", label: qsTr("Espaces de travail")}
            ]

            Item {
                id: tab

                required property var modelData
                required property int index
                readonly property color tone: index === 0 ? mock.p.m3primary : mock.p.m3onSurfaceVariant

                width: tabRow.implicitWidth + 36
                height: 44

                Row {
                    id: tabRow

                    anchors.centerIn: parent
                    spacing: 8

                    MaterialIcon {
                        renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                        anchors.verticalCenter: parent.verticalCenter
                        text: tab.modelData.icon
                        color: tab.tone
                        font.pointSize: Appearance.font.size.larger
                    }

                    StyledText {
                        renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
                        anchors.verticalCenter: parent.verticalCenter
                        text: tab.modelData.label
                        color: tab.tone
                        font.pointSize: Appearance.font.size.normal
                    }
                }

                Rectangle {
                    visible: tab.index === 0
                    anchors.horizontalCenter: parent.horizontalCenter
                    anchors.bottom: parent.bottom
                    width: parent.width - 24
                    height: 3
                    radius: 1
                    color: mock.p.m3primary
                }
            }
        }
    }

    MaterialIcon {
        renderType: Text.QtRendering // scaled preview: vector glyphs stay sharp
        x: parent.width - 16 - width - 4
        y: 10 + (44 - height) / 2
        text: "settings"
        color: mock.p.m3onSurfaceVariant
        font.pointSize: Appearance.font.size.larger
    }

    Rectangle {
        x: 16
        y: 54
        width: parent.width - 32
        height: 1
        color: mock.p.m3outlineVariant
    }

    // Weather
    Rectangle {
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
                    {icon: "water_drop", tone: mock.p.m3tertiary, v: "48%"},
                    {icon: "air", tone: mock.p.m3secondary, v: "12 km/h"},
                    {icon: "device_thermostat", tone: mock.p.m3primary, v: "19°"}
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

    // User and system
    Rectangle {
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
                    {icon: "desktop_windows", text: "Windows 11", tone: mock.p.m3primary},
                    {icon: "person", text: "lilid", tone: mock.p.m3primary},
                    {icon: "memory", text: "Winlestia", tone: mock.p.m3secondary},
                    {icon: "timer", text: qsTr("actif depuis 3 h"), tone: mock.p.m3tertiary}
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

    // Clock
    Rectangle {
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

    // Calendar
    Rectangle {
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

    // Resources
    Rectangle {
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
                    {icon: "memory", tone: mock.p.m3primary, v: 0.62},
                    {icon: "developer_board", tone: mock.p.m3secondary, v: 0.38},
                    {icon: "hard_drive", tone: mock.p.m3tertiary, v: 0.8}
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

    // Media
    Rectangle {
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
}
