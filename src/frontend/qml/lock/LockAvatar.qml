import QtQuick
import qs.components
import qs.components.images
import qs.services
import qs.config
import qs.utils

// Avatar dans une grande forme arrondie (approximation de la forme « ClamShell » de Caelestia), icône de repli.
Item {
    id: root

    property real side: 300
    // Rayon relatif : plus grand que la moitié d'un carré arrondi, moins qu'un disque.
    readonly property real shapeRadius: 0.32

    implicitWidth: side
    implicitHeight: side

    StyledClippingRect {
        anchors.fill: parent
        radius: root.side * root.shapeRadius
        color: Colours.palette.m3primaryContainer

        MaterialIcon {
            anchors.centerIn: parent
            text: "person"
            fill: 1
            grade: 200
            color: Colours.palette.m3onPrimaryContainer
            font.pointSize: Math.floor(root.side / 2) || 1
        }

        CachingImage {
            anchors.fill: parent
            path: `${Paths.home}/.face`
        }
    }
}
