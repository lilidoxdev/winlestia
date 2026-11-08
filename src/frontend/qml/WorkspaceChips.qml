// Puces de catégories, repliables sur une seule ligne.
// Extrait de WorkspaceBrowser.qml (pur découpage) : les ids du parent sont reçus en propriétés requises.
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.services
import qs.config

Item {
    id: chipsBox
    required property var browser
    property bool open: false
    readonly property bool overflow: chipFlow.implicitHeight > 40
    Layout.fillWidth: true
    implicitHeight: open ? chipFlow.implicitHeight : Math.min(chipFlow.implicitHeight, 34)
    clip: true
    opacity: browser.pHead
    Behavior on implicitHeight {
        enabled: !Appearance.reducedEffects
        Anim {
            duration: Appearance.anim.durations.small
        }
    }
    Flow {
        id: chipFlow
        width: parent.width - (chipsBox.overflow ? 44 : 0)
        spacing: Appearance.spacing.small
        Repeater {
            model: browser.categories
            WorkspaceFilterChip {
                required property string modelData
                label: modelData
                count: modelData === "Tout" ? -1 : (browser.categoryCounts[modelData] ?? -1)
                selected: browser.category === modelData
                onTriggered: {
                    browser.markBulk();
                    browser.category = modelData;
                    browser.replay();
                }
            }
        }
    }
    IconButton {
        visible: chipsBox.overflow
        anchors.right: parent.right
        anchors.top: parent.top
        icon: "expand_more"
        type: IconButton.Text
        rotation: chipsBox.open ? 180 : 0
        Behavior on rotation {
            WorkspaceSpring {}
        }
        onClicked: chipsBox.open = !chipsBox.open
    }
}
