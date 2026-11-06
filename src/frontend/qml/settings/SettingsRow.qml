import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config
import "."

// One setting: label + one-line grey description on the left, control(s) on the right.
// Conditional rows (`shown`) animate height and opacity; the search pulses a row with pulse().
FocusScope {
    id: row

    property string rowId
    property string title: SettingsIndex.label(rowId)
    property string hint: SettingsIndex.desc(rowId)
    property color hintColor: Colours.palette.m3outline
    // Accent-coloured value printed at the right of the title (sliders): "1864 ms".
    property string valueText
    property bool shown: true
    // Indented sub-option that belongs to the row above.
    property bool sub: false
    // Control under the text, spanning the full width (sliders, option lists, chips).
    property bool below: false
    property bool dim: false
    // The control activated when the row itself is clicked (switches).
    property Item target: null
    default property alias control: ctl.data

    readonly property bool stacked: width < 460
    readonly property bool dropped: below || stacked
    readonly property real reveal: shownAmount
    property real shownAmount: shown ? 1 : 0
    readonly property real indent: sub ? Appearance.padding.large * 2 : 0

    function activate(): void {
        if (dim || !target || !target.enabled)
            return;
        target.forceActiveFocus();
        if (target.toggle)
            target.toggle();
    }

    // Brief highlight used by the search; skipped entirely with reduced effects.
    function pulse(): void {
        if (Appearance.reducedEffects)
            return;
        pulseAnim.restart();
    }

    Layout.fillWidth: true
    implicitHeight: bg.implicitHeight * shownAmount
    visible: shownAmount > 0.001
    clip: shownAmount < 0.999
    opacity: shownAmount * (dim ? 0.5 : 1)
    enabled: !dim

    Behavior on shownAmount {
        enabled: !Appearance.reducedEffects

        Anim {
            duration: Appearance.anim.durations.small
        }
    }

    StyledRect {
        id: bg

        anchors.left: parent.left
        anchors.right: parent.right
        anchors.top: parent.top
        implicitHeight: grid.implicitHeight + Appearance.padding.larger * 2
        radius: Appearance.rounding.small
        color: Qt.alpha(Colours.palette.m3onSurface, hover.hovered && !row.dim && row.target ? 0.06 : 0)

        HoverHandler {
            id: hover
        }

        TapHandler {
            enabled: row.target !== null && !row.dim
            onTapped: row.activate()
        }

        Rectangle {
            visible: row.sub
            x: Appearance.padding.large
            y: Appearance.padding.smaller
            width: 2
            height: parent.height - Appearance.padding.smaller * 2
            radius: 1
            color: Colours.palette.m3outlineVariant
        }

        GridLayout {
            id: grid

            anchors.left: parent.left
            anchors.right: parent.right
            anchors.top: parent.top
            anchors.leftMargin: Appearance.padding.larger + row.indent
            anchors.rightMargin: Appearance.padding.larger
            anchors.topMargin: Appearance.padding.larger
            columns: 2
            columnSpacing: Appearance.spacing.normal
            rowSpacing: Appearance.spacing.smaller

            ColumnLayout {
                Layout.row: 0
                Layout.column: 0
                Layout.fillWidth: true
                Layout.minimumWidth: 120
                Layout.alignment: Qt.AlignVCenter
                spacing: 1

                StyledText {
                    Layout.fillWidth: true
                    wrapMode: Text.WordWrap
                    text: row.title
                    font.pointSize: Appearance.font.size.normal
                }

                StyledText {
                    Layout.fillWidth: true
                    visible: text !== ""
                    wrapMode: Text.WordWrap
                    text: row.hint
                    font.pointSize: Appearance.font.size.small
                    color: row.hintColor
                }
            }

            StyledText {
                Layout.row: 0
                Layout.column: 1
                Layout.alignment: Qt.AlignVCenter
                visible: row.valueText !== ""
                text: row.valueText
                font.pointSize: Appearance.font.size.small
                font.weight: Font.Medium
                color: Colours.palette.m3primary
            }

            RowLayout {
                id: ctl

                Layout.row: row.dropped ? 1 : 0
                Layout.column: row.dropped ? 0 : 1
                Layout.columnSpan: row.dropped ? 2 : 1
                Layout.fillWidth: row.dropped
                Layout.alignment: row.dropped ? Qt.AlignLeft | Qt.AlignVCenter : Qt.AlignRight | Qt.AlignVCenter
                visible: children.length > 0
                spacing: Appearance.spacing.small
            }
        }
    }

    SettingsFocusRing {
        target: bg
        grow: 0
        visible: row.activeFocus && row.visible
    }

    Rectangle {
        id: glow

        anchors.fill: bg
        radius: bg.radius
        color: Qt.alpha(Colours.palette.m3primary, 0.18)
        border.width: 2
        border.color: Colours.palette.m3primary
        opacity: 0
        visible: opacity > 0
    }

    NumberAnimation {
        id: pulseAnim

        target: glow
        property: "opacity"
        from: 1
        to: 0
        duration: Math.round(600 * (Bridge.settings.motionScale ?? 1))
        easing.type: Easing.OutCubic
    }
}
