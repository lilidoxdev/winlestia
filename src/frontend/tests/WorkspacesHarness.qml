import QtQuick
import qs.modules.dashboard
import qs.services

Rectangle {
    color: Colours.palette.m3surface
    Component.onCompleted: console.log("BRIDGE", Bridge, Bridge.dark)
    Workspaces { x: 40; y: 40; width: parent.width - 80 }
}
