import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
ColumnLayout{
    spacing:18
    RowLayout{Layout.fillWidth:true;MaterialIcon{text:qsTr("screenshot_monitor");font.pointSize:28;color:Colours.palette.m3primary} StyledText{text:qsTr("Capture d’écran");font.pointSize:23;Layout.fillWidth:true} IconButton{icon:"close";type:IconButton.Text;onClicked:Bridge.closePanel()}}
    StyledText{Layout.fillWidth:true;text:qsTr("Sélectionne, annote et partage ce qui compte.");font.pointSize:11;color:Colours.palette.m3outline;wrapMode:Text.Wrap}
    Repeater{model:[{icon:"crop_free",name:"Sélectionner une zone",detail:"Dessiner, surligner, copier, enregistrer ou épingler",mode:"region"},{icon:"document_scanner",name:"Extraire du texte",detail:"Le texte reconnu rejoint le presse-papiers",mode:"text"},{icon:"desktop_windows",name:"Copier cet écran",detail:"Une image complète de l’écran actuel",mode:"screen"}]
        StyledRect{id:card;required property var modelData;Layout.fillWidth:true;implicitHeight:95;radius:24;color:Colours.palette.m3surfaceContainer
            StateLayer{function onClicked(){Bridge.capture.start(card.modelData.mode)}}
            RowLayout{anchors.fill:parent;anchors.margins:18;spacing:16;MaterialIcon{text:card.modelData.icon;font.pointSize:28;color:Colours.palette.m3primary} ColumnLayout{Layout.fillWidth:true;spacing:6;StyledText{text:card.modelData.name;font.pointSize:13} StyledText{Layout.fillWidth:true;text:card.modelData.detail;font.pointSize:9;wrapMode:Text.Wrap;color:Colours.palette.m3outline}} MaterialIcon{text:qsTr("chevron_right")}}
        }
    }
    StyledText{text:qsTr("Ctrl + F3 · capture rapide d’une zone");font.pointSize:10;color:Colours.palette.m3outline}
}
