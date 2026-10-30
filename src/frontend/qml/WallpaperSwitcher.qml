// Fullscreen wallpaper switcher (Caelestia wallpaper picker layout). Created by a Loader that is active only while open: no timer, no animation and no image exists at idle.
// The background is a live preview of the selected file; the system wallpaper only changes on Enter.
import QtQuick
import QtQuick.Window
import qs.components
import qs.components.controls
import qs.services
import qs.config
Window{
 id:root;objectName:"wallpaperSwitcher";title:"Panneaux · Fonds d’écran";transientParent:null;color:Colours.palette.m3scrim
 flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.NoDropShadowWindowHint
 x:Bridge.screenX;y:Bridge.screenY;width:Bridge.screenWidth;height:Bridge.screenHeight;visible:true
 signal closeRequested()
 readonly property bool reduced:Appearance.reducedEffects
 readonly property real ratio:Bridge.screenWidth/Math.max(1,Bridge.screenHeight)
 readonly property int selectedHeight:Math.round(Bridge.screenHeight*.17)
 readonly property int normalHeight:Math.round(selectedHeight*.68)
 property int current:0
 property bool dialogOpen:false
 property bool confirmDelete:false
 property string wantPath:""
 property bool seenActive:false
 property int front:0
 property int pending:-1
 readonly property string currentPath:current>=0&&current<items.count?items.get(current).path:""
 readonly property string currentUrl:current>=0&&current<items.count?items.get(current).url:""
 readonly property string currentName:current>=0&&current<items.count?items.get(current).name:""
 readonly property bool currentRemovable:current>=0&&current<items.count&&items.get(current).origin==="user"

 ListModel{id:items}
 function norm(value){return String(value||"").toLowerCase().split("\\").join("/").replace("file:///","")}
 function load(){
  var keep=wantPath||currentPath,choices=Bridge.wallpaperChoices(""),want=keep||norm((Bridge.wallpaperInfo||{}).source);wantPath="";confirmDelete=false;
  items.clear();var index=0;
  for(var i=0;i<choices.length;i++){
   items.append({path:choices[i].path,name:choices[i].name,url:choices[i].url,origin:choices[i].origin||"",thumb:""})
   if(want&&norm(choices[i].path)===norm(want))index=i
  }
  current=index;list.currentIndex=index;show(currentUrl)
  var order=[];for(var j=0;j<choices.length;j++)order.push(choices[(index+j)%choices.length].path)
  Bridge.requestWallpaperThumbs(order)
 }
 function move(step){if(items.count<1)return;confirmDelete=false;current=(current+step+items.count)%items.count;list.currentIndex=current;show(currentUrl)}
 function select(index){if(index<0||index>=items.count)return;confirmDelete=false;current=index;list.currentIndex=index;show(currentUrl)}
 function apply(){if(currentPath!=="")Bridge.setWallpaper(currentPath);closeRequested()}
 // The native dialog cannot sit above this topmost frameless window: hide it while the dialog is open (no auto-close meanwhile), then come back with focus.
 function addImages(){if(dialogOpen)return;confirmDelete=false;dialogOpen=true;visible=false;addTimer.start()}
 function finishAdd(){
  var added=Bridge.addWallpaperImages();dialogOpen=false;visible=true;requestActivate();keys.forceActiveFocus()
  if(added.length>0){wantPath=norm(added[0]);Bridge.refreshWallpapersNow()}
 }
 function removeCurrent(){
  var path=currentPath,next=current+1<items.count?items.get(current+1).path:current>0?items.get(current-1).path:""
  confirmDelete=false
  if(path!==""&&Bridge.removeWallpaper(path)){wantPath=norm(next);Bridge.refreshWallpapersNow()}
 }
 Timer{id:addTimer;interval:80;repeat:false;onTriggered:root.finishAdd()}
 // Cross-fade: the hidden layer loads the next file asynchronously and takes over once Ready.
 function show(url){
  if(url==="")return
  var layers=[layerA,layerB],visibleLayer=layers[front],next=layers[1-front]
  if(visibleLayer.source.toString()===url){pending=-1;return}
  if(next.source.toString()===url&&next.status===Image.Ready){front=1-front;pending=-1;return}
  pending=1-front;next.source=url
 }
 function layerReady(index){if(pending===index){front=index;pending=-1}}

 Component.onCompleted:{Bridge.refreshWallpapers();load();requestActivate();keys.forceActiveFocus()}
 onVisibleChanged:if(visible){requestActivate();keys.forceActiveFocus()}
 onActiveChanged:{if(active)seenActive=true;else if(seenActive&&!dialogOpen)closeRequested()}
 onClosing:event=>{event.accepted=false;closeRequested()}
 Connections{target:Bridge
  function onWallpapersReady(){root.load()}
  function onWallpaperThumbReady(path,url){for(var i=0;i<items.count;i++)if(items.get(i).path===path){items.setProperty(i,"thumb",url);break}}
 }

 Item{
  id:keys;anchors.fill:parent;focus:true
  Keys.onPressed:event=>{
   if(root.confirmDelete){
    if(event.key===Qt.Key_Return||event.key===Qt.Key_Enter)root.removeCurrent()
    else root.confirmDelete=false
    event.accepted=true;return
   }
   if(event.key===Qt.Key_Left)root.move(-1)
   else if(event.key===Qt.Key_Right)root.move(1)
   else if(event.key===Qt.Key_Return||event.key===Qt.Key_Enter)root.apply()
   else if(event.key===Qt.Key_Escape)root.closeRequested()
   else if(event.key===Qt.Key_O&&(event.modifiers&Qt.ControlModifier))root.addImages()
   else if(event.key===Qt.Key_Delete){if(root.currentRemovable)root.confirmDelete=true}
   else return
   event.accepted=true
  }
  MouseArea{anchors.fill:parent;onWheel:wheel=>{root.move(wheel.angleDelta.y>0||wheel.angleDelta.x>0?-1:1)}}

  Image{id:layerA;anchors.fill:parent;asynchronous:true;cache:false;fillMode:Image.PreserveAspectCrop;sourceSize:Qt.size(Bridge.screenWidth,Bridge.screenHeight)
   opacity:root.front===0?1:0;onStatusChanged:if(status===Image.Ready)root.layerReady(0)
   Behavior on opacity{enabled:!root.reduced;Anim{duration:250}}}
  Image{id:layerB;anchors.fill:parent;asynchronous:true;cache:false;fillMode:Image.PreserveAspectCrop;sourceSize:Qt.size(Bridge.screenWidth,Bridge.screenHeight)
   opacity:root.front===1?1:0;onStatusChanged:if(status===Image.Ready)root.layerReady(1)
   Behavior on opacity{enabled:!root.reduced;Anim{duration:250}}}

  Rectangle{anchors.left:parent.left;anchors.right:parent.right;anchors.bottom:parent.bottom;height:parent.height*.4
   gradient:Gradient{GradientStop{position:0;color:Qt.alpha(Colours.palette.m3scrim,0)}GradientStop{position:1;color:Qt.alpha(Colours.palette.m3scrim,.7)}}}

  StyledRect{
   visible:root.confirmDelete;anchors.horizontalCenter:parent.horizontalCenter;anchors.top:parent.top;anchors.topMargin:Appearance.padding.large*2
   radius:Appearance.rounding.full;color:Qt.alpha(Colours.palette.m3surfaceContainer,.92)
   implicitWidth:confirmText.implicitWidth+Appearance.padding.large*2;implicitHeight:confirmText.implicitHeight+Appearance.padding.normal*2
   StyledText{id:confirmText;anchors.centerIn:parent;text:qsTr("Supprimer ce fond de votre dossier ? Entrée confirmer · Échap annuler");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3onSurface}
  }

  StyledText{
   visible:items.count===0;anchors.centerIn:parent;text:qsTr("Aucun fond d’écran trouvé");font.pointSize:Appearance.font.size.larger;color:Colours.palette.m3onSurface
  }

  Column{
   anchors.horizontalCenter:parent.horizontalCenter;anchors.bottom:parent.bottom;anchors.bottomMargin:Appearance.padding.large*2;width:parent.width;spacing:Appearance.spacing.normal
   ListView{
    id:list;width:parent.width;height:root.selectedHeight+Appearance.padding.normal*2;orientation:ListView.Horizontal;model:items;spacing:Appearance.spacing.normal;clip:false;interactive:false
    highlightRangeMode:ListView.StrictlyEnforceRange;highlightMoveDuration:root.reduced?0:250
    preferredHighlightBegin:(width-root.selectedHeight*root.ratio)/2;preferredHighlightEnd:(width+root.selectedHeight*root.ratio)/2
    delegate:Item{
     id:cell;required property int index;required property string name;required property string thumb
     readonly property bool selected:ListView.isCurrentItem
     width:(selected?root.selectedHeight:root.normalHeight)*root.ratio;height:list.height
     Behavior on width{enabled:!root.reduced;Anim{duration:250}}
     StyledClippingRect{
      id:frame;anchors.horizontalCenter:parent.horizontalCenter;anchors.verticalCenter:parent.verticalCenter
      width:parent.width;height:cell.selected?root.selectedHeight:root.normalHeight;radius:Appearance.rounding.normal;color:Colours.palette.m3surfaceContainerHigh
      Behavior on height{enabled:!root.reduced;Anim{duration:250}}
      Image{anchors.fill:parent;source:cell.thumb;asynchronous:true;cache:false;fillMode:Image.PreserveAspectCrop;sourceSize.width:480}
     }
     Rectangle{anchors.fill:frame;radius:Appearance.rounding.normal;color:"transparent";border.width:cell.selected?3:0;border.color:Colours.palette.m3primary}
     MouseArea{anchors.fill:parent;onClicked:root.select(cell.index)}
    }
   }
   Row{
    anchors.horizontalCenter:parent.horizontalCenter;spacing:Appearance.spacing.normal
    StyledText{text:root.currentName;font.pointSize:Appearance.font.size.normal;color:Colours.palette.m3onSurface;elide:Text.ElideRight;width:Math.min(implicitWidth,root.width*.6)}
    StyledText{visible:items.count>0;text:(root.current+1)+" / "+items.count;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3onSurfaceVariant}
   }
   Row{
    anchors.horizontalCenter:parent.horizontalCenter;spacing:Appearance.spacing.normal
    IconTextButton{text:qsTr("Ajouter des images");icon:"add_photo_alternate";type:IconTextButton.Tonal;onClicked:root.addImages()}
    IconTextButton{text:qsTr("Ouvrir le dossier");icon:"folder_open";type:IconTextButton.Text;onClicked:Bridge.openWallpaperFolder()}
   }
   StyledText{anchors.horizontalCenter:parent.horizontalCenter;text:qsTr("← → choisir · Entrée appliquer · Ctrl+O ajouter · Suppr retirer · Échap fermer");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
  }
 }
}
