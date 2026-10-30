// One pill per Windows virtual desktop (registry-driven, see VirtualDesktops.cs). Active pill = palette primary; click switches, wheel = next/previous.
import QtQuick
import qs.components
import qs.components.controls
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
    implicitWidth:visible?pills.width+8+addButton.implicitWidth+4:0;implicitHeight:32
    Accessible.name:qsTr("Bureaux virtuels")
    // "+" = Win+Ctrl+D on a user gesture (VirtualDesktops.CreateNew); Windows creates and switches to the new desktop.
    IconButton {
        id:addButton
        objectName:"dockDesktopAdd"
        anchors.right:parent.right;anchors.rightMargin:2;anchors.verticalCenter:parent.verticalCenter
        icon:"add";type:IconButton.Text;Accessible.name:qsTr("Nouveau bureau")
        onClicked:Bridge.action("desktopNew",{})
        HoverHandler {id:addHover}
        ShellToolTip {visible:addHover.hovered;delay:400;text:qsTr("Nouveau bureau")}
    }
    Row {
        id:pills
        anchors.verticalCenter:parent.verticalCenter;anchors.left:parent.left;spacing:0
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
                ShellToolTip {visible:hover.hovered;delay:400;text:(root.info.names&&root.info.names[cell.index])||qsTr("Bureau %1").arg(cell.index+1)}
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
