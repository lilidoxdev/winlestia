// Windows dock menus: bounded independent window, no full-screen input surface.
import QtQuick
import QtQuick.Window
import QtQuick.Controls as C
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config

Window {
    id:menu
    title:"Panneaux · Menu du dock"
    transientParent:null
    flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.NoDropShadowWindowHint
    color:"transparent"
    width:390
    height:Math.min(620,Bridge.screenHeight-120,body.implicitHeight+28)
    property string kind:"app"
    property var entry:({})
    property bool fromRunning:false
    property bool backgroundTab:false
    property string powerChoice:""
    property bool confirmStop:false
    property real anchorX:0
    property real anchorY:0
    function openAt(type,data,px,py){if(type==="running")Bridge.action("runningRefresh",{});fromRunning=type==="running";kind=type;powerChoice="";backgroundTab=false;entry=data??({});confirmStop=false;anchorX=px;anchorY=py;position();visible=true;requestActivate()}
    function position(){x=Math.max(Bridge.screenX+8,Math.min(anchorX-width/2,Bridge.screenX+Bridge.screenWidth-width-8));y=Math.max(Bridge.screenY+8,anchorY-height-8)}
    onHeightChanged:if(visible)position()
    // Closes when another window takes focus (event-driven); the pointer poll below only covers clicks that never change focus (e.g. the dock itself).
    property bool wasActive:false
    onActiveChanged:{if(active)wasActive=true;else if(visible&&wasActive){wasActive=false;visible=false}}
    onVisibleChanged:if(!visible)wasActive=false
    onConfirmStopChanged:confirmDialog.open=confirmStop
    Timer{id:focusCheck;objectName:"outsideClickTimer";interval:100;repeat:true;running:menu.visible;onTriggered:if(Bridge.menuOutsideClick(menu.x,menu.y,menu.width,menu.height))menu.visible=false}
    Shortcut {sequence:"Escape";enabled:menu.visible&&!confirmDialog.open;onActivated:menu.visible=false}
    function selectEntry(data){entry=data;confirmStop=false;kind="app"}
    function appAction(action,hwnd){
        if(action==="stop"&&!confirmStop){confirmStop=true;return}
        const selected=entry;if(fromRunning){kind="running";confirmStop=false}else visible=false
        Bridge.action("dockApp",{id:selected.id,hwnd:hwnd??selected.hwnd??0,action:action,confirmed:action==="stop"})
    }
    readonly property var systemItems:[
        {id:"apps",text:qsTr("Applications installées"),icon:"apps"},{id:"power",text:qsTr("Alimentation et veille"),icon:"power_settings_new"},
        {id:"events",text:qsTr("Observateur d’événements"),icon:"event_note"},{id:"system",text:qsTr("Informations système"),icon:"info"},
        {id:"devices",text:qsTr("Gestionnaire de périphériques"),icon:"devices"},{id:"network",text:qsTr("Connexions réseau"),icon:"lan"},
        {id:"disks",text:qsTr("Gestion des disques"),icon:"storage"},{id:"computer",text:qsTr("Gestion de l’ordinateur"),icon:"desktop_windows"},
        {id:"terminal",text:qsTr("Terminal Winlestia"),icon:"terminal"},{id:"terminalAdmin",text:qsTr("Terminal Winlestia — administrateur"),icon:"admin_panel_settings"},
        {id:"taskmgr",text:qsTr("Gestionnaire des tâches"),icon:"monitoring"},{id:"settings",text:qsTr("Paramètres Windows"),icon:"settings"},
        {id:"control",text:qsTr("Panneau de configuration"),icon:"tune"},{id:"explorer",text:qsTr("Explorateur de fichiers"),icon:"folder_open"},
        {id:"run",text:qsTr("Exécuter / rechercher"),icon:"search"},{id:"desktop",text:qsTr("Afficher le bureau"),icon:"desktop_windows"}
    ]
    readonly property var appItems:[
        {id:"open",text:qsTr("Ouvrir une nouvelle fenêtre"),icon:"open_in_new"},
        {id:"admin",text:qsTr("Exécuter en administrateur"),icon:"admin_panel_settings"},
        {id:"pin",text:entry.pinned?"Détacher de la barre des tâches":"Épingler à la barre des tâches",icon:"keep"},
        {id:"favorite",text:entry.favorite?"Retirer des favoris":"Ajouter aux favoris et au dock",icon:"star"},
        {id:"reveal",text:qsTr("Afficher dans l’Explorateur"),icon:"folder_open"},
        {id:"minimize",text:qsTr("Réduire la fenêtre"),icon:"minimize",window:true},
        {id:"maximize",text:qsTr("Agrandir la fenêtre"),icon:"fullscreen",window:true},
        {id:"close",text:qsTr("Fermer cette fenêtre"),icon:"close",window:true},
        {id:"closeAll",text:qsTr("Fermer toutes les fenêtres (")+(entry.count??0)+")",icon:"close_fullscreen",window:true},
        {id:"stop",text:qsTr("Arrêter l’application…"),icon:"stop_circle",stop:true}
    ].filter(item=>(!item.window||(entry.count??0)>0)&&(!item.stop||entry.canStop))
    Rectangle {
        anchors.fill:parent;radius:Appearance.rounding.large;color:Qt.alpha(Colours.surfaceBase,.96);border.width:1;border.color:Colours.palette.m3outlineVariant
        C.ScrollView {
            anchors.fill:parent;anchors.margins:14;clip:true;contentWidth:availableWidth
            Column {
                id:body;width:menu.width-28;spacing:3
                StyledText {width:parent.width-12;x:6;text:menu.kind==="system"?"Windows · Winlestia":menu.kind==="power"?"Alimentation":menu.kind==="running"?"Applications":menu.entry.name??"Application";font.pointSize:Appearance.font.size.normal;elide:Text.ElideRight;topPadding:5;bottomPadding:9}
                IconTextButton{visible:menu.fromRunning&&menu.kind==="app";text:qsTr("Retour");icon:"arrow_back";type:IconTextButton.Text;onClicked:{menu.kind="running";menu.confirmStop=false}}
                StyledText {visible:menu.kind==="app";width:parent.width-12;x:6;text:(menu.entry.background?"En arrière-plan":(menu.entry.count??0)+" fenêtre(s)")+"\n"+(menu.entry.path??"");font.pointSize:Appearance.font.size.small;wrapMode:Text.WrapAnywhere;color:Colours.palette.m3onSurfaceVariant;bottomPadding:8;maximumLineCount:3;elide:Text.ElideRight}
                            Row {visible:menu.kind==="running";spacing:4
                    IconTextButton{text:qsTr("En cours");checked:!menu.backgroundTab;onClicked:menu.backgroundTab=false}
                    IconTextButton{text:qsTr("Arrière-plan");checked:menu.backgroundTab;onClicked:menu.backgroundTab=true}
                }
                Repeater {model:menu.kind==="power"?(menu.powerChoice===""?[{id:"sleep",text:qsTr("Mettre en veille"),icon:"bedtime"},{id:"restart",text:qsTr("Redémarrer"),icon:"restart_alt"},{id:"shutdown",text:qsTr("Éteindre"),icon:"power_settings_new"},{id:"quitWinlestia",text:qsTr("Fermer Winlestia"),icon:"logout"}]:[{id:"confirm",text:qsTr("Confirmer"),icon:"check"},{id:"cancel",text:qsTr("Annuler"),icon:"close"}]):[]
                    StyledRect{required property var modelData;objectName:"power-"+modelData.id;width:body.width;height:54;radius:Appearance.rounding.normal;color:Colours.palette.m3surfaceContainer
                        StateLayer{function onClicked(){if(parent.modelData.id==="quitWinlestia"){Bridge.action("quit",{});menu.visible=false}else if(parent.modelData.id==="cancel")menu.powerChoice="";else if(parent.modelData.id==="confirm"){Bridge.action("systemAction",{id:menu.powerChoice});menu.visible=false}else menu.powerChoice=parent.modelData.id}}
                        RowLayout{anchors.fill:parent;anchors.margins:14;spacing:14;MaterialIcon{text:parent.parent.modelData.icon;font.pointSize:Appearance.font.size.title;color:Colours.palette.m3primary}StyledText{Layout.fillWidth:true;text:parent.parent.modelData.text;font.pointSize:Appearance.font.size.normal}}
                    }
                }
                Repeater {
                    model:menu.kind==="system"?menu.systemItems:menu.kind==="app"?menu.appItems:[]
                    delegate:StyledRect {
                        id:row;required property var modelData;width:body.width;height:38;radius:Appearance.rounding.small;color:"transparent"
                        StateLayer {function onClicked(){
                                                        if(menu.kind==="system"){menu.visible=false;if(row.modelData.id==="run")Bridge.navigate("palette");else Bridge.action("systemAction",{id:row.modelData.id})}
                            else menu.appAction(row.modelData.id)
                        }}
                        RowLayout {anchors.fill:parent;anchors.leftMargin:9;anchors.rightMargin:8;spacing:10;MaterialIcon{text:row.modelData.icon;font.pointSize:Appearance.font.size.large;color:row.modelData.id==="stop"?Colours.palette.m3error:Colours.palette.m3primary} StyledText{Layout.fillWidth:true;text:row.modelData.text;font.pointSize:Appearance.font.size.small;elide:Text.ElideRight}}
                    }
                }
                Repeater {
                    model:menu.kind==="app"?(menu.entry.windows??[]):[]
                    delegate:StyledRect {id:windowRow;required property var modelData;width:body.width;height:36;radius:Appearance.rounding.small;color:"transparent"
                        StateLayer{function onClicked(){menu.appAction("activate",windowRow.modelData.hwnd)}}
                        StyledText{anchors.fill:parent;anchors.leftMargin:10;verticalAlignment:Text.AlignVCenter;text:windowRow.modelData.title;font.pointSize:Appearance.font.size.small;elide:Text.ElideRight;color:Colours.palette.m3onSurfaceVariant}
                    }
                }
                StyledText {visible:menu.kind==="running"&&Bridge.runningApps.length===0;text:qsTr("Aucune application détectée");font.pointSize:Appearance.font.size.small}
                Grid {
                    objectName:"runningGrid";visible:menu.kind==="running";columns:5;spacing:8;width:body.width
                    Repeater {model:menu.kind==="running"?Bridge.runningApps.filter(a=>!!a.background===menu.backgroundTab):[]
                        StyledRect{id:runningCell;required property var modelData;objectName:"running-cell-"+modelData.id;width:(body.width-32)/5;height:62;radius:Appearance.rounding.normal;color:hover.containsMouse?Colours.palette.m3secondaryContainer:Colours.palette.m3surfaceContainer
                            Image{id:appLogo;anchors.centerIn:parent;width:32;height:32;source:Bridge.iconUrl(runningCell.modelData.id,Bridge.iconRevision);asynchronous:true;fillMode:Image.PreserveAspectFit}
                            MaterialIcon{anchors.centerIn:parent;text:"apps";visible:appLogo.status!==Image.Ready;color:Colours.palette.m3primary}
                            MouseArea{id:hover;anchors.fill:parent;hoverEnabled:true;acceptedButtons:Qt.LeftButton|Qt.RightButton;onClicked:menu.selectEntry(runningCell.modelData)}
                            C.ToolTip.visible:hover.containsMouse;C.ToolTip.text:modelData.name;C.ToolTip.delay:350
                        }
                    }
                }

            }
        }
    }
    ConfirmDialog {
        id:confirmDialog;cardWidth:340;destructive:true
        title:qsTr("Arrêter cette application ?")
        message:qsTr("« ")+(menu.entry.name??"")+qsTr(" » sera arrêté. Les modifications non enregistrées seront perdues.")
        confirmText:qsTr("Arrêter l’application")
        onConfirmed:menu.appAction("stop")
        onClosed:menu.confirmStop=false
    }
}
