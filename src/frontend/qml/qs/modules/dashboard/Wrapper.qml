pragma ComponentBehavior: Bound

import qs.components
import qs.components.filedialog
import qs.config
import qs.utils
import Caelestia
import Quickshell
import QtQuick

Item {
    id: root

    required property PersistentProperties visibilities
    readonly property PersistentProperties dashState: PersistentProperties {
        property int currentTab
        property date currentDate: new Date()

        reloadableId: "dashboardState"
    }
    readonly property FileDialog facePicker: FileDialog {
        title: qsTr("Select a profile picture")
        filterLabel: qsTr("Image files")
        filters: Images.validImageExtensions
        onAccepted: path => {
            if (CUtils.copyFile(Qt.resolvedUrl(path), Qt.resolvedUrl(`${Paths.home}/.face`)))
                Quickshell.execDetached(["notify-send", "-a", "caelestia-shell", "-u", "low", "-h", `STRING:image-path:${path}`, "Profile picture changed", `Profile picture changed to ${Paths.shortenHome(path)}`]);
            else
                Quickshell.execDetached(["notify-send", "-a", "caelestia-shell", "-u", "critical", "Unable to change profile picture", `Failed to change profile picture to ${Paths.shortenHome(path)}`]);
        }
    }

    readonly property real nonAnimHeight: content.item?.nonAnimHeight ?? 0

    // True while the drawer is moving (y/height changing); the layer exists only then, not during tab slides.
    property bool drawerMoving: false

    // Width the host can give the drawer (0 = unconstrained); panes scale down to fit, see Content.
    property real maxWidth: 0
    // Narrowest drawer width the content can shrink to; the host uses it to decide when to stop shrinking.
    readonly property real minWidth: content.item?.minWidth ?? 0
    // Drawer width at scale 1, independent of maxWidth.
    readonly property real naturalWidth: content.item?.naturalWidth ?? 0

    visible: height > 0
    implicitHeight: content.implicitHeight
    implicitWidth: content.implicitWidth

    onYChanged: if (visible && !Appearance.reducedEffects) {
        drawerMoving = true;
        settle.restart();
    }
    onHeightChanged: if (visible && !Appearance.reducedEffects) {
        drawerMoving = true;
        settle.restart();
    }

    // Single-shot, only armed by a y/height change: no idle loop.
    Timer {
        id: settle

        interval: 120
        onTriggered: root.drawerMoving = false
    }

    // Keep initialized content warm; closing the drawer only changes visibility.
    Loader {
        id: content

        anchors.horizontalCenter: parent.horizontalCenter
        anchors.bottom: parent.bottom

        visible: true
        active: true
        layer.enabled: !Appearance.reducedEffects && root.drawerMoving

        sourceComponent: Content {
            visibilities: root.visibilities
            state: root.dashState
            facePicker: root.facePicker
            availableWidth: root.maxWidth
        }
    }
}
