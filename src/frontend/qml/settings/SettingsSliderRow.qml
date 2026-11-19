import QtQuick
import QtQuick.Layouts
import qs.components.controls
import qs.config
import "."

// Ligne avec un fin curseur d'accent ; la valeur en direct s'affiche à droite du titre à la couleur d'accent.
SettingsRow {
    id: row

    property real from: 0
    property real to: 100
    property real stepSize: 1
    // Valeur enregistrée ; suit le backend tant que la poignée n'est pas tenue.
    property real value: 0
    // Envoyer seulement au relâchement de la poignée.
    property bool commitOnRelease: false
    property alias slider: sl

    signal moved(real value)
    // Valeur en direct pendant le geste quand commitOnRelease (aperçu local, aucun envoi).
    signal previewed(real value)

    below: true

    StyledSlider {
        id: sl

        Layout.fillWidth: true
        implicitHeight: 20
        from: row.from
        to: row.to
        stepSize: row.stepSize
        onMoved: row.commitOnRelease ? row.previewed(value) : row.moved(value)
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
