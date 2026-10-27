// Windows entry points use the same Caelestia surfaces and controls as the dashboard.
import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
Item {
    id:root;implicitWidth:900;implicitHeight:420
    ColumnLayout {
        anchors.fill:parent;spacing:20
        RowLayout{Layout.fillWidth:true;StyledText{text:qsTr("Reprendre ton espace");font.pointSize:19;Layout.fillWidth:true} IconTextButton{text:qsTr("Tout afficher");icon:"arrow_forward";type:IconTextButton.Text;onClicked:Bridge.navigate("workspace:all")}}
        RowLayout {
            Layout.fillWidth:true;spacing:12
            Repeater{model:[{id:"apps",name:"Applications",icon:"apps"},{id:"games",name:"Jeux",icon:"sports_esports"},{id:"projects",name:"Projets",icon:"code_blocks"},{id:"documents",name:"Documents",icon:"folder_open"}]
                StyledRect{id:space;required property var modelData;objectName:"workspace-"+modelData.id;Layout.fillWidth:true;implicitHeight:134;radius:Appearance.rounding.large;color:Colours.palette.m3surfaceContainer
                    StateLayer{function onClicked(){Bridge.navigate("workspace:"+space.modelData.id)}}
                    Column{anchors.fill:parent;anchors.margins:18;spacing:8
                        MaterialIcon{text:space.modelData.icon;font.pointSize:27;color:Colours.palette.m3primary}
                        StyledText{text:space.modelData.name;font.pointSize:12}
                        StyledText{text:Bridge.apps.filter(e=>e.board===space.modelData.id).length+" accès";font.pointSize:10;color:Colours.palette.m3outline}
                    }
                }
            }
        }
        RowLayout{Layout.fillWidth:true;StyledText{text:qsTr("Tes favoris");font.pointSize:13;Layout.fillWidth:true} IconTextButton{text:qsTr("Récents");icon:"history";type:IconTextButton.Text;onClicked:Bridge.navigate("workspace:recent")}}
        Row {
            Layout.fillWidth:true;Layout.fillHeight:true;spacing:7
            Repeater{model:Bridge.favorites.slice(0,7)
                StyledRect{id:tile;required property var modelData;width:Math.min(116,(root.width-42)/7);height:100;radius:18;color:"transparent"
                    StateLayer{function onClicked(){Bridge.action("launch",{id:tile.modelData.id})}}
                    Image{id:logo;anchors.horizontalCenter:parent.horizontalCenter;y:10;width:44;height:44;fillMode:Image.PreserveAspectFit;source:Bridge.iconUrl(tile.modelData.id,Bridge.iconRevision);asynchronous:true}
                    MaterialIcon{anchors.centerIn:logo;text:qsTr("apps");visible:!logo.source.toString();color:Colours.palette.m3primary}
                    StyledText{anchors.left:parent.left;anchors.right:parent.right;y:65;text:tile.modelData.name;horizontalAlignment:Text.AlignHCenter;font.pointSize:9;elide:Text.ElideRight}
                    Component.onCompleted:Bridge.action("icon",{id:modelData.id})
                }
            }
            StyledText{visible:Bridge.favorites.length===0;text:qsTr("Ajoute des favoris depuis le clic droit d’un accès");color:Colours.palette.m3outline}
        }
        RowLayout{Layout.fillWidth:true;IconTextButton{text:qsTr("Presse-papiers");icon:"content_paste";type:IconTextButton.Text;onClicked:Bridge.navigate("clipboard")} IconTextButton{text:qsTr("Commande rapide");icon:"search";type:IconTextButton.Text;onClicked:Bridge.navigate("palette")} IconTextButton{text:qsTr("Ajouter un site");icon:"add_link";type:IconTextButton.Text;onClicked:Bridge.action("websites",{})} Item{Layout.fillWidth:true} IconTextButton{text:qsTr("Personnaliser");icon:"tune";type:IconTextButton.Text;onClicked:Bridge.navigate("settings")}}
    }
}
