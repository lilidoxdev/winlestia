import QtQuick
import qs.config
import QtQuick.Window
import qs.components
import qs.components.controls
import qs.services
import qs.config
Window{
 id:guide;transientParent:null;flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint;color:"transparent"
 visible:false;width:Math.min(540,Bridge.screenWidth-40);height:112;x:Bridge.screenX+(Bridge.screenWidth-width)/2;y:Bridge.screenY+Bridge.screenHeight/2
 property real margin:Bridge.settings.topActivationMargin??2
 onVisibleChanged:{if(visible){margin=Bridge.settings.topActivationMargin??2;Bridge.window.hide()}else if(Bridge.window){Bridge.window.show();Bridge.navigate("settings")}}
 Rectangle{anchors.fill:parent;radius:Appearance.rounding.normal;color:Qt.alpha(Colours.palette.m3surfaceContainerHigh,.96);border.width:1;border.color:Colours.palette.m3error
  StyledText{x:18;y:12;text:qsTr("Zone de detection en haut")+" · "+Math.round(guide.margin)+" px"}
  StyledSlider{x:18;y:48;width:parent.width-90;implicitHeight:28;from:0;to:160;value:guide.margin;onMoved:guide.margin=value}
  IconButton{anchors.right:parent.right;anchors.rightMargin:10;y:40;icon:"check";onClicked:{Bridge.action("preference",{key:"topActivationMargin",value:guide.margin});guide.visible=false}}
 }
 Window{transientParent:null;flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.WindowTransparentForInput|Qt.WindowDoesNotAcceptFocus;visible:guide.visible;color:Colours.palette.m3error;x:Bridge.screenX;y:Bridge.screenY+guide.margin;width:Bridge.screenWidth;height:3}
 Shortcut{sequence:"Escape";enabled:guide.visible;onActivated:guide.visible=false}
}
