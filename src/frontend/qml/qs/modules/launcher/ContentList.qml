// Windows model adapter; original Caelestia Content, AppList, AppItem and controls render the launcher.
import QtQuick
import qs.components.controls
Item {
    required property var content
    required property var visibilities
    required property var panels
    required property real maxHeight
    required property StyledTextField search
    required property int padding
    required property int rounding
    readonly property bool showWallpapers:false
    readonly property var currentList:list
    anchors.horizontalCenter:parent.horizontalCenter
    anchors.bottom:parent.bottom
    implicitWidth:600
    implicitHeight:Math.min(maxHeight,Math.max(70,list.implicitHeight))
    clip:true
    AppList {id:list;anchors.fill:parent;search:parent.search;visibilities:parent.visibilities}
}
