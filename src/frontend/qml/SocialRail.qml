// Right-edge hidden rail of social networks: an EdgeRail instance with a history drawer.
import QtQuick
import QtQuick.Window
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.config
import qs.services

EdgeRail {
    id:rail
    side:"right"
    title:"Panneaux · Réseaux sociaux"
    objectName:"socialRail"
    contentW:54
    chevronW:28
    panelW:340
    contentHeight:column.implicitHeight
    readonly property var networks:(Bridge.social.networks??[]).filter(n=>n.enabled)
    readonly property bool globalMuted:Bridge.social.muted??false
    // Left to right: logos | history drawer | chevron | frame band.
    Item {
        anchors.fill:parent
        transform:Translate {x:rail.offset}
        ColumnLayout {
            id:column
            x:rail.width-rail.contentBump+Appearance.padding.large;y:(parent.height-height)/2;spacing:Appearance.spacing.normal
            Repeater {
                model:rail.networks
                Item {
                    id:logo
                    required property var modelData
                    objectName:"social-"+modelData.id
                    readonly property bool muted:rail.globalMuted||(modelData.muted??false)
                    readonly property int count:modelData.count??0
                    readonly property bool connected:modelData.connected??false
                    Layout.preferredWidth:54;Layout.preferredHeight:54;Layout.alignment:Qt.AlignHCenter
                    Image {
                        anchors.fill:parent;fillMode:Image.PreserveAspectFit;smooth:true;asynchronous:true
                        sourceSize.width:108;sourceSize.height:108;opacity:logo.connected&&!logo.muted?1:.55
                        source:Bridge.localUrl(Bridge.assetRoot+"/social/"+logo.modelData.id+".svg")
                    }
                    StateLayer {radius:width/2;function onClicked(){Bridge.action(logo.connected?"socialOpen":"socialLogin",{id:logo.modelData.id});rail.shown=false}}
                    HoverHandler {id:hover}
                    C.ToolTip {visible:hover.hovered;text:logo.modelData.name;delay:500;popupType:C.Popup.Window}
                    // Unread counter: top-left, hidden at 0, scale-in.
                    Rectangle {
                        objectName:"badge-"+logo.modelData.id
                        property string label:logo.count>99?"99+":String(logo.count)
                        x:-4;y:-8;height:22;width:Math.max(22,badgeText.implicitWidth+10);radius:11
                        color:Colours.palette.m3error
                        scale:logo.connected&&logo.count>0?1:0;visible:scale>.01;transformOrigin:Item.Center
                        Behavior on scale {Anim {duration:Appearance.anim.durations.small;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
                        StyledText {id:badgeText;objectName:"badgeText-"+logo.modelData.id;anchors.centerIn:parent;text:parent.label;color:Colours.palette.m3onError;font.pointSize:Appearance.font.size.small;font.bold:true}
                    }
                    // Muted indicator: small crossed bell, bottom-right.
                    Rectangle {
                        objectName:"muted-"+logo.modelData.id
                        visible:logo.muted||!logo.connected;width:20;height:20;radius:10;x:logo.width-width+4;y:logo.height-height+4
                        color:Colours.palette.m3surfaceContainerHighest;border.width:1;border.color:Colours.palette.m3outlineVariant
                        MaterialIcon {anchors.centerIn:parent;text:logo.muted?"notifications_off":"link_off";font.pointSize:Appearance.font.size.small;color:Colours.palette.m3onSurfaceVariant}
                    }
                }
            }
            Rectangle {visible:rail.networks.length>0;Layout.alignment:Qt.AlignHCenter;implicitWidth:34;implicitHeight:1;color:Colours.palette.m3outlineVariant}
            IconButton {objectName:"socialGear";Layout.alignment:Qt.AlignHCenter;icon:"settings";type:IconButton.Text;onClicked:{Bridge.action("settings",{section:"social"});rail.shown=false}}
        }
    }
    // Chevron: right of the logo column (frame side), toggles the history drawer.
    Item {
        id:chevron
        objectName:"socialChevron"
        width:rail.chevronW;height:56;y:(parent.height-height)/2
        x:rail.width-width+rail.offset
        StateLayer {radius:Appearance.rounding.small;function onClicked(){rail.expanded=!rail.expanded}}
        MaterialIcon {anchors.centerIn:parent;text:rail.expanded?"chevron_right":"chevron_left";font.pointSize:22;color:Colours.palette.m3onSurfaceVariant}
    }
    // History drawer between logos and chevron: clipped container, content fixed in size and fading in once the bump is mostly open.
    Item {
        id:drawerClip
        objectName:"socialDrawer"
        clip:true
        readonly property real leftEdge:column.x+54+Appearance.padding.large
        x:leftEdge+rail.offset;width:Math.max(0,chevron.x-rail.offset-Appearance.padding.small-leftEdge)
        y:(parent.height-rail.bumpHeight)/2+rail.fillet+Appearance.padding.large;height:Math.max(0,rail.bumpHeight-2*(rail.fillet+Appearance.padding.large))
        opacity:Appearance.reducedEffects?(rail.expanded?1:0):Math.max(0,Math.min(1,(rail.reveal-.55)/.45))
        Loader {
            id:drawer
            active:rail.expanded||rail.reveal>.01
            anchors.right:parent.right;anchors.top:parent.top
            width:rail.panelW-2*Appearance.padding.large;height:rail.fullHeight-2*(rail.fillet+Appearance.padding.large)
            sourceComponent:historyPanel
        }
    }
    Component {
        id:historyPanel
        ColumnLayout {
            id:panel
            objectName:"socialHistory"
            spacing:Appearance.spacing.small
            property real nowMs:Date.now()
            ListModel {id:hist}
            function sync(list){
                list=list??[]
                if(list.length===0){hist.clear();return}
                const seqs={};for(const e of list)seqs[e.seq]=true
                for(let i=hist.count-1;i>=0;i--)if(!seqs[hist.get(i).seq])hist.remove(i)
                // Same seq can come back changed (count -> notif): update in place.
                const index={};for(let i=0;i<hist.count;i++)index[hist.get(i).seq]=i
                for(const e of list){
                    const i=index[e.seq]
                    if(i===undefined)continue
                    const cur=hist.get(i)
                    if(cur.title!==(e.title??"")||cur.body!==(e.body??"")||cur.kind!==(e.kind??"")||cur.net!==(e.net??"")||cur.at!==(e.at??0))hist.set(i,{net:e.net??"",title:e.title??"",body:e.body??"",kind:e.kind??"",at:e.at??0})
                }
                const have=hist.count>0?hist.get(0).seq:-1
                let n=0
                for(const e of list)if(e.seq>have)hist.insert(n++,{seq:e.seq,net:e.net??"",title:e.title??"",body:e.body??"",kind:e.kind??"",at:e.at??0})
                if(hist.count===0)for(const e of list)hist.append({seq:e.seq,net:e.net??"",title:e.title??"",body:e.body??"",kind:e.kind??"",at:e.at??0})
            }
            function rel(at){
                const d=panel.nowMs-at
                if(d<60000)return qsTr("à l’instant")
                if(d<3600000)return qsTr("il y a %1 min").arg(Math.floor(d/60000))
                return Qt.formatDateTime(new Date(at),"HH:mm")
            }
            Component.onCompleted:sync(Bridge.social.history)
            Connections {target:Bridge;function onChanged(){panel.sync(Bridge.social.history)}}
            // Relative-time refresh: only while the drawer exists and the rail is on screen.
            Timer {interval:60000;repeat:true;running:rail.visible&&rail.expanded;onTriggered:panel.nowMs=Date.now()}
            onVisibleChanged:nowMs=Date.now()
            RowLayout {
                Layout.fillWidth:true
                StyledText {Layout.fillWidth:true;text:qsTr("Historique");font.pointSize:Appearance.font.size.larger;font.bold:true}
                IconButton {objectName:"socialHistoryClear";icon:"delete_sweep";type:IconButton.Text;onClicked:Bridge.action("socialHistoryClear",{})}
            }
            StyledText {objectName:"socialHistoryEmpty";visible:hist.count===0;Layout.fillWidth:true;Layout.topMargin:Appearance.padding.large;wrapMode:Text.WordWrap;horizontalAlignment:Text.AlignHCenter;text:qsTr("Aucune notification depuis l’ouverture de session");color:Colours.palette.m3outline;font.pointSize:Appearance.font.size.small}
            ListView {
                id:list
                objectName:"socialHistoryList"
                Layout.fillWidth:true;Layout.fillHeight:true;clip:true;spacing:Appearance.spacing.small;boundsBehavior:Flickable.StopAtBounds
                model:hist
                delegate:Item {
                    id:entry
                    required property int index
                    required property string net
                    required property string title
                    required property string body
                    required property real at
                    objectName:"history-"+index
                    width:ListView.view.width;implicitHeight:row.implicitHeight+2*Appearance.padding.smaller;height:implicitHeight
                    StateLayer {radius:Appearance.rounding.normal;function onClicked(){Bridge.action("socialOpen",{id:entry.net});rail.expanded=false;rail.shown=false}}
                    RowLayout {
                        id:row
                        anchors.fill:parent;anchors.margins:Appearance.padding.smaller;spacing:Appearance.spacing.small
                        Image {Layout.preferredWidth:24;Layout.preferredHeight:24;Layout.alignment:Qt.AlignTop;sourceSize.width:48;sourceSize.height:48;fillMode:Image.PreserveAspectFit;asynchronous:true;source:Bridge.localUrl(Bridge.assetRoot+"/social/"+entry.net+".svg")}
                        ColumnLayout {
                            Layout.fillWidth:true;spacing:0
                            StyledText {Layout.fillWidth:true;text:entry.title;font.bold:true;elide:Text.ElideRight;font.pointSize:Appearance.font.size.small}
                            StyledText {Layout.fillWidth:true;visible:entry.body!=="";text:entry.body;wrapMode:Text.WordWrap;maximumLineCount:2;elide:Text.ElideRight;color:Colours.palette.m3onSurfaceVariant;font.pointSize:Appearance.font.size.small}
                        }
                        StyledText {Layout.alignment:Qt.AlignTop;text:panel.rel(entry.at);color:Colours.palette.m3outline;font.pointSize:Appearance.font.size.small}
                    }
                }
            }
        }
    }
    Connections {target:Bridge;function onShellEvent(event){if(event==="socialPeek")rail.shown=true;else if(event==="socialHide")rail.shown=false}}
}
