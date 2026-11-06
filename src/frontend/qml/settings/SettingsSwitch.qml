import QtQuick
import qs.components.controls
import qs.config

// StyledSwitch (knob shows a check when on, a cross when off) that keeps following the stored value after the user toggled it.
StyledSwitch {
    id: sw

    property bool value: false

    Binding {
        target: sw
        property: "checked"
        value: sw.value
    }
}
