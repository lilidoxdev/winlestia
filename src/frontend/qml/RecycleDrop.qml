// QtQuick / RecycleDrop.qml / Caelestia file action shelf, accepts only external file URLs.
import QtQuick
import QtQuick.Window
import qs.config
import qs.services
import qs.components
Window {
 id:shelf;transientParent:null;title:"Winlestia · Actions fichiers";color:"transparent"
 flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.WindowDoesNotAcceptFocus|Qt.NoDropShadowWindowHint
 width:Math.min(760,Bridge.screenWidth-32);height:128
 x:Bridge.screenX+(Bridge.screenWidth-width)/2;y:Bridge.screenY+Math.max(0,Bridge.frameThickness-4)
 visible:Bridge.fileDragNearTop||sensor.containsDrag
 property bool hasFiles:false
 property int count:0
 property int hovered:-1
 property real reveal:hasFiles?1:0
 Behavior on reveal {Anim{duration:Appearance.anim.durations.small;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
 function localPaths(urls){let result=[];for(let url of urls){let value=url.toString();if(value.startsWith("file:///"))result.push(decodeURIComponent(value.substring(8)).replace(/\//g,"\\"))}return result}
 function perform(index,urls){let paths=localPaths(urls);if(!paths.length)return;Bridge.action(["recycleFiles","clipboardFiles","zipFiles"][index],{paths:paths});hasFiles=false;hovered=-1}
 onVisibleChanged:if(!visible){hasFiles=false;hovered=-1}
 Rectangle{
  anchors.fill:parent;anchors.bottomMargin:6;radius:Appearance.rounding.large
  color:Qt.alpha(Colours.palette.m3surfaceContainer,Bridge.glassEnabled ? .94 : 1)
  border.width:1;border.color:Colours.palette.m3outlineVariant
  opacity:shelf.reveal;scale:.94+.06*shelf.reveal;transformOrigin:Item.Top
  transform:Translate{y:-14*(1-shelf.reveal)}
  Row{
   anchors.centerIn:parent;spacing:12
   Repeater{model:[{icon:"delete",label:"Corbeille",hint:"Déposer pour supprimer"},{icon:"content_copy",label:"Presse-papiers",hint:"Copier les fichiers"},{icon:"folder_zip",label:"Compresser",hint:"Créer une archive ZIP"}]
    delegate:Rectangle{
     id:tile;required property var modelData;required property int index
     objectName:"fileAction-"+index;width:212;height:92;radius:shelf.hovered===index?Appearance.rounding.large:Appearance.rounding.normal
     color:shelf.hovered===index?Colours.palette.m3primaryContainer:Colours.palette.m3surfaceContainerHigh
     border.width:shelf.hovered===index?1:0;border.color:Colours.palette.m3primary
     scale:shelf.hovered===index?1.035:1;y:shelf.hovered===index?-3:0
     Behavior on color{CAnim{duration:Appearance.anim.durations.small}}Behavior on radius{Anim{duration:Appearance.anim.durations.small}}Behavior on scale{Anim{duration:Appearance.anim.durations.small}}Behavior on y{Anim{duration:Appearance.anim.durations.small}}
     Row{anchors.centerIn:parent;spacing:12
      Rectangle{width:44;height:48;radius:Appearance.rounding.small;color:shelf.hovered===tile.index?Colours.palette.m3primary:Colours.palette.m3secondaryContainer
       MaterialIcon{anchors.centerIn:parent;text:tile.modelData.icon;font.pointSize:Appearance.font.size.title;color:shelf.hovered===tile.index?Colours.palette.m3onPrimary:Colours.palette.m3onSecondaryContainer;fill:shelf.hovered===tile.index?1:0;rotation:shelf.hovered===tile.index?-8:0;Behavior on rotation{Anim{duration:Appearance.anim.durations.small}}}}
      Column{anchors.verticalCenter:parent.verticalCenter;spacing:5;StyledText{text:tile.modelData.label;font.pointSize:Appearance.font.size.normal;font.weight:Font.DemiBold;color:shelf.hovered===tile.index?Colours.palette.m3onPrimaryContainer:Colours.palette.m3onSurface}StyledText{text:tile.modelData.hint;font.pointSize:Appearance.font.size.small;color:shelf.hovered===tile.index?Colours.palette.m3onPrimaryContainer:Colours.palette.m3onSurfaceVariant}}
     }
    }
   }
  }
 }
 DropArea{
  id:sensor;anchors.fill:parent
  function update(x){shelf.hovered=Math.max(0,Math.min(2,Math.floor((x-(shelf.width-660)/2)/224)))}
  onEntered:drag=>{drag.accepted=drag.hasUrls&&shelf.localPaths(drag.urls).length>0;if(drag.accepted){shelf.hasFiles=true;shelf.count=drag.urls.length;update(drag.x)}}
  onPositionChanged:drag=>update(drag.x)
  onExited:{shelf.hasFiles=false;shelf.hovered=-1}
  onDropped:drop=>{if(drop.hasUrls){shelf.perform(shelf.hovered<0?0:shelf.hovered,drop.urls);drop.acceptProposedAction()}}
 }
}
