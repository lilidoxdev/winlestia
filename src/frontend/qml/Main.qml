// Native Windows host for Caelestia views. One navigation state owns dismissal and focus.
import QtQuick
import QtQuick.Window
import QtQuick.Shapes
import Quickshell
import qs.config
import qs.services
import qs.components
import qs.modules.dashboard as Dashboard

Window {
    id:window
    title:"Panneaux · Caelestia"
    color:"transparent"
    flags:(Bridge.preview?Qt.Window:Qt.Tool)|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.NoDropShadowWindowHint
    property bool peek:Bridge.preview
    property string pageMode:"dashboard"
    property bool extraOpen:false
    property bool mixerOpen:false
    property int menuDuration:Math.round(Appearance.anim.durations.expressiveFastSpatial*(["audio","microphone","mixer","controls"].includes(pageMode)?.6:1))
    property real mixerExtent:mixerOpen?Math.min(430,Bridge.screenWidth-520):0
    property bool audioSampling:extraOpen&&(pageMode==="audio"||pageMode==="microphone"||pageMode==="mixer"||(pageMode==="controls"&&mixerOpen))
    onAudioSamplingChanged:subscribe()
    Behavior on mixerExtent{Anim{duration:window.menuDuration;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
    property bool controlsOpen:pageMode==="controls"&&extraOpen
    property real extraHeight:extraOpen?extraBody.implicitHeight+36:0
    property real contentWidth:pageMode==="workspace"?Math.min(1220,Bridge.screenWidth-80):pageMode==="palette"?660:pageMode==="settings"?690:pageMode==="clipboard"?730:pageMode==="tools"?760:pageMode==="audio"||pageMode==="microphone"?650:pageMode==="mixer"?490:pageMode==="capture"?620:450+(pageMode==="controls"?mixerExtent:0)

    property real panelWidth:Math.ceil((pageMode==="dashboard"?Math.max(600,drawer.implicitWidth||854):contentWidth)+40)
    property real panelHeight:Math.ceil(Math.max(1,pageMode==="dashboard"?(vis.dashboard?drawer.implicitHeight:peek?82:0):extraHeight))
    width:Math.max(Math.min(1600,Bridge.screenWidth-16),Math.ceil(panelWidth))
    height:Math.max(Math.min(900,Bridge.screenHeight-Config.border.thickness),Math.ceil(panelHeight))
    Behavior on panelWidth {Anim{duration:Math.round(250*(Bridge.settings.motionScale??1));easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
    Behavior on panelHeight {Anim{duration:Math.round(260*(Bridge.settings.motionScale??1));easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
    onPanelWidthChanged:Bridge.updateSurfaces()
    onPanelHeightChanged:Bridge.updateSurfaces()
    property int dashboardTab:drawer.dashState.currentTab
    x:Bridge.screenX+(Bridge.screenWidth-width)/2
    y:Bridge.screenY+Math.max(0,Config.border.thickness)
    visible:panelHeight>1
    property int servicePage:pageMode==="dashboard"?drawer.dashState.currentTab:pageMode==="controls"?4:pageMode==="clipboard"?6:pageMode==="audio"||pageMode==="microphone"||pageMode==="mixer"?7:5
    function subscribe(){Bridge.subscribe(pageMode==="dashboard"?vis.dashboard:extraOpen,servicePage)}
    function openMode(mode){
        if(mode==="close"){vis.dashboard=false;peek=false;extraOpen=false;mixerOpen=false;subscribe();return}
        if(mode==="toggle"){if(visible&&pageMode==="dashboard"&&vis.dashboard){openMode("close");return}mode="dashboard"}
        if(mode==="dashboard"||mode==="media"||mode==="performance"||mode==="access"){
            extraOpen=false;pageMode="dashboard";drawer.dashState.currentTab=mode==="media"?1:mode==="performance"?2:mode==="access"?3:0;vis.dashboard=true;peek=true
        }else{
            mixerOpen=false;vis.dashboard=false;peek=false;pageMode=mode.split(":")[0];extraOpen=true
            if(pageMode==="palette")Qt.callLater(()=>palette.reset())
        }
        subscribe();Bridge.activate()
    }
    onVisibleChanged:subscribe()
    onControlsOpenChanged:subscribe()
    Shortcut {sequence:"Escape";enabled:window.visible;onActivated:Bridge.closePanel()}
    Shortcut {sequence:"Ctrl+1";enabled:window.visible&&window.pageMode!=="settings";onActivated:Bridge.navigate("workspace:apps")}
    Shortcut {sequence:"Ctrl+2";enabled:window.visible&&window.pageMode!=="settings";onActivated:Bridge.navigate("workspace:games")}
    Shortcut {sequence:"Ctrl+3";enabled:window.visible&&window.pageMode!=="settings";onActivated:Bridge.navigate("workspace:projects")}
    Shortcut {sequence:"Ctrl+4";enabled:window.visible&&window.pageMode!=="settings";onActivated:Bridge.navigate("workspace:documents")}
    Shortcut {sequence:"Ctrl+H";enabled:window.visible&&window.pageMode!=="settings";onActivated:Bridge.navigate("dashboard")}

    PersistentProperties {id:vis;property bool dashboard:false;property bool launcher:false}
    Binding {target:Config.appearance.anim.durations;property:"scale";value:Bridge.settings.motionScale??1}
    // All backgrounds are drawn once by the shared screen surface.
    Dashboard.Wrapper {id:drawer;y:Math.min(0,window.panelHeight-implicitHeight);anchors.horizontalCenter:parent.horizontalCenter;visibilities:vis;visible:window.pageMode==="dashboard"&&(vis.dashboard||(!window.peek&&window.panelHeight>1))}
    Dashboard.Tabs {anchors.top:parent.top;anchors.topMargin:15;anchors.horizontalCenter:parent.horizontalCenter;width:window.panelWidth-70;nonAnimWidth:width;state:drawer.dashState;visible:window.pageMode==="dashboard"&&window.peek&&!vis.dashboard}
    Item {
        id:extraBody
        anchors.top:parent.top;anchors.topMargin:18;anchors.horizontalCenter:parent.horizontalCenter;width:window.panelWidth-80
        implicitHeight:window.pageMode==="workspace"?browser.implicitHeight:window.pageMode==="palette"?palette.implicitHeight:window.pageMode==="settings"?preferences.implicitHeight:window.pageMode==="clipboard"?clipboard.implicitHeight:window.pageMode==="tools"?tools.implicitHeight:window.pageMode==="audio"||window.pageMode==="microphone"?audio.implicitHeight:window.pageMode==="mixer"?fullMixer.implicitHeight:window.pageMode==="capture"?capturePane.implicitHeight:Math.max(controls.implicitHeight,window.mixerOpen?sideMixer.implicitHeight:0)
        visible:window.pageMode!=="dashboard";clip:true
        opacity:window.extraOpen?1:0
        Behavior on opacity {Anim {duration:Appearance.anim.durations.small}}
        WindowsControls {id:controls;width:410;visible:window.pageMode==="controls";mixerOpen:window.mixerOpen;onToggleMixer:window.mixerOpen=!window.mixerOpen}
        Item{x:430;width:Math.max(0,window.mixerExtent-20);height:sideMixer.implicitHeight;visible:window.pageMode==="controls"&&window.mixerExtent>1;clip:true
            MixerPane{id:sideMixer;width:Math.max(320,window.mixerExtent-20);opacity:window.mixerOpen?1:0;x:window.mixerOpen?0:24;onDismiss:window.mixerOpen=false
                Behavior on opacity{Anim{duration:Appearance.anim.durations.small}} Behavior on x{Anim{duration:Appearance.anim.durations.expressiveFastSpatial}}
            }
        }
        ToolsPane{id:tools;width:parent.width;visible:window.pageMode==="tools"}
        AudioPane{id:audio;width:parent.width;visible:window.pageMode==="audio"||window.pageMode==="microphone";microphone:window.pageMode==="microphone"}
        MixerPane{id:fullMixer;width:parent.width;visible:window.pageMode==="mixer";standalone:true;onDismiss:Bridge.closePanel()}
        CapturePane{id:capturePane;width:parent.width;visible:window.pageMode==="capture"}
        WorkspaceBrowser {id:browser;width:parent.width;visible:window.pageMode==="workspace"}
        PalettePane {id:palette;width:parent.width;visible:window.pageMode==="palette"}
        ShellPreferences {id:preferences;width:parent.width;visible:window.pageMode==="settings"}
        ClipboardPane {id:clipboard;width:parent.width;visible:window.pageMode==="clipboard"}
    }
    Connections {target:drawer.dashState;function onCurrentTabChanged(){window.subscribe()}}
    Connections {
        target:Bridge
        function onNavigationRequested(mode){window.openMode(mode)}
        function onTabRequested(index){window.pageMode="dashboard";window.extraOpen=false;drawer.dashState.currentTab=index;vis.dashboard=true;window.peek=true;window.subscribe();Bridge.activate()}
        function onShellEvent(event){if(event==="peek"&&!vis.dashboard&&!window.extraOpen){window.pageMode="dashboard";window.peek=true}}
        function onNoticeChanged(){noticeTimer.restart()}
    }
    Timer{id:noticeTimer;interval:5000}
    StyledRect {
        anchors.bottom:parent.bottom;anchors.bottomMargin:window.height-window.panelHeight+14;anchors.horizontalCenter:parent.horizontalCenter
        width:Math.min(window.panelWidth-80,640);height:noticeLabel.implicitHeight+22;radius:18
        color:Colours.palette.m3surfaceContainerHighest;border.width:1;border.color:Colours.palette.m3outlineVariant
        opacity:noticeTimer.running?1:0;visible:opacity>.01
        Behavior on opacity{Anim{duration:Appearance.anim.durations.small}}
        StyledText{id:noticeLabel;anchors.centerIn:parent;width:parent.width-26;text:Bridge.notice;wrapMode:Text.Wrap;font.pointSize:11}
    }
    Repeater{model:Bridge.frameScreens;delegate:Item{id:frameHolder;required property var modelData;ScreenFrame{modelData:frameHolder.modelData}DesktopLogo{modelData:frameHolder.modelData}}}
    CaptureWindow{}
    Dock {objectName:"bottomDock"}
}
