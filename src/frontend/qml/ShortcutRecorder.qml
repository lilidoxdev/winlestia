// Independent shortcut recorder. The dashboard is hidden while this focused window owns input.
import QtQuick
import QtQuick.Window
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
Window{
 id:recorder;objectName:"shortcutRecorder";title:"Winlestia · Raccourci";transientParent:null;color:"transparent"
 flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.NoDropShadowWindowHint
 width:480;height:245;x:Bridge.screenX+(Bridge.screenWidth-width)/2;y:Bridge.screenY+(Bridge.screenHeight-height)/2
 property string kind:"panel";property string draft:""
 function openFor(value){kind=value;draft="";Bridge.recordShortcut(true,kind);Bridge.window.hide();visible=true;requestActivate();input.forceActiveFocus()}
 function finish(){if(!visible)return;visible=false;Bridge.recordShortcut(false,kind);Bridge.window.show();Bridge.navigate("settings")}
 Connections{target:Bridge;function onShellEvent(event){if(event==="shortcut-saved:"+recorder.kind)recorder.finish()}}
 onClosing:event=>{event.accepted=false;finish()}
 Rectangle{anchors.fill:parent;radius:24;color:Qt.alpha(Colours.surfaceBase,.98);border.width:1;border.color:Qt.alpha(Colours.palette.m3primary,.5)
  ColumnLayout{anchors.fill:parent;anchors.margins:24;spacing:14
   RowLayout{Layout.fillWidth:true;MaterialIcon{text:"keyboard";font.pointSize:25;color:Colours.palette.m3primary}StyledText{Layout.fillWidth:true;text:qsTr("Appuie sur le nouveau raccourci");font.pointSize:15}IconButton{icon:"close";type:IconButton.Text;onClicked:recorder.finish()}}
   StyledRect{id:input;objectName:"shortcutInput";Layout.fillWidth:true;Layout.preferredHeight:66;radius:15;color:Colours.palette.m3surfaceContainerHigh;focus:true
    StyledText{anchors.centerIn:parent;text:recorder.draft||qsTr("Ctrl + une touche");font.pointSize:22;color:Colours.palette.m3primary}
    Keys.onPressed:event=>{event.accepted=true;if(event.key===Qt.Key_Escape){recorder.finish();return}if(event.key>=Qt.Key_Shift&&event.key<=Qt.Key_Alt)return;recorder.draft=Bridge.shortcutText(event.key,event.modifiers);Bridge.captureShortcut(recorder.kind,event.key,event.modifiers)}
   }
   StyledText{Layout.fillWidth:true;text:Bridge.notice||qsTr("Le raccourci est enregistré automatiquement.");font.pointSize:10;wrapMode:Text.WordWrap;color:Colours.palette.m3onSurfaceVariant}
   StyledText{text:qsTr("Échap pour annuler");font.pointSize:9;color:Colours.palette.m3outline}
  }
 }
}
