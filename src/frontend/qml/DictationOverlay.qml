// Push-to-talk dictation pill. Click-through, never takes focus, fully hidden (no animation, no rendering) when idle.
import QtQuick
import QtQuick.Window
import qs.components
import qs.services
import qs.config
Window{
 id:root;objectName:"dictationOverlay";title:"Panneaux · Dictée";transientParent:null;color:"transparent"
 flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.NoDropShadowWindowHint|Qt.WindowDoesNotAcceptFocus|Qt.WindowTransparentForInput
 // The background is not drawn here: ScreenFrame draws it (same surfaceColor, same dock_path silhouette fused with the frame band, see surface_geometry.py).
 // This window only holds the content, sliding out of the bottom band exactly like Dock.qml.
 property var dock:null
 readonly property rect area:Qt.rect(Bridge.dictationArea.x??0,Bridge.dictationArea.y??0,Bridge.dictationArea.width??0,Bridge.dictationArea.height??0)
 readonly property bool reduced:Appearance.reducedEffects
 readonly property string st:Bridge.dictationState
 readonly property real r:26
 property bool shown:false
 property bool live:false
 // The dictation shares the bottom-centre slot with the dock: the dock retracts first (and is restored once the pill is gone), the pill emerges only when the slot is free.
 property bool dockWas:false
 readonly property bool dockClear:!dock||!dock.visible||(!dock.shown&&(dock.reduced?dock.fade<0.05:dock.offset>=dock.height-2))
 readonly property bool open:shown&&dockClear
 property real fade:open?1:0
 Behavior on fade{enabled:root.reduced;NumberAnimation{duration:120;easing.type:Easing.OutCubic}}
 property real offset:reduced||open?0:height
 Behavior on offset{Anim{duration:Appearance.anim.durations.expressiveFastSpatial;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
 readonly property real contentOpacity:reduced?fade:Math.max(0,Math.min(1,1-offset/(height*0.6)))
 // Content width animated toward the state-dependent size; pillWidth (body + both concave shoulders) is read by the Python surface scene.
 property real animW:targetWidth
 Behavior on animW{NumberAnimation{duration:root.reduced?Appearance.anim.durations.small:Appearance.anim.durations.expressiveDefaultSpatial*0.8;easing.type:Easing.BezierSpline;easing.bezierCurve:root.reduced?Appearance.anim.curves.standard:Appearance.anim.curves.emphasized}}
 readonly property real pillWidth:animW+2*r
 width:480;height:64
 x:area.x+(area.width-width)/2
 y:area.y+area.height-height-(Bridge.screenFullscreen?12:Math.max(0,Config.border.thickness))
 visible:area.width>0&&live
 function settle(){
  if(open)live=true
  else if(live&&(reduced?fade<=0.01:offset>=height-1))live=false
  if(!shown&&!live&&dockWas){dockWas=false;if(dock)dock.shown=true}
 }
 onOpenChanged:settle()
 onOffsetChanged:{Bridge.updateSurfaces();settle()}
 onFadeChanged:settle()
 onReducedChanged:settle()
 onPillWidthChanged:Bridge.updateSurfaces()
 onLiveChanged:if(live){raise();Bridge.updateSurfaces()}
 onStChanged:{
  var want=st!=="idle"
  if(want&&!shown&&dock&&dock.shown){dockWas=true;dock.shown=false}
  shown=want;settle()
 }
 Connections{target:root.dock;function onShownChanged(){if(root.dock.shown&&root.shown){root.dockWas=true;root.dock.shown=false}}}
 Timer{id:hideTimer;interval:root.st==="error"?2800:1500;running:root.st==="done"||root.st==="error";onTriggered:root.shown=false}
 // Smoothed bar heights, driven per frame only while the window is visible.
 property real t:0
 property real lvl:0
 property var smooth:new Array(32).fill(0)
 FrameAnimation{
  running:root.visible
  onTriggered:{
   var dt=Math.min(frameTime,0.05);root.t+=dt
   var levels=Bridge.dictationLevels,s=root.st,tt=root.t,active=(s==="listening")
   var up=1-Math.exp(-dt*28),down=1-Math.exp(-dt*9)
   for(var i=0;i<32;i++){
    var x=(i-15.5)/15.5,env=0.55+0.45*Math.exp(-x*x*2.2),target
    if(s==="listening")target=Math.min(1,(levels[i]||0)*env*1.15)+(root.reduced?0.04:0.05+0.05*Math.sin(tt*2.2+i*0.45)*(1-Bridge.dictationLevel))
    else if(s==="loading")target=root.reduced?0.06:0.1+0.07*Math.sin(tt*3-i*0.3)
    else if(s==="transcribing")target=(0.2+0.16*Math.sin(tt*4-i*0.5)+0.06*Math.sin(tt*7+i*0.9))*env
    else target=0.04
    var v=root.smooth[i];v+=(target-v)*(target>v?up:down);root.smooth[i]=v
    var bar=bars.itemAt(i);if(bar)bar.v=v
   }
   root.lvl+=((active?Bridge.dictationLevel:0)-root.lvl)*(active?up:down)
  }
 }
 readonly property real contentX:68
 readonly property real targetWidth:{
  if(st==="loading")return contentX+loadLabel.implicitWidth+32
  if(st==="transcribing")return contentX+96+10+transLabel.implicitWidth+10+24+24
  if(st==="typing"||st==="done")return contentX+(chip.visible?chip.width+8:0)+Math.min(preview.implicitWidth,240)+28
  if(st==="error")return contentX+Math.min(errLabel.implicitWidth,300)+28
  return contentX+192+24
 }
 // Fixed clip box = silhouette bounds; only the content slides (same as Dock.qml).
 Item{
  anchors.horizontalCenter:parent.horizontalCenter;anchors.bottom:parent.bottom;width:root.pillWidth;height:parent.height;clip:true
 Item{
  id:pill;x:root.r;width:root.animW;height:parent.height;opacity:root.contentOpacity;transform:Translate{y:root.offset}
  // Mic circle + level halo
  Rectangle{
   id:halo;visible:!root.reduced&&root.st==="listening";width:40+root.lvl*24;height:width;radius:width/2;x:34-width/2;anchors.verticalCenter:parent.verticalCenter
   color:Colours.palette.m3primary;opacity:0.1+0.4*root.lvl
  }
  Rectangle{
   id:badge;width:40;height:40;radius:20;x:14;anchors.verticalCenter:parent.verticalCenter
   color:root.st==="error"?Colours.palette.m3error:Colours.palette.m3primary
   Behavior on color{ColorAnimation{duration:Appearance.anim.durations.small}}
   MaterialIcon{
    anchors.centerIn:parent;fill:1;font.pointSize:Appearance.font.size.large
    color:root.st==="error"?Colours.palette.m3onError:Colours.palette.m3onPrimary
    text:root.st==="error"?"error":root.st==="done"?"check":root.st==="typing"?"keyboard":"mic"
   }
  }
  // Sonograph
  Item{
   id:barsBox;x:root.contentX;width:root.st==="transcribing"?96:192;height:parent.height
   opacity:(root.st==="listening"||root.st==="transcribing")?1:0;visible:opacity>0.01
   Behavior on opacity{NumberAnimation{duration:Appearance.anim.durations.small}}
   Behavior on width{NumberAnimation{duration:root.reduced?Appearance.anim.durations.small:Appearance.anim.durations.expressiveDefaultSpatial*0.8;easing.type:Easing.BezierSpline;easing.bezierCurve:root.reduced?Appearance.anim.curves.standard:Appearance.anim.curves.emphasized}}
   Repeater{
    id:bars;model:32
    Rectangle{
     required property int index
     property real v:0
     readonly property real slot:barsBox.width/32
     width:Math.max(1.5,slot*0.55);height:Math.max(width,v*38);radius:width/2;x:index*slot+(slot-width)/2;anchors.verticalCenter:parent.verticalCenter
     color:root.reduced?Colours.palette.m3primary:Qt.tint(Colours.palette.m3primary,Qt.alpha(Colours.palette.m3tertiary,index/31))
    }
   }
  }
  // Transcribing label + dots
  StyledText{
   id:transLabel;x:root.contentX+96+10;anchors.verticalCenter:parent.verticalCenter;text:qsTr("Transcription")
   color:Colours.palette.m3onSurface;opacity:root.st==="transcribing"?1:0;visible:opacity>0.01
   Behavior on opacity{NumberAnimation{duration:Appearance.anim.durations.small}}
  }
  Row{
   x:transLabel.x+transLabel.implicitWidth+8;anchors.verticalCenter:parent.verticalCenter;spacing:4
   opacity:root.st==="transcribing"?1:0;visible:opacity>0.01
   Repeater{model:3;Rectangle{required property int index;width:5;height:5;radius:2.5;color:Colours.palette.m3primary;opacity:0.3+0.7*Math.max(0,Math.sin(root.t*6-index*0.9))}}
  }
  // Loading label with shimmer
  Item{
   id:loadBox;x:root.contentX;width:loadLabel.implicitWidth;height:parent.height;clip:true
   opacity:root.st==="loading"?1:0;visible:opacity>0.01
   Behavior on opacity{NumberAnimation{duration:Appearance.anim.durations.small}}
   StyledText{id:loadLabel;anchors.verticalCenter:parent.verticalCenter;text:qsTr("Préparation du modèle…");color:Colours.palette.m3onSurface}
   Rectangle{
    visible:!root.reduced;width:44;height:parent.height;x:((root.t*0.9)%1.6-0.3)*(loadBox.width+44)-44
    gradient:Gradient{orientation:Gradient.Horizontal
     GradientStop{position:0;color:"transparent"}
     GradientStop{position:0.5;color:Qt.alpha(Colours.palette.m3primary,0.35)}
     GradientStop{position:1;color:"transparent"}
    }
   }
  }
  // Done / typing: language chip + preview
  Rectangle{
   id:chip;visible:(root.st==="done"||root.st==="typing")&&Bridge.dictationLang!==""
   x:root.contentX;width:34;height:22;radius:11;anchors.verticalCenter:parent.verticalCenter;color:Colours.palette.m3secondaryContainer
   StyledText{anchors.centerIn:parent;text:Bridge.dictationLang.toUpperCase();font.pointSize:Appearance.font.size.smaller;font.bold:true;color:Colours.palette.m3onSecondaryContainer}
  }
  StyledText{
   id:preview;color:Colours.palette.m3onSurface;x:root.contentX+(chip.visible?chip.width+8:0);width:Math.min(implicitWidth,240);anchors.verticalCenter:parent.verticalCenter
   elide:Text.ElideRight;maximumLineCount:1;text:Bridge.dictationText
   opacity:(root.st==="done"||root.st==="typing")?1:0;visible:opacity>0.01
   Behavior on opacity{NumberAnimation{duration:Appearance.anim.durations.small}}
  }
  StyledText{
   id:errLabel;x:root.contentX;width:Math.min(implicitWidth,300);anchors.verticalCenter:parent.verticalCenter
   elide:Text.ElideRight;maximumLineCount:1;text:Bridge.dictationError||qsTr("Dictée impossible");color:Colours.palette.m3error
   opacity:root.st==="error"?1:0;visible:opacity>0.01
   Behavior on opacity{NumberAnimation{duration:Appearance.anim.durations.small}}
  }
 }
 }
}
