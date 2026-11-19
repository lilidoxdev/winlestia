import QtQuick
import qs.components.controls
import qs.config

// StyledSwitch (coche quand actif, croix quand inactif) qui continue de suivre la valeur enregistrée après un basculement de l'utilisateur.
StyledSwitch {
    id: sw

    property bool value: false

    Binding {
        target: sw
        property: "checked"
        value: sw.value
    }
}
