import QtQuick
import qs.components
import qs.components.images
import qs.services
import qs.config
import qs.utils

// Avatar dans la forme « ClamShell », icône de repli.
Item {
    id: root

    property real side: 300
    implicitWidth: side
    implicitHeight: side

    LockShape {
        anchors.fill: parent
        preset: "clamShell"
        fillColour: Colours.solid(Colours.palette.m3surfaceContainerHigh)

        MaterialIcon {
            anchors.centerIn: parent
            text: "person"
            fill: 1
            grade: 200
            color: Colours.palette.m3onSurfaceVariant
            font.pointSize: Math.floor(root.side / 2) || 1
        }

        CachingImage {
            anchors.fill: parent
            path: `${Paths.home}/.face`
        }
    }
}
