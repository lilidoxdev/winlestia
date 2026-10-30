// One pill per Windows virtual desktop (registry-driven, see VirtualDesktops.cs). Active pill = palette primary; click switches, wheel = next/previous.
import QtQuick
import QtQuick.Controls as C
import qs.components
import qs.config
import qs.services

Item {
    id:root
    objectName:"dockDesktops"
    readonly property var info:Bridge.virtualDesktops||({})
    readonly property int count:info.count||0
    readonly property int current:info.current||0
    property real wheelAcc:0
    visible:(Bridge.settings.desktopIndicator??true)&&count>0
    implicitWidth:visible?pills.width+8:0;implicitHeight:32
    Accessible.name:qsTr("Bureaux virtuels")
    Row {
        id:pills
        anchors.centerIn:parent;spacing:0
        Repeater {
            model:root.count
            delegate:Item {
                id:cell
                required property int index
                readonly property bool active:index===root.current
                width:pill.width+8;height:root.implicitHeight
                Rectangle {
                    id:pill
                    anchors.centerIn:parent;height:8;radius:4
                    width:cell.active?18:8
                    color:cell.active?Colours.palette.m3primary:Colours.palette.m3outline
                    Behavior on width {enabled:!Appearance.reducedEffects;Anim {duration:Appearance.anim.durations.small}}
                    Behavior on color {enabled:!Appearance.reducedEffects;ColorAnimation {duration:Appearance.anim.durations.small}}
                }
                HoverHandler {id:hover;cursorShape:Qt.PointingHandCursor}
                TapHandler {onTapped:Bridge.action("desktopSwitch",{index:cell.index})}
                C.ToolTip {visible:hover.hovered;delay:400;popupType:C.Popup.Window;text:(root.info.names&&root.info.names[cell.index])||qsTr("Bureau %1").arg(cell.index+1)}
            }
        }
    }
    WheelHandler {
        acceptedDevices:PointerDevice.Mouse|PointerDevice.TouchPad
        onWheel:event=>{
            root.wheelAcc+=event.angleDelta.y
            if(Math.abs(root.wheelAcc)>=120){Bridge.action("desktopSwitch",{relative:root.wheelAcc>0?-1:1});root.wheelAcc=0}
        }
    }
}
