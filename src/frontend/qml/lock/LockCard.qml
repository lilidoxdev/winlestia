import QtQuick
import qs.components
import qs.services
import qs.config

// Carte commune des colonnes latérales.
StyledRect {
    default property alias content: holder.data
    property real inset: Appearance.padding.large * 1.2

    implicitHeight: holder.childrenRect.height + inset * 2
    radius: Appearance.rounding.large
    color: Colours.layer(Colours.solid(Colours.palette.m3surfaceContainer), 2)

    Item {
        id: holder

        anchors.fill: parent
        anchors.margins: parent.inset
    }
}
