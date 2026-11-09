import QtQuick
import qs.services

// Bloc `divider` : filet 1 px.
Item {
    property var spec: null
    property var ctx: ({})
    property var host: null

    implicitWidth: 1
    implicitHeight: 1

    Rectangle {
        anchors.fill: parent
        color: Qt.alpha(Colours.palette.m3outline, 0.4)
    }
}
