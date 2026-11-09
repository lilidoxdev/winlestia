import QtQuick
import QtQuick.Layouts
import qs.components
import qs.config
import qs.services
import ".."

// Bloc `calendar` : variant month|day, firstDay monday|sunday. Se met à jour à la minute (Time.date), jamais plus.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null

    readonly property string variant: spec ? String(Bridge.winjets.resolve(spec.variant, ctx) ?? "month") : "month"
    readonly property bool sundayFirst: spec ? Bridge.winjets.resolve(spec.firstDay, ctx) === "sunday" : false
    readonly property date today: Time.date
    readonly property int offset: {
        const first = new Date(today.getFullYear(), today.getMonth(), 1).getDay(); // 0 = dimanche
        return sundayFirst ? first : (first + 6) % 7;
    }
    readonly property int days: new Date(today.getFullYear(), today.getMonth() + 1, 0).getDate()
    readonly property var heads: {
        const out = [];
        for (let i = 0; i < 7; i++)
            out.push(Qt.locale().dayName((sundayFirst ? i : i + 1) % 7, Locale.NarrowFormat));
        return out;
    }

    implicitWidth: variant === "day" ? 96 : 224
    implicitHeight: variant === "day" ? 96 : 190

    ColumnLayout {
        visible: block.variant === "day"
        anchors.fill: parent
        spacing: 0

        StyledText {
            Layout.fillWidth: true
            text: Qt.locale().dayName(block.today.getDay(), Locale.LongFormat)
            color: WinjetTheme.accent
            font.pointSize: Appearance.font.size.small
            font.capitalization: Font.AllUppercase
            font.letterSpacing: 0.8
            elide: Text.ElideRight
        }
        StyledText {
            Layout.fillWidth: true
            Layout.fillHeight: true
            text: String(block.today.getDate())
            color: WinjetTheme.textPrimary
            font.pointSize: Appearance.font.size.extraLarge + 14
            font.weight: Font.Medium
            fontSizeMode: Text.Fit
            minimumPixelSize: 16
            verticalAlignment: Text.AlignVCenter
        }
        StyledText {
            Layout.fillWidth: true
            text: Qt.locale().monthName(block.today.getMonth(), Locale.LongFormat) + " " + block.today.getFullYear()
            color: WinjetTheme.textSecondary
            font.pointSize: Appearance.font.size.small
            elide: Text.ElideRight
        }
    }

    ColumnLayout {
        visible: block.variant !== "day"
        anchors.fill: parent
        spacing: 4

        StyledText {
            Layout.fillWidth: true
            text: Qt.locale().monthName(block.today.getMonth(), Locale.LongFormat) + " " + block.today.getFullYear()
            color: WinjetTheme.textPrimary
            font.pointSize: Appearance.font.size.normal
            font.weight: Font.Medium
            font.capitalization: Font.Capitalize
            elide: Text.ElideRight
        }

        GridLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            columns: 7
            rowSpacing: 0
            columnSpacing: 0

            Repeater {
                model: block.heads

                StyledText {
                    required property string modelData

                    Layout.fillWidth: true
                    horizontalAlignment: Text.AlignHCenter
                    text: modelData
                    color: WinjetTheme.textSecondary
                    font.pointSize: Appearance.font.size.small
                }
            }

            Repeater {
                model: 42

                Item {
                    id: cell

                    required property int index
                    readonly property int day: index - block.offset + 1
                    readonly property bool inMonth: day >= 1 && day <= block.days
                    readonly property bool isToday: inMonth && day === block.today.getDate()

                    Layout.fillWidth: true
                    Layout.fillHeight: true
                    Layout.preferredHeight: 1

                    Rectangle {
                        anchors.centerIn: parent
                        width: Math.min(parent.width, parent.height)
                        height: width
                        radius: width / 2
                        color: WinjetTheme.accent
                        visible: cell.isToday
                    }
                    StyledText {
                        anchors.centerIn: parent
                        visible: cell.inMonth
                        text: String(cell.day)
                        color: cell.isToday ? Colours.palette.m3onPrimary : WinjetTheme.textPrimary
                        font.pointSize: Appearance.font.size.small
                    }
                }
            }
        }
    }

    ActionArea {
        spec: block.spec
        host: block.host
    }
}
