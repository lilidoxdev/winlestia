// Icône d'application avec repli sur une lettre (seulement si l'icône manque ou n'a pas pu se charger).
import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes
import qs.components
import qs.components.controls
import qs.services
import qs.config

StyledRect {
    id: letter
    property string name
    property real size: 36
    implicitWidth: size
    implicitHeight: size
    radius: Appearance.rounding.full
    color: Colours.palette.m3primaryContainer
    StyledText {
        anchors.centerIn: parent
        text: (letter.name ?? "").trim().charAt(0).toUpperCase()
        color: Colours.palette.m3onPrimaryContainer
        font.pointSize: Math.round(letter.size * 0.17) + 3
        font.weight: 600
    }
}
