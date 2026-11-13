import QtQuick
import QtQml

// Couche bureau des Winjets : une fenêtre par instance (liste stable d'identifiants, donc pas de recréation à chaque donnée)
// et, en mode édition seulement, un EditOverlay par écran. Instantiator : les délégués sont des fenêtres, pas des Items.
QtObject {
    id: layer

    // Les overlays restent le temps du fondu de sortie (un minuteur à usage unique), puis sont détruits.
    property bool alive: Bridge.winjets.editMode

    property var windows: Instantiator {
        model: Bridge.winjets.instanceIds

        delegate: WinjetWindow {
            required property string modelData

            instanceId: modelData
        }
    }

    property var overlays: Instantiator {
        model: layer.alive ? Bridge.winjets.screenNames : []

        delegate: EditOverlay {
            required property string modelData

            screenName: modelData
            shown: Bridge.winjets.editMode
        }
    }

    property var fade: Timer {
        interval: 180
        onTriggered: layer.alive = Bridge.winjets.editMode
    }

    property var watch: Connections {
        target: Bridge.winjets

        function onEditModeChanged(): void {
            if (Bridge.winjets.editMode) {
                layer.fade.stop();
                layer.alive = true;
            } else {
                layer.fade.restart();
            }
        }
    }
}
