// Infobulle de l'espace (suit la tuile survolée).
// Extrait de WorkspaceBrowser.qml (pur découpage) : les ids du parent sont reçus en propriétés requises.
import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config

StyledRect {
    id: tip
    required property var browser
    readonly property alias label: tipLabel
    property bool shown: false
    z: 50
    visible: opacity > .01
    opacity: shown ? 1 : 0
    scale: shown || !browser.fx ? 1 : .92
    implicitWidth: tipLabel.implicitWidth + Appearance.padding.large * 2
    implicitHeight: tipLabel.implicitHeight + Appearance.padding.normal * 2
    radius: Appearance.rounding.small
    color: Colours.palette.m3surfaceContainerHighest
    border.width: 1
    border.color: Colours.palette.m3outlineVariant
    Behavior on opacity {
        enabled: !Appearance.reducedEffects
        Anim {
            duration: Appearance.anim.durations.small
        }
    }
    Behavior on scale {
        WorkspaceSpring {}
    }
    StyledText {
        id: tipLabel
        anchors.centerIn: parent
        font.pointSize: Appearance.font.size.small
    }
}
