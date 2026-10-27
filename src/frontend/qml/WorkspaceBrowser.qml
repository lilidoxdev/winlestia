// Windows library, composed from Caelestia's original typography, ripples, menus and motion.
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
    property string scope:"apps"
    property string category:"Tout"
    property bool favoritesOnly:false
    property bool recentSort:false
    property bool listMode:false
    property string selectedId:""
    property string selectedName:""
    property string selectedCategory:""
    readonly property var spaces:[{id:"apps",name:"Applications",icon:"apps"},{id:"games",name:"Jeux",icon:"sports_esports"},{id:"projects",name:"Projets",icon:"code_blocks"},{id:"documents",name:"Documents",icon:"folder_open"}]
    readonly property string title:scope==="documents"?"Documents & médias":scope==="favorites"?"Favoris":scope==="recent"?"Récemment ouverts":spaces.find(s=>s.id===scope)?.name||"Tout ton espace"
    readonly property var base:Bridge.apps.filter(e=>(scope==="all"||scope==="favorites"||scope==="recent"||e.board===scope)&&(!(favoritesOnly||scope==="favorites")||e.favorite)&&(scope!=="recent"||e.recent>=0))
    readonly property var categories:["Tout"].concat(Array.from(new Set(base.map(e=>e.category).filter(c=>c))).sort())
    readonly property var entries:{const words=query.text.toLocaleLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g,"").split(" ").filter(w=>w);let values=base.filter(e=>(category==="Tout"||e.category===category)&&words.every(w=>(e.name+" "+e.category).toLocaleLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g,"").includes(w)));return values.sort((a,b)=>recentSort||scope==="recent"?(a.recent<0?9999:a.recent)-(b.recent<0?9999:b.recent):a.name.localeCompare(b.name))}
    function selectBoard(board){scope=board;category="Tout";query.text="";favoritesOnly=false;grid.positionViewAtBeginning()}
    Connections {target:Bridge;function onNavigationRequested(mode){if(mode.startsWith("workspace:"))browser.selectBoard(mode.split(":")[1])}}
    C.Popup {
        id:context;width:320;height:menu.implicitHeight;padding:0;background:Item{}
        contentItem:Menu {
            id:menu;expanded:context.opened
            items:[
                MenuItem{text:qsTr("Ajouter / retirer des favoris");icon:"star";onClicked:Bridge.action("favorite",{id:browser.selectedId})},
                MenuItem{text:qsTr("Afficher dans l’Explorateur");icon:"folder_open";onClicked:Bridge.action("reveal",{id:browser.selectedId})},
                MenuItem{text:qsTr("Renommer et catégoriser");icon:"edit";onClicked:{nameField.text=browser.selectedName;categoryField.text=browser.selectedCategory;editor.open()}},
                MenuItem{text:qsTr("Choisir une icône");icon:"image";onClicked:Bridge.chooseEntryIcon(browser.selectedId)},
                MenuItem{text:qsTr("Ranger dans Applications");icon:"apps";onClicked:Bridge.action("move",{id:browser.selectedId,board:"apps"})},
                MenuItem{text:qsTr("Ranger dans Jeux");icon:"sports_esports";onClicked:Bridge.action("move",{id:browser.selectedId,board:"games"})},
                MenuItem{text:qsTr("Ranger dans Projets");icon:"code_blocks";onClicked:Bridge.action("move",{id:browser.selectedId,board:"projects"})},
                MenuItem{text:qsTr("Ranger dans Documents");icon:"folder_open";onClicked:Bridge.action("move",{id:browser.selectedId,board:"documents"})}
            ]
            onItemSelected:item=>{context.close();item.clicked()}
        }
    }
    C.Popup {
        id:editor;width:430;height:230;anchors.centerIn:parent;padding:22;modal:true
        background:StyledRect{color:Colours.palette.m3surfaceContainer;radius:25;border.width:1;border.color:Colours.palette.m3outlineVariant}
        ColumnLayout {anchors.fill:parent;spacing:12
            StyledText{text:qsTr("Personnaliser cet accès");font.pointSize:17}
            StyledTextField{id:nameField;Layout.fillWidth:true;placeholderText:"Nom"}
            StyledTextField{id:categoryField;Layout.fillWidth:true;placeholderText:"Catégorie"}
            RowLayout{Item{Layout.fillWidth:true} IconTextButton{text:qsTr("Annuler");icon:"close";type:IconTextButton.Text;onClicked:editor.close()} IconTextButton{text:qsTr("Enregistrer");icon:"check";onClicked:{Bridge.action("editEntry",{id:browser.selectedId,name:nameField.text,category:categoryField.text});editor.close()}}}
        }
    }
    RowLayout {
        anchors.fill:parent;spacing:24
        ColumnLayout {
            Layout.preferredWidth:205;Layout.minimumWidth:205;Layout.maximumWidth:205;Layout.fillWidth:false;Layout.fillHeight:true;spacing:6
            RowLayout {Layout.topMargin:4;Layout.bottomMargin:22;MaterialIcon{text:qsTr("space_dashboard");font.pointSize:25;color:Colours.palette.m3primary} StyledText{text:qsTr("Ton espace");font.pointSize:19}}
            Repeater {model:browser.spaces
                StyledRect {id:space;required property var modelData;Layout.fillWidth:true;implicitHeight:60;radius:18;color:browser.scope===modelData.id?Colours.palette.m3secondaryContainer:"transparent"
                    StateLayer{function onClicked(){browser.selectBoard(space.modelData.id)}}
                    RowLayout{anchors.fill:parent;anchors.margins:13;spacing:13;MaterialIcon{text:space.modelData.icon;font.pointSize:21;color:browser.scope===space.modelData.id?Colours.palette.m3onSecondaryContainer:Colours.palette.m3onSurfaceVariant} StyledText{text:space.modelData.name;Layout.fillWidth:true;font.pointSize:11} StyledText{text:Bridge.apps.filter(e=>e.board===space.modelData.id).length;font.pointSize:10;color:Colours.palette.m3outline}}
                }
            }
            Rectangle{Layout.fillWidth:true;Layout.topMargin:15;Layout.bottomMargin:12;implicitHeight:1;color:Colours.palette.m3outlineVariant}
            Repeater {model:[{id:"favorites",name:"Favoris",icon:"star"},{id:"recent",name:"Récents",icon:"history"},{id:"all",name:"Tout afficher",icon:"grid_view"}]
                IconTextButton{required property var modelData;Layout.fillWidth:true;icon:modelData.icon;text:modelData.name;type:browser.scope===modelData.id?IconTextButton.Tonal:IconTextButton.Text;onClicked:browser.selectBoard(modelData.id)}
            }
            Item{Layout.fillHeight:true}
            IconTextButton{Layout.fillWidth:true;icon:"content_paste";text:qsTr("Presse-papiers");type:IconTextButton.Text;onClicked:Bridge.navigate("clipboard")}
            IconTextButton{Layout.fillWidth:true;icon:"settings";text:qsTr("Personnaliser");type:IconTextButton.Text;onClicked:Bridge.navigate("settings")}
        }
        ColumnLayout {
            Layout.fillWidth:true;Layout.fillHeight:true;spacing:16
            RowLayout {
                Layout.fillWidth:true
                Column{spacing:4;StyledText{text:browser.title;font.pointSize:24} StyledText{text:browser.entries.length+" accès · Clique pour ouvrir, clic droit pour organiser";font.pointSize:10;color:Colours.palette.m3outline}}
                Item{Layout.fillWidth:true}
                IconButton{icon:"search";type:IconButton.Text;onClicked:Bridge.navigate("palette")}
                IconButton{icon:"add";type:IconButton.Text;onClicked:Bridge.action("addAccess",{board:browser.scope})}
                IconButton{icon:"create_new_folder";type:IconButton.Text;visible:browser.scope==="projects"||browser.scope==="documents";onClicked:Bridge.action("addFolder",{board:browser.scope})}
                IconButton{icon:"close";type:IconButton.Text;onClicked:Bridge.closePanel()}
            }
            StyledRect {
                Layout.fillWidth:true;implicitHeight:50;radius:25;color:Colours.layer(Colours.palette.m3surfaceContainer,2)
                RowLayout{anchors.fill:parent;anchors.leftMargin:16;anchors.rightMargin:12;MaterialIcon{text:qsTr("search");color:Colours.palette.m3outline} StyledTextField{id:query;objectName:"workspaceQuery";Layout.fillWidth:true;placeholderText:"Rechercher dans "+browser.title.toLocaleLowerCase();Keys.onReturnPressed:{if(grid.count)Bridge.action("launch",{id:browser.entries[0].id})}} IconButton{icon:"close";type:IconButton.Text;visible:query.text.length>0;onClicked:query.clear()}}
            }
            RowLayout {
                Layout.fillWidth:true
                Flickable{id:categoryScroll;Layout.fillWidth:true;implicitHeight:38;contentWidth:chips.implicitWidth;clip:true;boundsBehavior:Flickable.StopAtBounds
                    Row{id:chips;spacing:6;Repeater{model:browser.categories;IconTextButton{required property string modelData;icon:browser.category===modelData?"check":"";text:modelData;type:browser.category===modelData?IconTextButton.Tonal:IconTextButton.Text;onClicked:browser.category=modelData}}}
                    MouseArea{anchors.fill:parent;acceptedButtons:Qt.NoButton;onWheel:event=>{categoryScroll.contentX=Math.max(0,Math.min(Math.max(0,categoryScroll.contentWidth-categoryScroll.width),categoryScroll.contentX-event.angleDelta.y));event.accepted=true}}
                }
                IconButton{icon:"star";type:IconButton.Tonal;toggle:true;checked:browser.favoritesOnly;onClicked:browser.favoritesOnly=!browser.favoritesOnly}
                IconButton{icon:browser.recentSort?"history":"sort_by_alpha";type:IconButton.Text;onClicked:browser.recentSort=!browser.recentSort}
                IconButton{icon:browser.listMode?"grid_view":"view_list";type:IconButton.Text;onClicked:browser.listMode=!browser.listMode}
            }
            GridView {
                id:grid;objectName:"workspaceGrid";Layout.fillWidth:true;Layout.fillHeight:true;clip:true;cacheBuffer:120
                cellWidth:browser.listMode?width:Math.floor(width/Math.max(3,Math.floor(width/140)))
                cellHeight:browser.listMode?74:Math.round((Bridge.settings.iconSize??56)*.6+99)
                model:browser.visible?browser.entries:[]
                boundsBehavior:Flickable.StopAtBounds
                delegate:Item {
                    id:tile;required property var modelData;required property int index;width:grid.cellWidth;height:grid.cellHeight
                    StyledRect {anchors.fill:parent;anchors.margins:4;radius:18;color:"transparent"
                        StateLayer{id:tileLayer;function onClicked(){Bridge.action("launch",{id:tile.modelData.id})}}
                        C.ToolTip {visible:tileLayer.containsMouse&&browser.visible;delay:650;text:tile.modelData.name
                            contentItem:StyledText{text:tile.modelData.name;font.pointSize:10}
                            background:StyledRect{radius:12;color:Colours.palette.m3surfaceContainerHighest;border.width:1;border.color:Colours.palette.m3outlineVariant}
                        }
                        MouseArea{anchors.fill:parent;acceptedButtons:Qt.RightButton;onClicked:event=>{browser.selectedId=tile.modelData.id;browser.selectedName=tile.modelData.name;browser.selectedCategory=tile.modelData.category;let p=tile.mapToItem(browser,event.x,event.y);context.x=Math.min(p.x,browser.width-context.width);context.y=Math.max(0,Math.min(p.y,browser.height-context.height));context.open()}}
                        Item {id:iconBox;width:browser.listMode?40:(Bridge.settings.iconSize??56);height:width;x:browser.listMode?14:(parent.width-width)/2;y:browser.listMode?(parent.height-height)/2:13
                            Image{id:logo;anchors.fill:parent;source:Bridge.iconUrl(tile.modelData.id,Bridge.iconRevision);asynchronous:true;fillMode:Image.PreserveAspectFit}
                            MaterialIcon{anchors.centerIn:parent;text:tile.modelData.kind==="folder"?"folder":"apps";font.pointSize:30;color:Colours.palette.m3primary;visible:!logo.source.toString()}
                        }
                        MaterialIcon{anchors.top:parent.top;anchors.right:parent.right;anchors.margins:8;text:qsTr("star");fill:1;font.pointSize:10;color:Colours.palette.m3tertiary;visible:tile.modelData.favorite}
                        StyledText{x:browser.listMode?70:8;y:browser.listMode?14:iconBox.y+iconBox.height+10;width:browser.listMode?parent.width-90:parent.width-16;text:tile.modelData.name;horizontalAlignment:browser.listMode?Text.AlignLeft:Text.AlignHCenter;font.pointSize:10;elide:Text.ElideRight}
                        StyledText{x:browser.listMode?70:8;y:browser.listMode?36:parent.height-22;width:browser.listMode?parent.width-90:parent.width-16;text:tile.modelData.category;horizontalAlignment:browser.listMode?Text.AlignLeft:Text.AlignHCenter;font.pointSize:8;color:Colours.palette.m3outline;elide:Text.ElideRight}
                    }
                    Component.onCompleted:Bridge.action("icon",{id:modelData.id})
                }
                displaced:Transition{Anim{properties:"x,y";duration:Appearance.anim.durations.small}}
                C.ScrollBar.vertical:StyledScrollBar{flickable:grid}
                StyledText{anchors.centerIn:parent;visible:grid.count===0;text:query.text?"Aucun accès ne correspond à cette recherche":"Cet espace est prêt à accueillir tes accès";color:Colours.palette.m3outline}
            }
            RowLayout{Layout.fillWidth:true;StyledText{text:qsTr("Tes fichiers restent à leur emplacement d’origine");font.pointSize:9;color:Colours.palette.m3outline;Layout.fillWidth:true} IconTextButton{text:qsTr("Tableau de bord");icon:"dashboard";type:IconTextButton.Text;onClicked:Bridge.navigate("dashboard")}}
        }
    }
}
