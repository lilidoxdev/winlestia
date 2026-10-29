// Left-edge hidden rail of browser bookmarks: the mirror twin of SocialRail, an EdgeRail instance with side:"left".
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
    side:"left"
    title:"Panneaux · Favoris"
    objectName:"bookmarksRail"
    contentW:54
    contentHeight:column.implicitHeight
    readonly property var bm:Bridge.bookmarks
    readonly property var itemsData:bm.items??[]
    readonly property string error:bm.error??""
    readonly property bool busy:bm.busy??false
    readonly property string browserName:{
        const list=bm.browsers??[]
        for(const b of list)if(b.id===bm.browser)return b.name
        return qsTr("le navigateur")
    }
    function errorText(e){
        return e==="running"?qsTr("Fermez %1 pour supprimer un favori.").arg(browserName):e==="unsupported"?qsTr("Suppression non prise en charge pour ce navigateur."):e==="notfound"?qsTr("Favori introuvable."):e
    }
    // Folders unfold in place: ids of the open ones; the ListModel `flat` holds the visible rows and is synced by id.
    property var openIds:({})
    property string lastSig:""
    readonly property real gearBlock:40+1+2*Appearance.spacing.normal
    readonly property real maxList:Math.max(100,Bridge.screenHeight-80-2*(fillet+Appearance.padding.large)-gearBlock)
    ListModel {id:flat}
    function flatten(list,depth,out){
        for(const it of list){
            const open=(it.folder??false)&&openIds[it.id]===true
            out.push({id:String(it.id),title:it.title??"",url:it.url??"",icon:it.icon??"",folder:it.folder??false,depth:depth,open:open})
            if(open)flatten(it.children??[],depth+1,out)
        }
        return out
    }
    function sync(){
        if(!visible)return
        const target=flatten(itemsData,0,[])
        const sig=JSON.stringify(target)
        if(sig===lastSig)return
        lastSig=sig
        const keys=["title","url","icon","folder","depth","open"]
        // Remove rows that are gone, then walk the target: keep, move or insert by id.
        const want={};for(const t of target)want[t.id]=true
        for(let i=flat.count-1;i>=0;i--)if(!want[flat.get(i).id])flat.remove(i)
        for(let i=0;i<target.length;i++){
            const t=target[i]
            if(i<flat.count&&flat.get(i).id===t.id){
                const cur=flat.get(i);let diff=false
                for(const k of keys)if(cur[k]!==t[k]){diff=true;break}
                if(diff)flat.set(i,t)
                continue
            }
            let j=-1
            for(let k=i+1;k<flat.count;k++)if(flat.get(k).id===t.id){j=k;break}
            if(j>=0){flat.move(j,i,1);flat.set(i,t)}else flat.insert(i,t)
        }
        while(flat.count>target.length)flat.remove(flat.count-1)
    }
    onItemsDataChanged:sync()
    onOpenIdsChanged:sync()
    onVisibleChanged:{if(visible)sync()}
    Component.onCompleted:sync()
    // Once per reveal (hidden -> shown), never per frame: the backend rescans only if the files changed.
    Connections {target:rail;function onShownChanged(){if(rail.shown)Bridge.action("bookmarksRefresh",{ifDirty:true})}}
    Connections {target:Bridge;function onShellEvent(event){if(event==="bookmarksPeek")rail.shown=true;else if(event==="bookmarksHide")rail.shown=false}}
    function toggleFolder(id){const o=Object.assign({},openIds);if(o[id])delete o[id];else o[id]=true;openIds=o}
    function initial(t,u){
        const s=(t||u||"?").replace(/^https?:\/\/(www\.)?/,"").trim()
        return s.length?s[0].toUpperCase():"?"
    }
    function tint(t,u){
        let h=0;const s=(t||u||"");for(let i=0;i<s.length;i++)h=(h*31+s.charCodeAt(i))&0xffff
        return Qt.hsla((h%360)/360,.45,Colours.light?.62:.38,1)
    }
    function deletable(id){return (bm.canDeleteInBrowser??false)&&!id.endsWith(":other")&&!id.endsWith(":synced")}

    Item {
        anchors.fill:parent
        transform:Translate {x:rail.slide}
        ColumnLayout {
            id:column
            x:rail.edgeX-Appearance.padding.large-rail.contentW;y:(parent.height-height)/2;spacing:Appearance.spacing.normal
            Item {
                id:listBox
                objectName:"bookmarksList"
                Layout.preferredWidth:rail.contentW
                Layout.preferredHeight:flat.count>0?Math.min(list.contentHeight,rail.maxList):54
                ListView {
                    id:list
                    objectName:"bookmarksView"
                    anchors.fill:parent;clip:true;model:flat;spacing:Appearance.spacing.small
                    boundsBehavior:Flickable.StopAtBounds;reuseItems:true
                    C.ScrollBar.vertical:StyledScrollBar {flickable:list}
                    delegate:Item {
                        id:entry
                        required property int index
                        required property string id
                        required property string title
                        required property string url
                        required property string icon
                        required property bool folder
                        required property int depth
                        required property bool open
                        objectName:"bookmark-"+index
                        readonly property real size:depth>0?38:48
                        width:list.width;height:size
                        StyledClippingRect {
                            id:disc
                            anchors.centerIn:parent;width:entry.size;height:entry.size;radius:width/2
                            color:entry.folder?Colours.palette.m3secondaryContainer:entry.icon===""?rail.tint(entry.title,entry.url):Colours.palette.m3surfaceContainerHigh
                            Image {
                                id:favicon
                                objectName:"favicon-"+entry.index
                                anchors.fill:parent;visible:!entry.folder&&entry.icon!==""&&status===Image.Ready
                                source:entry.folder?"":entry.icon;asynchronous:true;fillMode:Image.PreserveAspectCrop;smooth:true;sourceSize.width:64;sourceSize.height:64
                            }
                            MaterialIcon {visible:entry.folder;anchors.centerIn:parent;text:entry.open?"folder_open":"folder";font.pointSize:entry.depth>0?Appearance.font.size.large:Appearance.font.size.title;color:Colours.palette.m3onSecondaryContainer}
                            StyledText {
                                objectName:"initial-"+entry.index
                                visible:!entry.folder&&!favicon.visible;anchors.centerIn:parent
                                text:rail.initial(entry.title,entry.url);color:"white";font.bold:true;font.pointSize:entry.depth>0?Appearance.font.size.normal:Appearance.font.size.large
                            }
                            StateLayer {radius:width/2;function onClicked(){if(entry.folder)rail.toggleFolder(entry.id);else{Bridge.action("bookmarksOpen",{id:entry.id});rail.shown=false}}}
                        }
                        HoverHandler {id:hover}
                        TapHandler {acceptedButtons:Qt.RightButton;onTapped:{const p=entry.mapToGlobal(entry.width,entry.height/2);menu.openFor(entry.id,entry.title,p.x,p.y)}}
                        C.ToolTip {visible:hover.hovered;text:entry.title||entry.url;delay:500;popupType:C.Popup.Window}
                    }
                }
                // Empty / busy / error states (only while there is nothing to list).
                Rectangle {
                    objectName:"bookmarksState"
                    visible:flat.count===0;anchors.centerIn:parent;width:48;height:48;radius:24;color:Colours.palette.m3surfaceContainerHigh
                    readonly property string kind:rail.busy?"busy":rail.error!==""&&rail.error!=="sync"?"error":"empty"
                    property string label:kind==="busy"?qsTr("Chargement des favoris…"):kind==="error"?rail.errorText(rail.error):qsTr("Aucun favori")
                    MaterialIcon {anchors.centerIn:parent;text:parent.kind==="busy"?"hourglass_empty":parent.kind==="error"?"error":"bookmark_border";font.pointSize:Appearance.font.size.title;color:parent.kind==="error"?Colours.palette.m3error:Colours.palette.m3onSurfaceVariant}
                    HoverHandler {id:stateHover}
                    C.ToolTip {visible:stateHover.hovered;text:parent.label;delay:300;popupType:C.Popup.Window}
                }
            }
            // Error while the list is non-empty (not the sync warning, which the menu handles).
            Rectangle {
                objectName:"bookmarksWarn"
                visible:flat.count>0&&rail.error!==""&&rail.error!=="sync";Layout.alignment:Qt.AlignHCenter;Layout.preferredWidth:30;Layout.preferredHeight:30;radius:15;color:Colours.palette.m3error
                MaterialIcon {anchors.centerIn:parent;text:"warning";font.pointSize:Appearance.font.size.large;color:Colours.palette.m3onError}
                HoverHandler {id:warnHover}
                C.ToolTip {visible:warnHover.hovered;text:rail.errorText(rail.error);delay:300;popupType:C.Popup.Window}
            }
            Rectangle {Layout.alignment:Qt.AlignHCenter;implicitWidth:34;implicitHeight:1;color:Colours.palette.m3outlineVariant}
            IconButton {objectName:"bookmarksGear";Layout.alignment:Qt.AlignHCenter;icon:"settings";type:IconButton.Text;onClicked:{Bridge.action("settings",{section:"bookmarks"});rail.shown=false}}
        }
    }

    // Context menu: bounded independent window (same recipe as the dock menu), closed when it loses focus or on an outside click.
    Window {
        id:menu
        objectName:"bookmarksMenu"
        title:"Panneaux · Menu des favoris"
        transientParent:null
        flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.NoDropShadowWindowHint
        color:"transparent"
        width:300
        height:body.implicitHeight+28
        property string entryId:""
        property string entryTitle:""
        property string mode:"menu"
        property string pendingId:""
        property real anchorX:0
        property real anchorY:0
        function openFor(id,title,px,py){entryId=id;entryTitle=title;mode="menu";anchorX=px;anchorY=py;place();visible=true;Qt.callLater(menu.requestActivate)}
        function place(){x=Math.max(Bridge.screenX+8,Math.min(anchorX+8,Bridge.screenX+Bridge.screenWidth-width-8));y=Math.max(Bridge.screenY+8,Math.min(anchorY-height/2,Bridge.screenY+Bridge.screenHeight-height-8))}
        onHeightChanged:if(visible)place()
        property bool wasActive:false
        onActiveChanged:{if(active)wasActive=true;else if(visible&&wasActive){wasActive=false;visible=false}}
        onVisibleChanged:if(!visible)wasActive=false
        Timer {objectName:"bookmarksMenuOutside";interval:100;repeat:true;running:menu.visible&&!Bridge.preview;onTriggered:if(Bridge.menuOutsideClick(menu.x,menu.y,menu.width,menu.height))menu.visible=false}
        Shortcut {sequence:"Escape";enabled:menu.visible;onActivated:menu.visible=false}
        // The backend answers a first delete with error "sync" when browser sync is active: ask again, then repeat with confirmSync.
        Connections {
            target:Bridge
            function onChanged(){
                if(menu.pendingId==="")return
                if(rail.error==="sync"){menu.mode="sync";menu.place();menu.visible=true;Qt.callLater(menu.requestActivate)}
                else if(!rail.busy)menu.pendingId=""
            }
        }
        Rectangle {
            anchors.fill:parent;radius:Appearance.rounding.large;color:Qt.alpha(Colours.surfaceBase,.96);border.width:1;border.color:Colours.palette.m3outlineVariant
            Column {
                id:body
                x:14;y:14;width:menu.width-28;spacing:3
                StyledText {width:parent.width-12;x:6;text:menu.entryTitle;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3onSurfaceVariant;elide:Text.ElideRight}
                StyledRect {
                    objectName:"bookmarksMenuHide"
                    visible:menu.mode==="menu";width:body.width;height:38;radius:Appearance.rounding.small;color:"transparent"
                    StateLayer {function onClicked(){Bridge.action("bookmarksHide",{id:menu.entryId});menu.visible=false}}
                    RowLayout {anchors.fill:parent;anchors.leftMargin:9;anchors.rightMargin:8;spacing:10;MaterialIcon {text:"visibility_off";font.pointSize:Appearance.font.size.large;color:Colours.palette.m3primary} StyledText {Layout.fillWidth:true;text:qsTr("Retirer de Winlestia");elide:Text.ElideRight}}
                }
                StyledRect {
                    objectName:"bookmarksMenuDelete"
                    visible:menu.mode==="menu"&&rail.deletable(menu.entryId);width:body.width;height:38;radius:Appearance.rounding.small;color:"transparent"
                    StateLayer {function onClicked(){menu.mode="confirm"}}
                    RowLayout {anchors.fill:parent;anchors.leftMargin:9;anchors.rightMargin:8;spacing:10;MaterialIcon {text:"delete";font.pointSize:Appearance.font.size.large;color:Colours.palette.m3error} StyledText {Layout.fillWidth:true;text:qsTr("Supprimer dans %1…").arg(rail.browserName);elide:Text.ElideRight}}
                }
                StyledText {
                    objectName:"bookmarksMenuNote"
                    visible:menu.mode!=="menu";width:parent.width-12;x:6;wrapMode:Text.WordWrap;font.pointSize:Appearance.font.size.small
                    text:menu.mode==="sync"?qsTr("La synchronisation de %1 est active : la suppression sera répliquée sur vos autres appareils.").arg(rail.browserName):qsTr("Supprimer ce favori dans %1 ? Une sauvegarde du fichier est faite avant. %1 doit être fermé.").arg(rail.browserName)
                }
                Row {
                    visible:menu.mode!=="menu";spacing:8;x:6
                    TextButton {objectName:"bookmarksMenuCancel";text:qsTr("Annuler");type:TextButton.Text;onClicked:{menu.pendingId="";menu.visible=false}}
                    TextButton {
                        objectName:"bookmarksMenuConfirm"
                        text:menu.mode==="sync"?qsTr("Supprimer quand même"):qsTr("Supprimer");type:TextButton.Tonal
                        onClicked:{
                            const sync=menu.mode==="sync"
                            menu.pendingId=menu.entryId
                            Bridge.action("bookmarksDeleteInBrowser",sync?{id:menu.entryId,confirmSync:true}:{id:menu.entryId})
                            menu.visible=false
                        }
                    }
                }
            }
        }
    }
}
