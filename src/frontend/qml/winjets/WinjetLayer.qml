import QtQuick
import QtQml

// Couche bureau des Winjets : une fenêtre par instance (liste stable d'identifiants, donc pas de recréation à chaque donnée)
// et, en mode édition seulement, un EditOverlay par écran. Instantiator : les délégués sont des fenêtres, pas des Items.
QtObject {
    id: layer

    property var windows: Instantiator {
        model: Bridge.winjets.instanceIds

        delegate: WinjetWindow {
            required property string modelData

            instanceId: modelData
        }
    }

    property var overlays: Instantiator {
        model: Bridge.winjets.editMode ? Bridge.winjets.screenNames : []

        delegate: EditOverlay {
            required property string modelData

            screenName: modelData
        }
    }
}
