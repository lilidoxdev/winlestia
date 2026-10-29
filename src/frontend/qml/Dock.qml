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
    property var dockItems:Bridge.taskbarApps
    property string dragId:""
    property int dragIndex:-1
    property int dropIndex:-1
    property real dragDelta:0
    property int pinnedCount:dockItems.filter(a=>a.pinned).length
    function visualIndex(i){if(dragIndex<0)return i;if(i===dragIndex)return dropIndex;if(dragIndex<dropIndex&&i>dragIndex&&i<=dropIndex)return i-1;if(dragIndex>dropIndex&&i>=dropIndex&&i<dragIndex)return i+1;return i}
    function commitMove(){let id=dragId,to=dropIndex,list=dockItems.slice();let item=list.splice(dragIndex,1)[0];list.splice(to,0,item);Bridge.action("dockApp",{id:id,action:"reorder",index:to});dragId="";dragIndex=-1;dropIndex=-1;dragDelta=0;dockItems=list}
    property bool menuOpen:dockMenu.visible
    // Window previews (native bubble in the backend): requested after a hover delay, never while hidden / sliding / menu / dragging.
    property string previewId:""
    readonly property bool previewAllowed:shown&&offset===0&&!menuOpen&&dragId===""&&Bridge.taskbarEnabled
    function previewRequest(item,cursor){
        if(!previewAllowed||item.modelData.count<1||item.modelData.hwnd<=0)return;
        previewEndTimer.stop();const c=item.mapToGlobal(item.width/2,0),p=item.mapToGlobal(cursor.x,cursor.y);
        previewId=item.modelData.id;Bridge.action("dockPreview",{id:previewId,x:c.x,y:dock.y,cx:p.x,cy:p.y,dpr:dock.screen?dock.screen.devicePixelRatio:1})
    }
    function previewLeave(){if(previewId!=="")previewEndTimer.restart()}
    function previewClose(){previewEndTimer.stop();if(previewId!==""){previewId="";Bridge.action("dockPreviewEnd",{now:true})}}
    Timer{id:previewEndTimer;interval:200;repeat:false;onTriggered:{if(dock.previewId!==""){dock.previewId="";Bridge.action("dockPreviewEnd",{})}}}
    onShownChanged:{if(!shown){hiding=reduced?fade>0.01:offset<height-1;previewClose()}syncShown()}
    onMenuOpenChanged:if(menuOpen)previewClose()
    DockMenu{id:dockMenu}
    function showMenu(kind,data,item){const point=item.mapToGlobal(item.width/2,0);dockMenu.openAt(kind,data,point.x,dock.y+dock.offset)}
    property bool shown:Bridge.preview
    // Effets allégés: no slide, a short opacity fade only (offset stays 0, so the contour does not move either).
    property bool reduced:Appearance.reducedEffects
    property real fade:shown?1:0
    Behavior on fade {enabled:dock.reduced;NumberAnimation{duration:120;easing.type:Easing.OutCubic}}
    property real offset:reduced||shown?0:height
    // Icons vanish as soon as they sink under the contour line instead of drawing over the screen-edge band.
    readonly property real contentOpacity:reduced?fade:Math.max(0,Math.min(1,1-offset/(height*0.6)))
    // True only while a hide transition started from a visible dock is running: toggling reducedEffects while hidden animates offset/fade but must never show the window.
    property bool hiding:false
    // shownCore is only written by syncShown(), so the result does not depend on the order in which QML re-evaluates bindings.
    property bool shownCore:false
    function syncShown(){shownCore=shown||(hiding&&(reduced?fade>0.01:offset<height-1))}
    onOffsetChanged:{Bridge.updateSurfaces();if(offset>=height-1)hiding=false;syncShown()}
    onFadeChanged:{if(reduced&&fade<=0.01)hiding=false;syncShown()}
    onReducedChanged:{hiding=false;syncShown()}
    Component.onCompleted:syncShown()
    property real customWidth:Bridge.settings.dockWidth??0
    width:Math.min(Bridge.screenWidth-24,customWidth>0?Math.max(560,customWidth):Math.max(756,520+Bridge.taskbarApps.length*47))
    height:68
    x:Bridge.screenX+(Bridge.screenWidth-width)/2
    y:Bridge.screenY+Bridge.screenHeight-height-(Bridge.screenFullscreen?12:Math.max(0,Config.border.thickness))
    visible:Bridge.taskbarEnabled&&shownCore
    Behavior on offset {Anim {duration:Appearance.anim.durations.expressiveFastSpatial;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
    // Fixed (never translated) clip box = dock contour bounds; only the content inside slides.
    Item {
        objectName:"dockClip";anchors.fill:parent;clip:true
    Item {
        objectName:"dockContent";anchors.fill:parent;opacity:dock.contentOpacity
        transform:Translate {y:dock.offset}
        RowLayout {
            anchors.fill:parent;anchors.topMargin:8;anchors.bottomMargin:8;anchors.leftMargin:Bridge.frameEnabled&&!Bridge.screenFullscreen?34:20;anchors.rightMargin:Bridge.frameEnabled&&!Bridge.screenFullscreen?34:20;spacing:5
            Item {
                objectName:"dockStart";implicitWidth:40;implicitHeight:40
                StateLayer {radius:Appearance.rounding.large;function onClicked(){Bridge.openTab(3)}}
                MouseArea{anchors.fill:parent;acceptedButtons:Qt.RightButton;onClicked:dock.showMenu("system",{},parent)}
                Grid {anchors.centerIn:parent;columns:2;spacing:2;Repeater {model:4;Rectangle{width:8;height:8;color:Colours.palette.m3primary;radius:Appearance.rounding.tiny/8}}}
            }
            IconButton {objectName:"dockRecycle";icon:"delete";type:IconButton.Text;onClicked:Bridge.action("emptyRecycleBin",{})}
            IconButton {objectName:"dockFiles";icon:"folder";type:IconButton.Text;Accessible.name:"Fichiers";onClicked:Bridge.action("openFiles",{})}
            IconButton {objectName:"dockSearch";icon:"search";type:IconButton.Text;onClicked:Bridge.action("palette",{})}
            Rectangle {implicitWidth:1;implicitHeight:23;color:Colours.palette.m3outlineVariant;Layout.leftMargin:6;Layout.rightMargin:6}
            Flickable {
                Layout.fillWidth:true;implicitHeight:44;contentWidth:appsRow.width;clip:true;boundsBehavior:Flickable.StopAtBounds
                Item {
                    id:appsRow;width:dock.dockItems.length*46+(dock.pinnedCount>0?14:0);height:44
                    Repeater {model:dock.dockItems
                        StyledRect {
                            id:app;required property var modelData;required property int index;objectName:"dock-app-"+modelData.id;width:42;height:42;radius:Appearance.rounding.normal; x:dock.visualIndex(index)*46+(index>=dock.pinnedCount&&dock.pinnedCount>0?14:0);z:dock.dragId===modelData.id?10:0
                            Behavior on x {NumberAnimation{duration:170;easing.type:Easing.OutCubic}}
                            transform:Translate{x:dock.dragId===app.modelData.id?dock.dragDelta+(dock.dragIndex-dock.dropIndex)*46:0}
                            color:modelData.active?Colours.palette.m3secondaryContainer:"transparent"
                            StateLayer {function onClicked(){dock.previewClose();Bridge.closePanel();Bridge.action("activate",{id:app.modelData.id,hwnd:app.modelData.hwnd})}}
                            HoverHandler{id:previewHover;onHoveredChanged:{if(hovered){previewArm.interval=dock.previewId!==""?120:450;previewArm.restart()}else{previewArm.stop();dock.previewLeave()}}}
                            Timer{id:previewArm;repeat:false;onTriggered:dock.previewRequest(app,previewHover.point.position)}
                            MouseArea{anchors.fill:parent;acceptedButtons:Qt.RightButton;onClicked:dock.showMenu("app",app.modelData,app)}
                            Rectangle{visible:app.modelData.pinned&&app.index===Bridge.taskbarApps.filter(a=>a.pinned).length-1;width:1;height:23;anchors.right:parent.right;anchors.verticalCenter:parent.verticalCenter;color:Colours.palette.m3outlineVariant}
                            MouseArea{
                                anchors.fill:parent;acceptedButtons:Qt.LeftButton;preventStealing:true
                                property real startGlobal;property bool moving:false
                                onPressed:mouse=>{startGlobal=mapToGlobal(mouse.x,mouse.y).x;moving=false}
                                onPressAndHold: {if(app.modelData.pinned)beginMove()}
                                function beginMove(){dock.previewClose();moving=true;dock.dragId=app.modelData.id;dock.dragIndex=app.index;dock.dropIndex=app.index}
                                onPositionChanged:mouse=>{if(!pressed||!app.modelData.pinned)return;let delta=mapToGlobal(mouse.x,mouse.y).x-startGlobal;if(!moving&&Math.abs(delta)>8)beginMove();if(moving){dock.dragDelta=delta;dock.dropIndex=Math.max(0,Math.min(dock.pinnedCount-1,dock.dragIndex+Math.round(delta/46)))}}
                                function finish(){dock.dragId="";dock.dragIndex=-1;dock.dropIndex=-1;dock.dragDelta=0}
                                onCanceled:finish()
                                onReleased:{if(moving){dock.commitMove()}else{dock.previewClose();Bridge.closePanel();Bridge.action("activate",{id:app.modelData.id,hwnd:app.modelData.hwnd})}}
                            }
                            SequentialAnimation on rotation {running:dock.dragId===app.modelData.id;loops:Animation.Infinite;NumberAnimation{to:-3;duration:75}NumberAnimation{to:3;duration:110}NumberAnimation{to:0;duration:75}}
                            Image {anchors.centerIn:parent;width:28;height:28;fillMode:Image.PreserveAspectFit;asynchronous:true;source:Bridge.iconUrl(app.modelData.id,Bridge.iconRevision)}
                            Rectangle {anchors.bottom:parent.bottom;anchors.horizontalCenter:parent.horizontalCenter;anchors.bottomMargin:1;width:app.modelData.active?15:4;height:3;radius:Appearance.rounding.tiny/2;color:Colours.palette.m3primary;visible:app.modelData.hwnd>0}
                        }
                    }
                }
            }
            IconButton {id:runningButton;objectName:"dockRunning";icon:"apps";type:IconButton.Text;onClicked:dock.showMenu("running",{},runningButton)}
            IconButton {icon:"volume_up";type:IconButton.Text;onClicked:Bridge.navigate("controls")}
            IconButton {objectName:"dockNetwork";icon:Bridge.networkKind==="ethernet"?"lan":Bridge.networkKind==="none"?"wifi_off":"wifi";type:IconButton.Text;onClicked:Bridge.navigate("controls")}
            IconButton {icon:"settings";type:IconButton.Text;onClicked:Bridge.action("settings",{})}
            IconButton{id:powerButton;icon:"power_settings_new";type:IconButton.Text;onClicked:dock.showMenu("power",{},powerButton)}
            Column {Layout.leftMargin:8;Layout.rightMargin:7;StyledText {anchors.horizontalCenter:parent.horizontalCenter;text:Time.format("hh:mm");font.pointSize:Appearance.font.size.small}
StyledText {anchors.horizontalCenter:parent.horizontalCenter;text:Time.format("dd MMM");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3onSurfaceVariant}}
        }
    }
    }
    MouseArea{width:12;anchors.right:parent.right;anchors.top:parent.top;anchors.bottom:parent.bottom;cursorShape:Qt.SizeHorCursor;property real originX;property real originWidth;onPressed:mouse=>{originX=mapToGlobal(mouse.x,mouse.y).x;originWidth=dock.width} onPositionChanged:mouse=>{if(pressed)dock.customWidth=Math.max(560,Math.min(Bridge.screenWidth-24,originWidth+2*(mapToGlobal(mouse.x,mouse.y).x-originX)))} onReleased:Bridge.action("preference",{key:"dockWidth",value:dock.width})}
    Connections {target:Bridge;function onDockChanged(){if(!dock.dragId)dock.dockItems=Bridge.taskbarApps}function onShellEvent(event){if(event==="dock")dock.shown=true;else if(event==="dockHide")dock.shown=false}}
}
