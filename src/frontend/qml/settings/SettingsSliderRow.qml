import QtQuick
import QtQuick.Layouts
import qs.components.controls
import qs.config
import "."

// Row with a thin accent slider; the live value is printed at the right of the title in the accent colour.
SettingsRow {
    id: row

    property real from: 0
    property real to: 100
    property real stepSize: 1
    // Stored value; follows the backend while the handle is not held.
    property real value: 0
    // Send only when the handle is released.
    property bool commitOnRelease: false
    property alias slider: sl

    signal moved(real value)

    below: true

    StyledSlider {
        id: sl

        Layout.fillWidth: true
        implicitHeight: 20
        from: row.from
        to: row.to
        stepSize: row.stepSize
        onMoved: if (!row.commitOnRelease)
            row.moved(value)
        onPressedChanged: if (!pressed && row.commitOnRelease)
            row.moved(value)

        Binding {
            target: sl
            property: "value"
            value: row.value
            when: !sl.pressed
        }
    }
}
