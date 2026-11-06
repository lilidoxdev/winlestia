import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

// Inline "‹ value ›" selector over a list of values. Left/Right keys step too.
FocusScope {
    id: stepper

    // [{label, value}]
    property var model: []
    property var current
    readonly property int index: {
        for (let i = 0; i < model.length; i++)
            if (model[i].value === current)
                return i;
        return -1;
    }
    readonly property string currentLabel: index >= 0 ? model[index].label : String(current ?? "")

    signal chosen(var value)

    function step(delta: int): void {
        const base = index >= 0 ? index : 0;
        const next = Math.max(0, Math.min(model.length - 1, base + delta));
        if (next !== index && model.length > 0)
            chosen(model[next].value);
    }

    implicitWidth: pill.implicitWidth
    implicitHeight: pill.implicitHeight
    activeFocusOnTab: true

    Keys.onPressed: event => {
        if (event.key === Qt.Key_Left) {
            step(-1);
            event.accepted = true;
        } else if (event.key === Qt.Key_Right) {
            step(1);
            event.accepted = true;
        }
    }

    StyledRect {
        id: pill

        implicitWidth: row.implicitWidth + Appearance.padding.small * 2
        implicitHeight: 36
        radius: Appearance.rounding.full
        color: Qt.alpha(Colours.palette.m3secondaryContainer, 1)

        RowLayout {
            id: row

            anchors.centerIn: parent
            spacing: 0

            IconButton {
                icon: "chevron_left"
                type: IconButton.Text
                disabled: stepper.index <= 0
                onClicked: {
                    stepper.forceActiveFocus();
                    stepper.step(-1);
                }
            }

            StyledText {
                Layout.preferredWidth: Math.max(64, implicitWidth)
                horizontalAlignment: Text.AlignHCenter
                text: stepper.currentLabel
                font.pointSize: Appearance.font.size.normal
                color: Colours.palette.m3onSecondaryContainer
            }

            IconButton {
                icon: "chevron_right"
                type: IconButton.Text
                disabled: stepper.index >= stepper.model.length - 1
                onClicked: {
                    stepper.forceActiveFocus();
                    stepper.step(1);
                }
            }
        }
    }

    SettingsFocusRing {
        target: pill
        visible: stepper.activeFocus
    }
}
