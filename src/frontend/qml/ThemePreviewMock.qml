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
        const bg = c.background ?? "#fff8f6", tx = c.text ?? "#261916", pr = c.primary ?? "#895143", se = c.secondary
            ?? "#775b52", te = c.tertiary ?? "#68613c";
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
                {
                    icon: "dashboard",
                    label: qsTr("Tableau de bord")
                },
                {
                    icon: "queue_music",
                    label: qsTr("Média")
                },
                {
                    icon: "speed",
                    label: qsTr("Performances")
                },
                {
                    icon: "workspaces",
                    label: qsTr("Espaces de travail")
                }
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
    ThemePreviewWeather {
        mock: mock
    }

    // User and system
    ThemePreviewUser {
        mock: mock
    }

    // Clock
    ThemePreviewClock {
        mock: mock
    }

    // Calendar
    ThemePreviewCalendar {
        mock: mock
    }

    // Resources
    ThemePreviewResources {
        mock: mock
    }

    // Media
    ThemePreviewMedia {
        mock: mock
    }
}
