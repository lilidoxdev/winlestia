// Tonal pill button: state layer, spring hover/press, focus ring, staggered entrance.
import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes
import qs.components
import qs.components.controls
import qs.services
import qs.config

StyledRect {
    id: pill
    required property var host
    property string icon
    property string text
    property bool filled
    property alias order: pe.order
    signal clicked
    property real hk: pl.pressed ? 0.97 : pl.containsMouse ? 1.04 : 1
    Behavior on hk {
        WorkspacesSpring {}
    }
    implicitHeight: 36
    implicitWidth: pillRow.implicitWidth + Appearance.padding.large * 2
    radius: Appearance.rounding.full
    color: filled ? Colours.palette.m3primaryContainer : Colours.palette.m3surfaceContainerHigh
    activeFocusOnTab: true
    Accessible.role: Accessible.Button
    Accessible.name: text
    Keys.onReturnPressed: clicked()
    Keys.onSpacePressed: clicked()
    opacity: pe.alpha
    scale: pe.k * hk
    transform: Translate {
        y: pe.dy
    }
    WorkspacesEntrance {
        id: pe
        host: pill.host
        order: 9
    }
    StateLayer {
        id: pl
        color: pill.filled ? Colours.palette.m3onPrimaryContainer : Colours.palette.m3onSurface
        function onClicked(): void {
            pill.clicked();
        }
    }
    Row {
        id: pillRow
        anchors.centerIn: parent
        spacing: Appearance.spacing.small
        MaterialIcon {
            anchors.verticalCenter: parent.verticalCenter
            text: pill.icon
            fill: pl.containsMouse ? 1 : 0
            color: pill.filled ? Colours.palette.m3onPrimaryContainer : Colours.palette.m3primary
            font.pointSize: Appearance.font.size.normal
            Behavior on fill {
                Anim {}
            }
        }
        StyledText {
            anchors.verticalCenter: parent.verticalCenter
            text: pill.text
            color: pill.filled ? Colours.palette.m3onPrimaryContainer : Colours.palette.m3onSurface
        }
    }
    Rectangle {
        anchors.fill: parent
        radius: parent.radius
        color: "transparent"
        border.width: 2
        border.color: Colours.palette.m3primary
        visible: pill.activeFocus
    }
}
