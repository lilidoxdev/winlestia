import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.components.containers
import qs.services
import qs.config
Item {
    implicitHeight:Math.min(640,Bridge.screenHeight-90)
    ColumnLayout {
    id:root;spacing:18;anchors.fill:parent;

    RowLayout{Layout.fillWidth:true;MaterialIcon{text:qsTr("content_paste");font.pointSize:25;color:Colours.palette.m3primary} Column{StyledText{text:qsTr("Presse-papiers");font.pointSize:22} StyledText{text:qsTr("Clique sur un élément pour le recopier");font.pointSize:10;color:Colours.palette.m3outline}} Item{Layout.fillWidth:true} IconButton{icon:"close";type:IconButton.Text;onClicked:Bridge.closePanel()}}
    StyledRect{Layout.fillWidth:true;implicitHeight:48;radius:24;color:Colours.palette.m3surfaceContainer;StyledTextField{id:query;anchors.fill:parent;anchors.leftMargin:18;anchors.rightMargin:18;placeholderText:"Rechercher dans l’historique"}}
    StyledListView {
        id:list;Layout.fillWidth:true;Layout.fillHeight:true;clip:true;spacing:8;reuseItems:true
        model:root.visible?Bridge.clipboardItems.filter(e=>e.text.toLocaleLowerCase().includes(query.text.toLocaleLowerCase())):[]
        delegate:StyledRect {
            id:row;required property var modelData;width:list.width;implicitHeight:76;radius:18;color:Colours.palette.m3surfaceContainerLow
            StateLayer{function onClicked(){Bridge.action("clipboardCopy",{id:row.modelData.id});Bridge.closePanel()}}
            RowLayout{anchors.fill:parent;anchors.margins:14;spacing:14
                MaterialIcon{text:row.modelData.kind==="image"?"image":"notes";font.pointSize:23;color:Colours.palette.m3primary}
                Image{Layout.preferredWidth:55;Layout.preferredHeight:45;source:Bridge.localUrl(row.modelData.image);visible:row.modelData.kind==="image";asynchronous:true;fillMode:Image.PreserveAspectFit}
                Column{Layout.fillWidth:true;StyledText{width:parent.width;text:row.modelData.text||"Image copiée";maximumLineCount:2;wrapMode:Text.Wrap;elide:Text.ElideRight;font.pointSize:11} StyledText{text:row.modelData.at;font.pointSize:8;color:Colours.palette.m3outline}}
                IconButton{icon:"keep";toggle:true;checked:row.modelData.pinned;type:IconButton.Text;onClicked:Bridge.action("clipboardPin",{id:row.modelData.id})}
            }
        }
        C.ScrollBar.vertical:StyledScrollBar{flickable:list}
        StyledText{anchors.centerIn:parent;visible:list.count===0;text:Bridge.settings.clipboardHistory?"Aucun élément dans cet historique":"L’historique est désactivé dans les paramètres";color:Colours.palette.m3outline}
    }
    RowLayout{Layout.fillWidth:true;StyledText{text:qsTr("Historique local · aucun envoi en ligne");font.pointSize:9;color:Colours.palette.m3outline;Layout.fillWidth:true} IconTextButton{text:qsTr("Paramètres");icon:"settings";type:IconTextButton.Text;onClicked:Bridge.navigate("settings")}}
}
}
