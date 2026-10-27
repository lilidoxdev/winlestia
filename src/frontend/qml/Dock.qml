// Windows bottom-window adapter, reusing Caelestia StateLayer, typography, controls and easing.
import QtQuick
import QtQuick.Window
import QtQuick.Shapes
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.config
import qs.services

Window {
    id:dock
    // A transient Window is deferred while its owning dashboard is hidden on Windows.
    transientParent:null
    title:"Panneaux · Dock Caelestia"
    color:"transparent"
    flags:(Bridge.preview?Qt.Window:Qt.Tool)|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.NoDropShadowWindowHint|Qt.WindowDoesNotAcceptFocus
    property bool menuOpen:dockMenu.visible
    DockMenu{id:dockMenu}
    function showMenu(kind,data,item){const point=item.mapToGlobal(item.width/2,0);dockMenu.openAt(kind,data,point.x,dock.y+dock.offset)}
    property bool shown:Bridge.preview
    property real offset:shown?0:height
    onOffsetChanged:Bridge.updateSurfaces()
    property real customWidth:Bridge.settings.dockWidth??0
    width:Math.min(Bridge.screenWidth-24,customWidth>0?Math.max(560,customWidth):Math.max(756,520+Bridge.taskbarApps.length*47))
    height:68
    x:Bridge.screenX+(Bridge.screenWidth-width)/2
    y:Bridge.screenY+Bridge.screenHeight-height-(Bridge.screenFullscreen?12:Math.max(0,Config.border.thickness))
    visible:Bridge.taskbarEnabled&&(shown||offset<height-1)
    Behavior on offset {Anim {duration:Appearance.anim.durations.expressiveFastSpatial;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
    Item {
        anchors.fill:parent
        transform:Translate {y:dock.offset}
        RowLayout {
            anchors.fill:parent;anchors.topMargin:8;anchors.bottomMargin:8;anchors.leftMargin:Bridge.frameEnabled&&!Bridge.screenFullscreen?34:20;anchors.rightMargin:Bridge.frameEnabled&&!Bridge.screenFullscreen?34:20;spacing:5
            Item {
                objectName:"dockStart";implicitWidth:40;implicitHeight:40
                StateLayer {radius:20;function onClicked(){Bridge.openTab(3)}}
                MouseArea{anchors.fill:parent;acceptedButtons:Qt.RightButton;onClicked:dock.showMenu("system",{},parent)}
                Grid {anchors.centerIn:parent;columns:2;spacing:2;Repeater {model:4;Rectangle{width:8;height:8;color:Colours.palette.m3primary;radius:.5}}}
            }
            IconButton {icon:"search";type:IconButton.Text;onClicked:Bridge.action("palette",{})}
            Rectangle {implicitWidth:1;implicitHeight:23;color:Colours.palette.m3outlineVariant;Layout.leftMargin:6;Layout.rightMargin:6}
            Flickable {
                Layout.fillWidth:true;implicitHeight:44;contentWidth:appsRow.implicitWidth;clip:true;boundsBehavior:Flickable.StopAtBounds
                Row {
                    id:appsRow;spacing:4
                    Repeater {model:Bridge.taskbarApps
                        StyledRect {
                            id:app;required property var modelData;required property int index;objectName:"dock-app-"+modelData.id;implicitWidth:42+(modelData.pinned&&index===Bridge.taskbarApps.filter(a=>a.pinned).length-1?14:0);implicitHeight:42;radius:15
                            color:modelData.active?Colours.palette.m3secondaryContainer:"transparent"
                            StateLayer {function onClicked(){Bridge.closePanel();Bridge.action("activate",{id:app.modelData.id,hwnd:app.modelData.hwnd})}}
                            MouseArea{anchors.fill:parent;acceptedButtons:Qt.RightButton;onClicked:dock.showMenu("app",app.modelData,app)}
                            Rectangle{visible:app.modelData.pinned&&app.index===Bridge.taskbarApps.filter(a=>a.pinned).length-1;width:1;height:23;anchors.right:parent.right;anchors.verticalCenter:parent.verticalCenter;color:Colours.palette.m3outlineVariant}
                            MouseArea{anchors.fill:parent;acceptedButtons:Qt.LeftButton;preventStealing:true;property real startX;property bool moving:false;onPressed:mouse=>{startX=mouse.x;moving=false} onPositionChanged:mouse=>{if(pressed&&app.modelData.pinned&&Math.abs(mouse.x-startX)>10)moving=true} onReleased:mouse=>{if(moving){let count=Bridge.taskbarApps.filter(a=>a.pinned).length;Bridge.action("dockApp",{id:app.modelData.id,action:"reorder",index:Math.max(0,Math.min(count-1,app.index+Math.round((mouse.x-startX)/46)))})}else{Bridge.closePanel();Bridge.action("activate",{id:app.modelData.id,hwnd:app.modelData.hwnd})}}}
                            Image {anchors.centerIn:parent;width:28;height:28;fillMode:Image.PreserveAspectFit;asynchronous:true;source:Bridge.iconUrl(app.modelData.id,Bridge.iconRevision)}
                            Rectangle {anchors.bottom:parent.bottom;anchors.horizontalCenter:parent.horizontalCenter;anchors.bottomMargin:1;width:app.modelData.active?15:4;height:3;radius:2;color:Colours.palette.m3primary;visible:app.modelData.hwnd>0}
                        }
                    }
                }
            }
            IconButton {id:runningButton;objectName:"dockRunning";icon:"apps";type:IconButton.Text;onClicked:dock.showMenu("running",{},runningButton)}
            IconButton {icon:"volume_up";type:IconButton.Text;onClicked:Bridge.navigate("controls")}
            IconButton {icon:"wifi";type:IconButton.Text;onClicked:Bridge.navigate("controls")}
            IconButton {icon:"settings";type:IconButton.Text;onClicked:Bridge.action("settings",{})}
            IconButton{id:powerButton;icon:"power_settings_new";type:IconButton.Text;onClicked:dock.showMenu("power",{},powerButton)}
            Column {Layout.leftMargin:8;Layout.rightMargin:7;StyledText {anchors.horizontalCenter:parent.horizontalCenter;text:Time.format("hh:mm");font.pointSize:10}
StyledText {anchors.horizontalCenter:parent.horizontalCenter;text:Time.format("dd MMM");font.pointSize:8;color:Colours.palette.m3onSurfaceVariant}}
        }
    }
    MouseArea{width:12;anchors.right:parent.right;anchors.top:parent.top;anchors.bottom:parent.bottom;cursorShape:Qt.SizeHorCursor;property real originX;property real originWidth;onPressed:mouse=>{originX=mapToGlobal(mouse.x,mouse.y).x;originWidth=dock.width} onPositionChanged:mouse=>{if(pressed)dock.customWidth=Math.max(560,Math.min(Bridge.screenWidth-24,originWidth+2*(mapToGlobal(mouse.x,mouse.y).x-originX)))} onReleased:Bridge.action("preference",{key:"dockWidth",value:dock.width})}
    Connections {target:Bridge;function onShellEvent(event){if(event==="dock")dock.shown=true;else if(event==="dockHide")dock.shown=false}}
}
