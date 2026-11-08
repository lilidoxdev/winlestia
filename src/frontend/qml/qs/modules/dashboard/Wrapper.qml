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
                Quickshell.execDetached(["notify-send", "-a", "caelestia-shell", "-u", "low", "-h", `STRING:image-path:${path}`,
                    "Profile picture changed", `Profile picture changed to ${Paths.shortenHome(path)}`]);
            else
                Quickshell.execDetached(["notify-send", "-a", "caelestia-shell", "-u", "critical", "Unable to change profile picture",
                    `Failed to change profile picture to ${Paths.shortenHome(path)}`]);
        }
    }

    readonly property real nonAnimHeight: content.item?.nonAnimHeight ?? 0

    // Vrai pendant le déplacement du tiroir (y/height changent) ; la couche n'existe qu'alors, pas pendant les glissements d'onglet.
    property bool drawerMoving: false

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

    // Un seul coup, armé uniquement par un changement de y/height : pas de boucle au repos.
    Timer {
        id: settle

        interval: 120
        onTriggered: root.drawerMoving = false
    }

    // Garde le contenu initialisé au chaud ; fermer le tiroir ne change que la visibilité.
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
        }
    }
}
