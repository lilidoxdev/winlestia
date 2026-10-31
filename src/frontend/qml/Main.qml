// Native Windows host for Caelestia views. One navigation state owns dismissal and focus.
import QtQuick
import QtQuick.Window
import QtQuick.Shapes
import QtQml
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
    property bool wifiOpen:false
    property real wifiExtent:wifiOpen?Math.min(430,Bridge.screenWidth-520):0
    property bool wifiSampling:extraOpen&&pageMode==="controls"&&wifiOpen
    onMixerOpenChanged:if(mixerOpen)wifiOpen=false
    onWifiOpenChanged:if(wifiOpen)mixerOpen=false
    onWifiSamplingChanged:subscribe()
    Behavior on wifiExtent{enabled:!Appearance.reducedEffects;Anim{duration:window.menuDuration;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
    property int menuDuration:Math.round(Appearance.anim.durations.expressiveFastSpatial*(["audio","microphone","mixer","controls"].includes(pageMode)?.6:1))
    property real mixerExtent:mixerOpen?Math.min(430,Bridge.screenWidth-520):0
    property bool audioSampling:extraOpen&&(pageMode==="audio"||pageMode==="microphone"||pageMode==="mixer"||(pageMode==="controls"&&mixerOpen))
    onAudioSamplingChanged:subscribe()
    Behavior on mixerExtent{enabled:!Appearance.reducedEffects;Anim{duration:window.menuDuration;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
    property bool controlsOpen:pageMode==="controls"&&extraOpen
    property real extraHeight:extraOpen?extraBody.implicitHeight+36:0
    property real contentWidth:pageMode==="workspace"?Math.min(1220,Bridge.screenWidth-80):pageMode==="palette"?(palette.item&&palette.item.wide?Math.min(1320,Bridge.screenWidth-80):660):pageMode==="settings"?Math.min(900,Bridge.screenWidth-80):pageMode==="clipboard"?730:pageMode==="tools"?760:pageMode==="audio"||pageMode==="microphone"?650:pageMode==="mixer"?490:pageMode==="capture"?620:450+(pageMode==="controls"?mixerExtent+wifiExtent:0)

    property real panelWidth:Math.ceil((pageMode==="dashboard"?Math.max(600,drawer.implicitWidth||854):contentWidth)+40)
    property real panelHeight:Bridge.fileDragNearTop?0:Math.ceil(Math.max(1,pageMode==="dashboard"?(vis.dashboard?drawer.implicitHeight:peek?82:0):extraHeight))
    width:Math.max(Math.min(1600,Bridge.screenWidth-16),Math.ceil(panelWidth))
    height:Math.max(Math.min(900,Bridge.screenHeight-Config.border.thickness),Math.ceil(panelHeight))
    Behavior on panelWidth {enabled:!Appearance.reducedEffects;Anim{duration:Math.round(250*(Bridge.settings.motionScale??1));easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
    Behavior on panelHeight {enabled:!Appearance.reducedEffects;Anim{duration:Math.round(260*(Bridge.settings.motionScale??1));easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
    onPanelWidthChanged:Bridge.updateSurfaces()
    onPanelHeightChanged:Bridge.updateSurfaces()
    property int dashboardTab:drawer.dashState.currentTab
    x:Bridge.screenX+(Bridge.screenWidth-width)/2
    y:Bridge.screenY+Math.max(0,Config.border.thickness)
    visible:panelHeight>1&&!Bridge.screenFullscreen
    property int servicePage:pageMode==="dashboard"?drawer.dashState.currentTab:pageMode==="controls"?4:pageMode==="clipboard"?6:pageMode==="audio"||pageMode==="microphone"||pageMode==="mixer"?7:5
    function subscribe(){Bridge.subscribe(pageMode==="dashboard"?vis.dashboard:extraOpen,servicePage)}
    function openMode(mode){
        if(mode==="close"){vis.dashboard=false;peek=false;extraOpen=false;mixerOpen=false;wifiOpen=false;subscribe();return}
        if(mode==="toggle"){if(visible&&pageMode==="dashboard"&&vis.dashboard){openMode("close");return}mode="dashboard"}
        if(mode==="dashboard"||mode==="media"||mode==="performance"||mode==="access"){
            extraOpen=false;pageMode="dashboard";drawer.dashState.currentTab=mode==="media"?1:mode==="performance"?2:mode==="access"?3:0;vis.dashboard=true;peek=true
        }else{
            mixerOpen=false;wifiOpen=false;vis.dashboard=false;peek=false;pageMode=mode.split(":")[0];extraOpen=true
            if(pageMode==="palette")Qt.callLater(()=>{if(palette.item)palette.item.reset()})
            if(pageMode==="settings"&&mode==="settings:social"){if(preferences.item)preferences.item.tab=4;else preferences.pendingTab=4}
            if(pageMode==="settings"&&mode==="settings:bookmarks"){if(preferences.item)preferences.item.tab=5;else preferences.pendingTab=5}
        }
        subscribe();Bridge.activate()
    }
    onVisibleChanged:{if(!visible)sessionDialog.open=false;subscribe()}
    onExtraOpenChanged:if(!extraOpen)sessionDialog.open=false
    onPageModeChanged:sessionDialog.open=false
    onControlsOpenChanged:subscribe()
    Shortcut {sequence:"Escape";enabled:window.visible&&!sessionDialog.open&&!(browser.item&&browser.item.dialogOpen);onActivated:{if(window.pageMode==="controls"&&window.wifiOpen)window.wifiOpen=false;else Bridge.closePanel()}}
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
        implicitHeight:Math.max(sessionDialog.open?260:0,window.pageMode==="workspace"?browser.implicitHeight:window.pageMode==="palette"?palette.implicitHeight:window.pageMode==="settings"?preferences.implicitHeight:window.pageMode==="clipboard"?clipboard.implicitHeight:window.pageMode==="tools"?tools.implicitHeight:window.pageMode==="audio"||window.pageMode==="microphone"?audio.implicitHeight:window.pageMode==="mixer"?fullMixer.implicitHeight:window.pageMode==="capture"?capturePane.implicitHeight:Math.max(controls.implicitHeight,window.mixerOpen?sideMixer.implicitHeight:0,window.wifiOpen?sideWifi.implicitHeight:0))
        visible:window.pageMode!=="dashboard";clip:true
        opacity:window.extraOpen?1:0
        Behavior on opacity {enabled:!Appearance.reducedEffects;Anim {duration:Appearance.anim.durations.small}}
        WindowsControls {id:controls;width:410;visible:window.pageMode==="controls";mixerOpen:window.mixerOpen;wifiOpen:window.wifiOpen;onToggleMixer:window.mixerOpen=!window.mixerOpen;onToggleWifi:window.wifiOpen=!window.wifiOpen}
        Item{x:430;width:Math.max(0,window.mixerExtent-20);height:sideMixer.implicitHeight;visible:window.pageMode==="controls"&&window.mixerExtent>1;clip:true
            Loader{id:sideMixer;width:Math.max(320,window.mixerExtent-20);opacity:window.mixerOpen?1:0;x:window.mixerOpen?0:24;property bool once:false;property bool want:window.pageMode==="controls"&&(window.mixerOpen||window.mixerExtent>1);onWantChanged:if(want)once=true;active:want||once
                sourceComponent:MixerPane{onDismiss:window.mixerOpen=false}
                Behavior on opacity{enabled:!Appearance.reducedEffects;Anim{duration:Appearance.anim.durations.small}} Behavior on x{enabled:!Appearance.reducedEffects;Anim{duration:Appearance.anim.durations.expressiveFastSpatial}}
            }
        }
        Item{x:430;width:Math.max(0,window.wifiExtent-20);height:sideWifi.implicitHeight;visible:window.pageMode==="controls"&&window.wifiExtent>1;clip:true
            Loader{id:sideWifi;width:Math.max(320,window.wifiExtent-20);opacity:window.wifiOpen?1:0;x:window.wifiOpen?0:24;property bool once:false;property bool want:window.pageMode==="controls"&&(window.wifiOpen||window.wifiExtent>1);onWantChanged:if(want)once=true;active:want||once
                sourceComponent:WifiPane{onDismiss:window.wifiOpen=false}
                Behavior on opacity{enabled:!Appearance.reducedEffects;Anim{duration:Appearance.anim.durations.small}} Behavior on x{enabled:!Appearance.reducedEffects;Anim{duration:Appearance.anim.durations.expressiveFastSpatial}}
            }
        }
        // Keep-alive lazy panes: instantiated on first show, then kept loaded. Workspace/Palette are also preloaded asynchronously after idle.
        component PaneLoader:Loader{property bool want:false;property bool once:false;property bool warm:false;width:parent.width;visible:want&&item!==null;active:want||once||warm;onWantChanged:if(want)once=true;Component.onCompleted:if(want)once=true}
        Timer{id:warmTimer;interval:5000;running:true;repeat:false;onTriggered:{browser.warm=true;palette.warm=true}}
        PaneLoader{id:tools;want:window.pageMode==="tools";sourceComponent:ToolsPane{}}
        PaneLoader{id:audio;want:window.pageMode==="audio"||window.pageMode==="microphone";sourceComponent:AudioPane{microphone:window.pageMode==="microphone"}}
        PaneLoader{id:fullMixer;want:window.pageMode==="mixer";sourceComponent:MixerPane{standalone:true;onDismiss:Bridge.closePanel()}}
        PaneLoader{id:capturePane;want:window.pageMode==="capture";sourceComponent:CapturePane{}}
        PaneLoader{id:browser;want:window.pageMode==="workspace";asynchronous:true;sourceComponent:WorkspaceBrowser{}}
        PaneLoader{id:palette;want:window.pageMode==="palette";asynchronous:true;sourceComponent:PalettePane{}
            onLoaded:if(want)item.reset()}
        PaneLoader{id:preferences;want:window.pageMode==="settings";property int pendingTab:-1;sourceComponent:ShellPreferences{}
            onLoaded:if(pendingTab>=0){item.tab=pendingTab;pendingTab=-1}}
        PaneLoader{id:clipboard;want:window.pageMode==="clipboard";sourceComponent:ClipboardPane{}}
        ConfirmDialog {
            id:sessionDialog;cardWidth:340;destructive:true;property string choice:""
            function label(id){return id==="lock"?qsTr("Verrouiller"):id==="logout"?qsTr("Se déconnecter"):id==="sleep"?qsTr("Mettre en veille"):id==="hibernate"?qsTr("Hibernation"):id==="restart"?qsTr("Redémarrer"):id==="shutdown"?qsTr("Éteindre"):""}
            title:qsTr("%1 ?").arg(label(choice))
            message:choice==="lock"?qsTr("Ta session sera verrouillée."):qsTr("Les modifications non enregistrées seront perdues.")
            confirmText:label(choice)
            onConfirmed:{const id=choice;Bridge.closePanel();Bridge.action("systemAction",{id:id,confirmed:true})}
        }
        Connections {target:Bridge;function onSessionRequested(id){sessionDialog.choice=id;sessionDialog.open=true}}
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
        Behavior on opacity{enabled:!Appearance.reducedEffects;Anim{duration:Appearance.anim.durations.small}}
        StyledText{id:noticeLabel;anchors.centerIn:parent;width:parent.width-26;text:qsTr(Bridge.notice);wrapMode:Text.Wrap;font.pointSize:11}
    }
    Loader{id:recycleLoader;property bool once:false;active:Bridge.fileDragNearTop||once;onLoaded:once=true;sourceComponent:RecycleDrop{}}
    Repeater{model:Bridge.frameScreens;delegate:Item{id:frameHolder;required property var modelData;ScreenFrame{modelData:frameHolder.modelData}Instantiator{model:1;active:Bridge.settings.desktopLogo??false;delegate:DesktopLogo{modelData:frameHolder.modelData}}}}
    CaptureWindow{}
    DictationOverlay{dock:bottomDock}
    Loader{id:wallpaperSwitcher;active:false;sourceComponent:WallpaperSwitcher{onCloseRequested:wallpaperSwitcher.active=false}}
    Connections{target:Bridge;function onShellEvent(event){if(event==="wallpaper-switcher")wallpaperSwitcher.active=!wallpaperSwitcher.active}}
    Loader{id:osdLoader;active:Bridge.settings.osd??true;sourceComponent:Osd{}}
    Dock {id:bottomDock;objectName:"bottomDock"}
    SocialRail {}
    BookmarksRail {}
}
