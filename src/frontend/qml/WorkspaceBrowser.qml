// Windows library ("Ton espace"), composed from Caelestia's original typography, ripples, menus and motion.
// Performance model: the catalog is indexed once per catalog change (off the open path), the tile model is a
// persistent ListModel patched incrementally (never rebuilt on open or on filter), delegates are recycled, and
// every animation is gated on visibility and on the "Effets allégés" preference.
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.services
import qs.config

Item {
    id:browser
    objectName:"workspaceBrowser"
    implicitHeight:Math.min(760,Bridge.screenHeight-90)
    focus:visible
    property string scope:"apps"
    property string category:"Tout"
    readonly property bool dialogOpen:editor.open
    property bool favoritesOnly:false
    property bool recentSort:false
    property bool listMode:false
    property string selectedId:""
    property string selectedName:""
    property string selectedCategory:""
    property bool selectedFavorite:false
    property string selectedKind:"app"
    property bool flatMode:false
    property var collapsed:({})
    property var flat:[]
    property var secInfo:({})
    property var secOrder:[]
    property int cur:-1
    property var lineSigs:[]
    // "Effets allégés": no bounce, no stagger, no scale; only short fades (durations are already scaled down).
    readonly property bool fx:!Appearance.reducedEffects
    // Entrance progress per region (1 = settled). Driven only by openAnim, which only runs while visible.
    property real pPanel:1
    property real pSide:1
    property real pHead:1
    property real pBody:1
    property bool bulk:false
    property bool stale:true
    property var shown:[]
    property var sigs:({})
    property Item tipTarget:null
    property string tipText:""
    function fold(text){return String(text).toLocaleLowerCase().normalize("NFD").replace(/[̀-ͯ]/g,"")}

    // ---- Data: indexed once per catalog change, never on open -------------------------------------------------
    property var index:[]
    property var counts:({apps:0,games:0,projects:0,documents:0,favorites:0,recent:0,all:0})
    function rebuild(){
        stale=false
        const raw=Bridge.apps||[]
        // Dedupe: normalized target path, else name + kind. Keep the entry with the best metadata (favorite, recent, category).
        const score=e=>(e.favorite?4:0)+(e.recent>=0?2:0)+(e.category?1:0)
        const seen=new Map(),list=[]
        for(const e of raw){
            const k=e.target?"p:"+String(e.target).toLowerCase().replace(/\//g,"\\").replace(/\\+$/,""):"n:"+fold(e.name)+"|"+(e.kind||"app")
            const at=seen.get(k)
            if(at===undefined){seen.set(k,list.length);list.push(e)}
            else if(score(e)>score(list[at]))list[at]=e
        }
        const c={apps:0,games:0,projects:0,documents:0,favorites:0,recent:0,all:list.length}
        const idx=new Array(list.length)
        for(let i=0;i<list.length;i++){
            const e=list[i]
            if(e.board in c)c[e.board]++
            if(e.favorite)c.favorites++
            if(e.recent>=0)c.recent++
            const nk=fold(e.name)
            idx[i]={entry:e,key:nk+" "+fold(e.category||""),nk:nk}
        }
        counts=c
        index=idx
    }
    readonly property var spaces:[{id:"apps",name:"Applications",icon:"apps"},{id:"games",name:"Jeux",icon:"sports_esports"},{id:"projects",name:"Projets",icon:"code_blocks"},{id:"documents",name:"Documents",icon:"folder_open"}]
    readonly property var shortcuts:[{id:"favorites",name:"Favoris",icon:"star"},{id:"recent",name:"Récents",icon:"history"},{id:"all",name:"Tout afficher",icon:"grid_view"}]
    readonly property var navItems:spaces.concat(shortcuts)
    readonly property int navIndex:navItems.findIndex(n=>n.id===scope)
    function navY(i){return i*52+(i>=4?17:0)}
    readonly property string title:scope==="documents"?"Documents & médias":scope==="favorites"?"Favoris":scope==="recent"?"Récemment ouverts":spaces.find(s=>s.id===scope)?.name||"Tout ton espace"
    readonly property var base:index.filter(i=>{const e=i.entry;return (scope==="all"||scope==="favorites"||scope==="recent"||e.board===scope)&&(!(favoritesOnly||scope==="favorites")||e.favorite)&&(scope!=="recent"||e.recent>=0)})
    readonly property var categoryCounts:{const m={};for(const i of base){const c=i.entry.category;if(c)m[c]=(m[c]||0)+1}return m}
    readonly property var categories:["Tout"].concat(Object.keys(categoryCounts).sort((a,b)=>a.localeCompare(b)))
    readonly property var entries:{
        const words=fold(query.text).split(" ").filter(w=>w)
        const values=base.filter(i=>(category==="Tout"||i.entry.category===category)&&words.every(w=>i.key.includes(w)))
        if(recentSort||scope==="recent")values.sort((a,b)=>(a.entry.recent<0?9999:a.entry.recent)-(b.entry.recent<0?9999:b.entry.recent))
        else values.sort((a,b)=>a.nk<b.nk?-1:a.nk>b.nk?1:0)
        return values.map(i=>i.entry)
    }
    onEntriesChanged:syncModel()
    onFlatModeChanged:syncModel()
    readonly property int tileCount:entries.length
    function isCollapsed(key){return query.text?false:(key in collapsed?collapsed[key]:key==="tools")}
    function toggleSection(key){const c=Object.assign({},collapsed);c[key]=!isCollapsed(key);collapsed=c;syncModel()}
    function isTool(e){return fold(e.category||"").startsWith("windows et peri")}

    // ---- Sections: Favoris, Récents, categories A-Z, Dossiers, Outils Windows (or letters in flat A-Z mode) ----
    function buildSections(){
        const list=entries,out=[]
        if(flatMode){
            const m=new Map()
            for(const e of list){const f=fold(e.name).charAt(0).toUpperCase();const k=/[A-Z]/.test(f)?f:"#";if(!m.has(k))m.set(k,[]);m.get(k).push(e)}
            for(const k of Array.from(m.keys()).sort((a,b)=>a==="#"?-1:b==="#"?1:a<b?-1:1))out.push({key:"l:"+k,label:k,icon:"",items:m.get(k)})
            return out
        }
        if(scope==="recent"){out.push({key:"rec",label:qsTr("Récents"),icon:"history",items:list});return out}
        const byName=(a,b)=>fold(a.name)<fold(b.name)?-1:fold(a.name)>fold(b.name)?1:0
        const top=category==="Tout"&&!query.text&&scope!=="favorites"
        if(top){
            const fav=list.filter(e=>e.favorite);if(fav.length)out.push({key:"fav",label:qsTr("Favoris"),icon:"star",items:fav})
            const rec=list.filter(e=>e.recent>=0).sort((a,b)=>a.recent-b.recent).slice(0,grid.columns);if(rec.length)out.push({key:"rec",label:qsTr("Récents"),icon:"history",items:rec})
        }
        const cats=new Map(),folders=[],tools=[]
        for(const e of list){
            if(isTool(e))tools.push(e)
            else if(e.kind==="folder")folders.push(e)
            else{const c=e.category||qsTr("Autres");if(!cats.has(c))cats.set(c,[]);cats.get(c).push(e)}
        }
        for(const c of Array.from(cats.keys()).sort((a,b)=>a.localeCompare(b)))out.push({key:"c:"+c,label:c,icon:"label",items:cats.get(c).sort(byName)})
        if(folders.length)out.push({key:"folders",label:qsTr("Dossiers et projets"),icon:"folder",items:folders.sort(byName)})
        if(tools.length)out.push({key:"tools",label:qsTr("Outils Windows"),icon:"settings",items:tools.sort(byName)})
        return out
    }

    // ---- Line model: one row per tile line (or a zero-height stub for a collapsed section), patched in place ------
    ListModel{id:rows}
    function syncModel(){
        const cols=Math.max(1,grid.columns)
        const secs=buildSections(),flatNew=[],lines=[],info={}
        for(const sec of secs){
            const closed=isCollapsed(sec.key)
            info[sec.key]={label:sec.label,count:sec.items.length,icon:sec.icon,closed:closed}
            if(closed){lines.push({sec:sec.key,first:0,n:0});continue}
            for(let i=0;i<sec.items.length;i+=cols){lines.push({sec:sec.key,first:flatNew.length,n:Math.min(cols,sec.items.length-i)});for(let j=i;j<Math.min(i+cols,sec.items.length);j++)flatNew.push(sec.items[j])}
        }
        flat=flatNew;secInfo=info;secOrder=secs.map(x=>x.key)
        const sigs=lines.map(l=>l.sec+"|"+l.first+"|"+l.n+"|"+l.n)
        for(let i=0;i<lines.length;i++){
            if(i>=rows.count)rows.append(lines[i])
            else if(lineSigs[i]!==sigs[i])rows.set(i,lines[i])
        }
        if(rows.count>lines.length)rows.remove(lines.length,rows.count-lines.length)
        lineSigs=sigs
        if(cur>=flatNew.length)cur=-1
    }
    function lineOfTile(t){for(let i=0;i<lineSigs.length;i++){const p=lineSigs[i].split("|");const f=+p[1],n=+p[2];if(n>0&&t>=f&&t<f+n)return i}return 0}
    function markBulk(){bulk=true;bulkTimer.restart()}
    function replay(){if(fx&&visible)revealAnim.restart()}
    function selectBoard(board){markBulk();scope=board;category="Tout";query.text="";favoritesOnly=false;grid.positionViewAtBeginning();replay()}
    function hoverTip(target,text){
        tipTarget=target;tipText=text||"";tip.shown=false
        if(target)tipTimer.restart();else tipTimer.stop()
    }
    function focusSearch(){query.forceActiveFocus();query.selectAll()}

    Component.onCompleted:{if(visible)rebuild();else firstBuild.start()}
    // One-shot preload after the shell is idle; later catalog changes are debounced and also applied while hidden.
    Timer{id:firstBuild;interval:600;onTriggered:if(browser.stale)browser.rebuild()}
    Timer{id:catalogTimer;onTriggered:browser.rebuild()}
    Timer{id:bulkTimer;interval:80;onTriggered:browser.bulk=false}
    Timer{id:tipTimer;interval:650;onTriggered:{
        if(!browser.tipTarget||!browser.visible)return
        tipLabel.text=browser.tipText
        const p=browser.tipTarget.mapToItem(browser,browser.tipTarget.width/2,browser.tipTarget.height-4)
        tip.x=Math.max(8,Math.min(browser.width-tip.width-8,p.x-tip.width/2));tip.y=Math.max(8,p.y)
        tip.shown=true
    }}
    Connections{target:Bridge;function onCatalogChanged(){browser.stale=true;catalogTimer.interval=browser.visible?60:1500;catalogTimer.restart()}}
    Connections{target:Bridge;function onNavigationRequested(mode){if(mode.startsWith("workspace:"))browser.selectBoard(mode.split(":")[1])}}

    // ---- Open: scale + fade with a slight overshoot, regions stagger in. Stops when hidden. ------------------
    ParallelAnimation{
        id:openAnim
        NumberAnimation{target:browser;property:"pPanel";from:0;to:1;duration:Appearance.anim.durations.expressiveFastSpatial+80;easing.type:Easing.BezierSpline;easing.bezierCurve:Appearance.anim.curves.expressiveFastSpatial}
        SequentialAnimation{PauseAnimation{duration:browser.fx?30:0}NumberAnimation{target:browser;property:"pSide";from:0;to:1;duration:Appearance.anim.durations.normal;easing.type:Easing.BezierSpline;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
        SequentialAnimation{PauseAnimation{duration:browser.fx?80:0}NumberAnimation{target:browser;property:"pHead";from:0;to:1;duration:Appearance.anim.durations.normal;easing.type:Easing.BezierSpline;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
        SequentialAnimation{PauseAnimation{duration:browser.fx?130:0}NumberAnimation{target:browser;property:"pBody";from:0;to:1;duration:Appearance.anim.durations.normal;easing.type:Easing.BezierSpline;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
        NumberAnimation{target:grid;property:"reveal";from:browser.fx?0:1;to:1;duration:browser.fx?900:1}
    }
    NumberAnimation{id:revealAnim;target:grid;property:"reveal";from:0;to:1;duration:900}
    onVisibleChanged:{
        if(visible){
            if(stale)rebuild()
            catalogTimer.interval=60
            openAnim.restart()
            forceActiveFocus()
        }else{
            context.close();openAnim.stop();revealAnim.stop();tipTimer.stop();tip.shown=false;query.focus=false;grid.focus=false
            pPanel=1;pSide=1;pHead=1;pBody=1;grid.reveal=1
        }
    }
    Keys.onPressed:event=>{
        if(query.activeFocus||editor.open)return
        if(event.key===Qt.Key_Down||event.key===Qt.Key_Right||event.key===Qt.Key_Tab){grid.forceActiveFocus();if(browser.cur<0)browser.cur=0;event.accepted=true}
        else if(event.text.length>0&&!(event.modifiers&(Qt.ControlModifier|Qt.AltModifier))&&event.text.charCodeAt(0)>=32){query.forceActiveFocus();query.text+=event.text;query.cursorPosition=query.text.length;event.accepted=true}
    }
    Shortcut{sequence:"Ctrl+F";enabled:browser.visible&&!editor.open;onActivated:browser.focusSearch()}

    component Spring: Anim {
        duration:Appearance.reducedEffects?Appearance.anim.durations.small:Appearance.anim.durations.expressiveFastSpatial
        easing.bezierCurve:Appearance.reducedEffects?Appearance.anim.curves.standard:Appearance.anim.curves.expressiveFastSpatial
    }

    // Right-click menu: plain in-panel overlay, fully imperative. Root cause of "works only once": Menu.qml sets
    // `expanded=false` itself when an item is picked, which destroyed a `expanded:context.opened` binding, so the menu
    // stayed at height 0 / opacity 0 forever. Never bind `expanded`; openMenu/closeMenu own the whole state.
    Item {
        id:context;objectName:"workspaceContext";anchors.fill:parent;z:90;visible:false
        property bool opened:false
        function openMenu(px,py){
            opened=true;menu.expanded=true;visible=true;catcher.enabled=true
            menu.x=Math.max(8,Math.min(px,browser.width-menu.width-8));menu.y=Math.max(8,Math.min(py,browser.height-menu.implicitHeight-8))
        }
        function openAt(px,py){openMenu(px,py)}
        function close(){opened=false;menu.expanded=false;catcher.enabled=false;visible=false}
        MouseArea{id:catcher;anchors.fill:parent;enabled:false;acceptedButtons:Qt.LeftButton|Qt.MiddleButton;onPressed:context.close()}
        Menu {
            id:menu;objectName:"workspaceMenu";width:Math.max(260,implicitWidth);height:implicitHeight
            readonly property bool app:browser.selectedKind!=="folder"
            readonly property var itemTexts:{const r=[];for(let i=0;i<items.length;i++)r.push(items[i].text);return r}
            property MenuItem miOpen:MenuItem{text:qsTr("Ouvrir");icon:"open_in_new";onClicked:Bridge.action("launch",{id:browser.selectedId})}
            property MenuItem miFav:MenuItem{text:browser.selectedFavorite?qsTr("Retirer des favoris"):qsTr("Ajouter aux favoris");icon:"star";onClicked:Bridge.action("favorite",{id:browser.selectedId})}
            property MenuItem miReveal:MenuItem{text:qsTr("Ouvrir l’emplacement");icon:"folder_open";onClicked:Bridge.action("reveal",{id:browser.selectedId})}
            property MenuItem miAdmin:MenuItem{text:qsTr("Exécuter en administrateur");icon:"admin_panel_settings";onClicked:Bridge.action("dockApp",{id:browser.selectedId,action:"admin"})}
            property MenuItem miEdit:MenuItem{text:qsTr("Renommer et catégoriser");icon:"edit";onClicked:{nameField.text=browser.selectedName;categoryField.text=browser.selectedCategory;editor.open=true;nameField.forceActiveFocus()}}
            property MenuItem miIcon:MenuItem{text:qsTr("Choisir une icône");icon:"image";onClicked:Bridge.chooseEntryIcon(browser.selectedId)}
            property MenuItem miApps:MenuItem{text:qsTr("Ranger dans Applications");icon:"apps";onClicked:Bridge.action("move",{id:browser.selectedId,board:"apps"})}
            property MenuItem miGames:MenuItem{text:qsTr("Ranger dans Jeux");icon:"sports_esports";onClicked:Bridge.action("move",{id:browser.selectedId,board:"games"})}
            property MenuItem miProj:MenuItem{text:qsTr("Ranger dans Projets");icon:"code_blocks";onClicked:Bridge.action("move",{id:browser.selectedId,board:"projects"})}
            property MenuItem miDocs:MenuItem{text:qsTr("Ranger dans Documents");icon:"folder_open";onClicked:Bridge.action("move",{id:browser.selectedId,board:"documents"})}
            items:app?[miOpen,miFav,miReveal,miAdmin,miEdit,miIcon,miApps,miGames,miProj,miDocs]:[miOpen,miFav,miReveal,miEdit,miIcon,miApps,miGames,miProj,miDocs]
            onItemSelected:item=>{context.close();item.clicked()}
        }
    }

    component NavItem: Item {
        id:nav
        property string icon
        property string label
        property int count:-1
        property bool selected
        signal triggered
        implicitHeight:48
        MouseArea{id:navMouse;anchors.fill:parent;hoverEnabled:true;cursorShape:Qt.PointingHandCursor;onClicked:nav.triggered()}
        Rectangle{anchors.fill:parent;radius:Appearance.rounding.full;color:Colours.palette.m3onSurface;opacity:navMouse.pressed?.12:navMouse.containsMouse&&!nav.selected?.07:0;Behavior on opacity{Anim{duration:Appearance.anim.durations.small}}}
        RowLayout{anchors.fill:parent;anchors.leftMargin:Appearance.padding.larger;anchors.rightMargin:Appearance.padding.normal;spacing:Appearance.spacing.smaller
            MaterialIcon{text:nav.icon;fill:nav.selected?1:0;font.pointSize:Appearance.font.size.large;color:nav.selected?Colours.palette.m3onSecondaryContainer:Colours.palette.m3onSurfaceVariant
                scale:nav.selected&&!Appearance.reducedEffects?1.12:1;Behavior on scale{Spring{}}}
            StyledText{Layout.fillWidth:true;text:nav.label;elide:Text.ElideRight;font.pointSize:Appearance.font.size.normal;color:nav.selected?Colours.palette.m3onSecondaryContainer:Colours.palette.m3onSurface}
            Rectangle{visible:nav.count>=0;implicitWidth:Math.max(24,badge.implicitWidth+14);implicitHeight:22;radius:Appearance.rounding.full
                color:nav.selected?Qt.alpha(Colours.palette.m3onSecondaryContainer,.14):Colours.palette.m3surfaceContainerHighest
                StyledText{id:badge;anchors.centerIn:parent;text:nav.count;font.pointSize:Appearance.font.size.small;color:nav.selected?Colours.palette.m3onSecondaryContainer:Colours.palette.m3onSurfaceVariant}
            }
        }
    }
    component FilterChip: Item {
        id:chip
        property string label
        property int count:-1
        property bool selected
        signal triggered
        implicitHeight:34;implicitWidth:chipRow.implicitWidth+Appearance.padding.large*2
        Behavior on implicitWidth{Anim{duration:Appearance.anim.durations.small}}
        Rectangle{id:chipBg;anchors.fill:parent
            // Morphs from a rounded rectangle into a pill when selected.
            radius:chip.selected?height/2:Appearance.rounding.small;Behavior on radius{Spring{}}
            color:chip.selected?Colours.palette.m3secondaryContainer:"transparent"
            border.width:chip.selected?0:1;border.color:Colours.palette.m3outlineVariant}
        Rectangle{anchors.fill:parent;radius:chipBg.radius;color:Colours.palette.m3onSurface;opacity:chipMouse.pressed?.12:chipMouse.containsMouse?.07:0;Behavior on opacity{Anim{duration:Appearance.anim.durations.small}}}
        MouseArea{id:chipMouse;anchors.fill:parent;hoverEnabled:true;cursorShape:Qt.PointingHandCursor;onClicked:chip.triggered()}
        Row{id:chipRow;anchors.centerIn:parent;spacing:Appearance.spacing.small
            MaterialIcon{visible:chip.selected;anchors.verticalCenter:parent.verticalCenter;text:"check";font.pointSize:Appearance.font.size.normal;color:Colours.palette.m3onSecondaryContainer}
            StyledText{anchors.verticalCenter:parent.verticalCenter;text:chip.label;font.pointSize:Appearance.font.size.small;color:chip.selected?Colours.palette.m3onSecondaryContainer:Colours.palette.m3onSurface}
            StyledText{visible:chip.count>=0;anchors.verticalCenter:parent.verticalCenter;text:chip.count;font.pointSize:Appearance.font.size.small;opacity:.6;color:chip.selected?Colours.palette.m3onSecondaryContainer:Colours.palette.m3outline}
        }
    }

    Item {
        id:panel
        anchors.fill:parent
        transformOrigin:Item.Center
        opacity:browser.fx?Math.max(0,Math.min(1,browser.pPanel*1.5)):browser.pPanel
        scale:browser.fx?.94+.06*browser.pPanel:1
        RowLayout {
            anchors.fill:parent;spacing:Appearance.spacing.large
            StyledRect {
                id:side
                Layout.preferredWidth:252;Layout.minimumWidth:252;Layout.maximumWidth:252;Layout.fillWidth:false;Layout.fillHeight:true
                radius:Appearance.rounding.large;color:Colours.palette.m3surfaceContainer
                opacity:browser.pSide
                transform:Translate{x:browser.fx?(1-browser.pSide)*-24:0}
                ColumnLayout {
                    anchors.fill:parent;anchors.margins:Appearance.padding.large;spacing:Appearance.spacing.small
                    RowLayout {Layout.leftMargin:Appearance.padding.small;Layout.topMargin:Appearance.padding.small;Layout.bottomMargin:Appearance.spacing.normal;spacing:Appearance.spacing.normal
                        StyledRect{implicitWidth:40;implicitHeight:40;radius:Appearance.rounding.full;color:Colours.palette.m3primaryContainer
                            MaterialIcon{anchors.centerIn:parent;text:qsTr("space_dashboard");fill:1;font.pointSize:Appearance.font.size.larger;color:Colours.palette.m3onPrimaryContainer}}
                        Column{StyledText{text:qsTr("Ton espace");font.pointSize:Appearance.font.size.large;font.weight:Font.Medium}
                            StyledText{text:browser.counts.all+" accès";font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}}
                    }
                    Item {
                        Layout.fillWidth:true;implicitHeight:browser.navY(6)+48
                        // Sliding selection pill: one rectangle that springs between items.
                        Rectangle{
                            width:parent.width;height:48;radius:Appearance.rounding.full;color:Colours.palette.m3secondaryContainer
                            visible:browser.navIndex>=0;y:browser.navY(Math.max(0,browser.navIndex))
                            Behavior on y{Spring{}}
                        }
                        Rectangle{y:browser.navY(3)+48+8;width:parent.width;height:1;color:Colours.palette.m3outlineVariant}
                        Repeater {model:browser.navItems
                            NavItem {required property var modelData;required property int index;width:parent.width;y:browser.navY(index);icon:modelData.icon;label:modelData.name;count:browser.counts[modelData.id]??0;selected:browser.scope===modelData.id;onTriggered:browser.selectBoard(modelData.id)}
                        }
                    }
                    Item{Layout.fillHeight:true}
                    IconTextButton{Layout.fillWidth:true;icon:"content_paste";text:qsTr("Presse-papiers");type:IconTextButton.Text;onClicked:Bridge.navigate("clipboard")}
                    IconTextButton{Layout.fillWidth:true;icon:"settings";text:qsTr("Personnaliser");type:IconTextButton.Text;onClicked:Bridge.navigate("settings")}
                }
            }
            ColumnLayout {
                Layout.fillWidth:true;Layout.fillHeight:true;spacing:Appearance.spacing.larger
                RowLayout {
                    Layout.fillWidth:true
                    opacity:browser.pHead;transform:Translate{y:browser.fx?(1-browser.pHead)*-14:0}
                    Column{spacing:Appearance.padding.small
                        StyledText{text:browser.title;font.pointSize:Appearance.font.size.extraLarge;font.weight:Font.Medium}
                        Row{spacing:Appearance.spacing.small
                            Rectangle{anchors.verticalCenter:parent.verticalCenter;implicitWidth:Math.max(28,countText.implicitWidth+16);implicitHeight:22;radius:Appearance.rounding.full;color:Colours.palette.m3primaryContainer
                                StyledText{id:countText;anchors.centerIn:parent;text:browser.entries.length;font.pointSize:Appearance.font.size.small;font.weight:Font.Medium;color:Colours.palette.m3onPrimaryContainer}}
                            StyledText{anchors.verticalCenter:parent.verticalCenter;text:qsTr("accès · Clique pour ouvrir, clic droit pour organiser");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
                        }
                    }
                    Item{Layout.fillWidth:true}
                    IconButton{icon:"search";type:IconButton.Text;onClicked:Bridge.navigate("palette")}
                    IconButton{icon:"add";type:IconButton.Text;onClicked:Bridge.action("addAccess",{board:browser.scope})}
                    IconButton{icon:"create_new_folder";type:IconButton.Text;visible:browser.scope==="projects"||browser.scope==="documents";onClicked:Bridge.action("addFolder",{board:browser.scope})}
                    IconButton{icon:"close";type:IconButton.Text;onClicked:Bridge.closePanel()}
                }
                RowLayout {
                    Layout.fillWidth:true;spacing:Appearance.spacing.small
                    opacity:browser.pHead;transform:Translate{y:browser.fx?(1-browser.pHead)*-10:0}
                    StyledRect {
                        id:searchBox
                        Layout.fillWidth:true;implicitHeight:50;radius:Appearance.rounding.full
                        color:query.activeFocus?Colours.palette.m3surfaceContainerHighest:Colours.palette.m3surfaceContainerHigh
                        Rectangle{anchors.fill:parent;radius:parent.radius;color:"transparent";border.width:2;border.color:Colours.palette.m3primary;opacity:query.activeFocus?1:0;Behavior on opacity{Anim{duration:Appearance.anim.durations.small}}}
                        RowLayout{anchors.fill:parent;anchors.leftMargin:Appearance.padding.large+Appearance.padding.small;anchors.rightMargin:Appearance.padding.larger
                            MaterialIcon{text:qsTr("search");color:query.activeFocus?Colours.palette.m3primary:Colours.palette.m3outline;scale:query.activeFocus&&browser.fx?1.15:1;Behavior on scale{Spring{}}}
                            StyledTextField{id:query;objectName:"workspaceQuery";Layout.fillWidth:true;placeholderText:"Rechercher dans "+browser.title.toLocaleLowerCase()+"  (Ctrl+F)"
                                Keys.onReturnPressed:{if(browser.entries.length)Bridge.action("launch",{id:browser.entries[0].id})}
                                // First Esc clears the text (overrides Main's Escape shortcut only when there is text); next Esc closes via Main.
                                Keys.onShortcutOverride:event=>{if(event.key===Qt.Key_Escape&&query.text.length>0)event.accepted=true}
                                Keys.onEscapePressed:{if(query.text.length>0)query.clear()}
                                Keys.onDownPressed:{grid.forceActiveFocus();if(browser.cur<0)browser.cur=0}
                            }
                            IconButton{icon:"close";type:IconButton.Text;visible:query.text.length>0;onClicked:query.clear()}
                        }
                    }
                    IconButton{icon:"star";type:IconButton.Tonal;toggle:true;checked:browser.favoritesOnly;onClicked:{browser.markBulk();browser.favoritesOnly=!browser.favoritesOnly}}
                    IconButton{objectName:"workspaceFlat";icon:"sort_by_alpha";type:browser.flatMode?IconButton.Tonal:IconButton.Text;toggle:true;checked:browser.flatMode;onClicked:{browser.markBulk();browser.flatMode=!browser.flatMode}}
                    IconButton{icon:browser.listMode?"grid_view":"view_list";type:IconButton.Text;onClicked:browser.listMode=!browser.listMode}
                }
                // Categories wrap onto several lines instead of clipping; the row collapses to one line by default.
                Item {
                    id:chipsBox
                    property bool open:false
                    readonly property bool overflow:chipFlow.implicitHeight>40
                    Layout.fillWidth:true;implicitHeight:open?chipFlow.implicitHeight:Math.min(chipFlow.implicitHeight,34);clip:true
                    opacity:browser.pHead
                    Behavior on implicitHeight{Anim{duration:Appearance.anim.durations.small}}
                    Flow{id:chipFlow;width:parent.width-(chipsBox.overflow?44:0);spacing:Appearance.spacing.small
                        Repeater{model:browser.categories
                            FilterChip{required property string modelData;label:modelData;count:modelData==="Tout"?-1:(browser.categoryCounts[modelData]??-1);selected:browser.category===modelData
                                onTriggered:{browser.markBulk();browser.category=modelData;browser.replay()}}
                        }
                    }
                    IconButton{visible:chipsBox.overflow;anchors.right:parent.right;anchors.top:parent.top;icon:"expand_more";type:IconButton.Text;rotation:chipsBox.open?180:0
                        Behavior on rotation{Spring{}}
                        onClicked:chipsBox.open=!chipsBox.open}
                }
                ListView {
                    id:grid;objectName:"workspaceGrid";Layout.fillWidth:true;Layout.fillHeight:true;clip:true;cacheBuffer:260
                    property real reveal:1
                    readonly property int columns:browser.listMode?1:Math.max(3,Math.floor(width/150))
                    readonly property real iconSize:Bridge.settings.iconSize??56
                    readonly property real cellWidth:Math.floor(width/columns)
                    readonly property real cellHeight:browser.listMode?74:Math.round(iconSize+84)
                    onColumnsChanged:browser.syncModel()
                    model:rows;reuseItems:true;currentIndex:-1
                    boundsBehavior:Flickable.StopAtBounds
                    opacity:browser.pBody
                    onMovementStarted:{browser.hoverTip(null);context.close()}
                    onContentYChanged:if(context.opened)context.close()
                    function step(d){
                        if(!browser.flat.length)return
                        const n=Math.max(0,Math.min(browser.flat.length-1,browser.cur<0?0:browser.cur+d))
                        browser.cur=n;positionViewAtIndex(browser.lineOfTile(n),ListView.Contain)
                    }
                    Keys.onReturnPressed:{if(browser.cur>=0&&browser.cur<browser.flat.length)Bridge.action("launch",{id:browser.flat[browser.cur].id})}
                    Keys.onLeftPressed:step(-1)
                    Keys.onRightPressed:step(1)
                    Keys.onDownPressed:step(columns)
                    Keys.onUpPressed:{if(browser.cur<columns){query.forceActiveFocus();browser.cur=-1}else step(-columns)}
                    Keys.onPressed:event=>{
                        if(event.text.length>0&&!(event.modifiers&(Qt.ControlModifier|Qt.AltModifier))&&event.text.charCodeAt(0)>32){query.forceActiveFocus();query.text+=event.text;query.cursorPosition=query.text.length;event.accepted=true}
                    }
                    section.property:"sec";section.criteria:ViewSection.FullString;section.labelPositioning:ViewSection.InlineLabels|ViewSection.CurrentLabelAtStart
                    section.delegate:Item {
                        id:head
                        required property string section
                        readonly property var info:browser.secInfo[section]??({label:"",count:0,closed:false,icon:""})
                        width:grid.width;height:42
                        Rectangle{anchors.fill:parent;color:Qt.alpha(Colours.palette.m3surface,.94)}
                        Rectangle{anchors.fill:parent;anchors.margins:2;radius:Appearance.rounding.small;color:Colours.palette.m3onSurface;opacity:headHit.pressed?.12:headHit.containsMouse?.07:0}
                        RowLayout{anchors.fill:parent;anchors.leftMargin:Appearance.padding.normal;anchors.rightMargin:Appearance.padding.normal;spacing:Appearance.spacing.small
                            MaterialIcon{text:"expand_more";visible:!browser.flatMode;rotation:head.info.closed?-90:0;color:Colours.palette.m3onSurfaceVariant;Behavior on rotation{Spring{}}}
                            MaterialIcon{text:head.info.icon;visible:text!=="";fill:1;color:Colours.palette.m3primary;font.pointSize:Appearance.font.size.large}
                            StyledText{text:head.info.label;font.pointSize:Appearance.font.size.normal;font.weight:Font.Medium;color:Colours.palette.m3onSurface}
                            Rectangle{implicitWidth:Math.max(24,cnt.implicitWidth+14);implicitHeight:22;radius:Appearance.rounding.full;color:Colours.palette.m3surfaceContainerHighest
                                StyledText{id:cnt;anchors.centerIn:parent;text:head.info.count;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3onSurfaceVariant}}
                            Item{Layout.fillWidth:true}
                        }
                        Rectangle{anchors.bottom:parent.bottom;anchors.left:parent.left;anchors.right:parent.right;anchors.leftMargin:Appearance.padding.normal;anchors.rightMargin:Appearance.padding.normal;height:1;color:Colours.palette.m3outlineVariant;opacity:.6}
                        MouseArea{id:headHit;objectName:"sectionHeader";anchors.fill:parent;hoverEnabled:true;enabled:!browser.flatMode&&!query.text;cursorShape:enabled?Qt.PointingHandCursor:Qt.ArrowCursor;onClicked:browser.toggleSection(head.section)}
                    }
                    delegate:Item {
                        id:line
                        required property int index
                        required property string sec
                        required property int first
                        required property int n
                        width:grid.width;height:n>0?grid.cellHeight:0;visible:n>0
                        property bool live:true
                        ListView.onPooled:{live=false;if(browser.tipTarget)browser.hoverTip(null)}
                        ListView.onReused:live=true
                        Repeater {
                            model:line.n
                            delegate:Item {
                                id:tile
                                required property int index
                                readonly property int flatIndex:line.first+index
                                readonly property var e:browser.flat[flatIndex]??({id:"",name:"",category:"",kind:"app",favorite:false})
                                readonly property string eid:e.id
                                readonly property string name:e.name
                                readonly property string category:e.category||""
                                readonly property bool folder:e.kind==="folder"
                                readonly property bool favorite:!!e.favorite
                                x:index*grid.cellWidth;width:grid.cellWidth;height:grid.cellHeight
                                // Entrance stagger: only the first rows ever have a non-final value, and only while grid.reveal runs.
                                readonly property real rt:(grid.reveal*880-Math.min(line.index,6)*55)/340
                                readonly property real t:rt<=0?0:rt>=1?1:rt
                                readonly property bool current:browser.cur===flatIndex&&grid.activeFocus
                                function backOut(x){const u=x-1;return 1+2.6*u*u*u+1.6*u*u}
                                readonly property bool hov:line.live&&hit.containsMouse
                                readonly property bool prs:line.live&&hit.pressed
                                Item {
                                    id:card
                                    anchors.fill:parent;anchors.margins:Appearance.padding.small*.8
                                    opacity:tile.t
                                    transform:Translate{y:browser.fx?(1-tile.t)*18:0}
                                    scale:browser.fx?.88+.12*tile.backOut(tile.t):1
                                    Item {
                                        id:body
                                        anchors.fill:parent
                                        // Hover lift with overshoot, press squash.
                                        scale:tile.prs?.96:tile.hov&&browser.fx?1.04:1
                                        Behavior on scale{Spring{}}
                                        Rectangle{anchors.fill:parent;radius:Appearance.rounding.normal;color:Colours.palette.m3onSurface;opacity:tile.prs?.12:tile.hov?.07:0;Behavior on opacity{Anim{duration:Appearance.anim.durations.small}}
                                            border.width:tile.current?2:0;border.color:Colours.palette.m3primary}
                                        Rectangle {
                                            id:iconWell
                                            width:browser.listMode?52:iconBox.width+24;height:width;radius:tile.folder?Appearance.rounding.large:Appearance.rounding.normal
                                            x:browser.listMode?Appearance.padding.large+Appearance.padding.small:(parent.width-width)/2
                                            y:browser.listMode?(parent.height-height)/2:Appearance.padding.normal
                                            color:tile.folder?(tile.hov?Colours.palette.m3secondaryContainer:Qt.alpha(Colours.palette.m3secondaryContainer,.7)):tile.hov?Colours.palette.m3surfaceContainerHighest:Colours.palette.m3surfaceContainerHigh
                                            Item {
                                                id:iconBox
                                                width:browser.listMode?40:grid.iconSize;height:width;anchors.centerIn:parent
                                                Image{id:logo;anchors.fill:parent;source:tile.folder||!tile.eid?"":Bridge.iconUrl(tile.eid,Bridge.iconRevision);asynchronous:true;cache:true;fillMode:Image.PreserveAspectFit
                                                    sourceSize:Qt.size(width*2,height*2);opacity:status===Image.Ready?1:0;Behavior on opacity{Anim{duration:Appearance.anim.durations.small}}}
                                                MaterialIcon{anchors.centerIn:parent;text:tile.folder?"folder":"apps";fill:tile.folder?1:0;font.pointSize:tile.folder?Appearance.font.size.large:Appearance.font.size.extraLarge;color:tile.folder?Colours.palette.m3onSecondaryContainer:Colours.palette.m3primary;visible:tile.folder||logo.status!==Image.Ready}
                                            }
                                        }
                                        MaterialIcon{x:iconWell.x+iconWell.width-width+4;y:iconWell.y-4;text:qsTr("star");fill:1;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3tertiary;visible:tile.favorite}
                                        StyledText{id:nameText;x:browser.listMode?84:Appearance.padding.normal;y:browser.listMode?14:iconWell.y+iconWell.height+8;width:browser.listMode?parent.width-104:parent.width-Appearance.padding.normal*2;text:tile.name;horizontalAlignment:browser.listMode?Text.AlignLeft:Text.AlignHCenter;font.pointSize:Appearance.font.size.small;font.weight:Font.Medium;elide:Text.ElideRight}
                                        StyledText{id:catText;x:browser.listMode?84:Appearance.padding.normal;y:browser.listMode?36:iconWell.y+iconWell.height+27;width:browser.listMode?parent.width-104:parent.width-Appearance.padding.normal*2;text:tile.category;horizontalAlignment:browser.listMode?Text.AlignLeft:Text.AlignHCenter;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline;elide:Text.ElideRight;opacity:.85}
                                        MouseArea {
                                            id:hit
                                            anchors.fill:parent;hoverEnabled:true;cursorShape:Qt.PointingHandCursor;acceptedButtons:Qt.LeftButton|Qt.RightButton
                                            // Shared tooltip carries the full name and category (either may be elided).
                                            onEntered:browser.hoverTip(tile,tile.name+(tile.category&&(catText.truncated||nameText.truncated)?" · "+tile.category:""))
                                            onExited:if(browser.tipTarget===tile)browser.hoverTip(null)
                                            onClicked:event=>{
                                                browser.hoverTip(null)
                                                if(event.button===Qt.RightButton){
                                                    browser.selectedId=tile.eid;browser.selectedName=tile.name;browser.selectedCategory=tile.category;browser.selectedFavorite=tile.favorite;browser.selectedKind=tile.e.kind||"app"
                                                    const p=hit.mapToItem(browser,event.x,event.y)
                                                    context.openAt(p.x,p.y)
                                                }else Bridge.action("launch",{id:tile.eid})
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    C.ScrollBar.vertical:StyledScrollBar{flickable:grid}
                    EmptyState {
                        id:empty
                        anchors.centerIn:parent;visible:opacity>.01&&browser.tileCount===0
                        opacity:browser.tileCount===0?1:0;scale:browser.tileCount===0||!browser.fx?1:.85
                        Behavior on opacity{Anim{duration:Appearance.anim.durations.small}}
                        Behavior on scale{Spring{}}
                        icon:query.text?"search_off":"inbox"
                        title:query.text?qsTr("Aucun accès ne correspond à cette recherche"):qsTr("Cet espace est prêt à accueillir tes accès")
                        body:query.text?"":qsTr("Ajoute une application, un dossier ou un document pour le retrouver ici.")
                        actionText:query.text?qsTr("Effacer la recherche"):qsTr("Ajouter un accès")
                        actionIcon:query.text?"close":"add"
                        onActionTriggered:{if(query.text)query.clear();else Bridge.action("addAccess",{board:browser.scope})}
                    }
                }
                RowLayout{Layout.fillWidth:true;opacity:browser.pBody;StyledText{text:qsTr("Tes fichiers restent à leur emplacement d’origine");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline;Layout.fillWidth:true} IconTextButton{text:qsTr("Tableau de bord");icon:"dashboard";type:IconTextButton.Text;onClicked:Bridge.navigate("dashboard")}}
            }
        }
    }
    // One shared tooltip instead of a Popup per tile.
    StyledRect {
        id:tip
        property bool shown:false
        z:50;visible:opacity>.01;opacity:shown?1:0;scale:shown||!browser.fx?1:.92
        implicitWidth:tipLabel.implicitWidth+Appearance.padding.large*2;implicitHeight:tipLabel.implicitHeight+Appearance.padding.normal*2
        radius:Appearance.rounding.small;color:Colours.palette.m3surfaceContainerHighest;border.width:1;border.color:Colours.palette.m3outlineVariant
        Behavior on opacity{Anim{duration:Appearance.anim.durations.small}}
        Behavior on scale{Spring{}}
        StyledText{id:tipLabel;anchors.centerIn:parent;font.pointSize:Appearance.font.size.small}
    }
    Dialog {
        id:editor;title:qsTr("Personnaliser cet accès");cardWidth:430
        StyledTextField{id:nameField;Layout.fillWidth:true;placeholderText:"Nom"}
        StyledTextField{id:categoryField;Layout.fillWidth:true;placeholderText:"Catégorie"}
        actions:[
            Item{Layout.fillWidth:true},
            TextButton{type:TextButton.Text;text:qsTr("Annuler");onClicked:editor.close()},
            TextButton{type:TextButton.Filled;text:qsTr("Enregistrer");onClicked:{Bridge.action("editEntry",{id:browser.selectedId,name:nameField.text,category:categoryField.text});editor.close()}}
        ]
    }
}
