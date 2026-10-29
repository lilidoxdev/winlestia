// Winlestia custom theme studio: five semantic colours, HSV wheel and screen eyedropper.
import QtQuick
import QtQuick.Window
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.config
import qs.services
Window{
 id:studio;objectName:"customThemeStudio";title:"Winlestia · Palette";transientParent:null;color:"transparent"
 flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.NoDropShadowWindowHint
 width:Math.min(600,Bridge.screenWidth-32);height:Math.min(690,Bridge.screenHeight-40);x:Bridge.screenX+(Bridge.screenWidth-width)/2;y:Bridge.screenY+(Bridge.screenHeight-height)/2
 property var roles:[{key:"background",label:qsTr("Fond")},{key:"text",label:qsTr("Texte")},{key:"primary",label:qsTr("Principal")},{key:"secondary",label:qsTr("Secondaire")},{key:"tertiary",label:qsTr("Troisième accent")}]
 property var draft:({background:"#fff8f6",text:"#261916",primary:"#895143",secondary:"#775b52",tertiary:"#68613c"})
 property int selected:0;property real hue:0;property real saturation:0;property real brightness:1;property bool glass:false;property real transparency:88
 function syncHSV(){const value=Bridge.colorHSV(draft[roles[selected].key]);hue=value[0];saturation=value[1];brightness=value[2]}
 function openEditor(){draft=Object.assign({},draft,Bridge.settings.customPalette??{});glass=Bridge.settings.customGlass??false;transparency=(1-(Bridge.settings.customGlassOpacity??.12))*100;syncHSV();Bridge.editTheme(true);visible=true;requestActivate()}
 function finish(){visible=false;Bridge.editTheme(false)}
 function putColor(value,sync){if(!Bridge.validHex(value))return;const copy=Object.assign({},draft);copy[roles[selected].key]=value.toLowerCase();draft=copy;if(sync)syncHSV()}
 function wheelColor(h,s,v){hue=h;saturation=s;brightness=v;putColor(Bridge.hsvColor(h,s,v),false)}
 onSelectedChanged:syncHSV()
 onClosing:event=>{event.accepted=false;finish()}
 Shortcut{sequence:"Escape";enabled:studio.visible;onActivated:studio.finish()}
 Connections{target:Bridge;function onColorPicked(value){if(value)studio.putColor(value,true);studio.visible=true;studio.requestActivate()}}
 Rectangle{anchors.fill:parent;radius:Appearance.rounding.large;color:Colours.surfaceBase;border.width:1;border.color:Qt.alpha(Colours.palette.m3primary,.4)
  ColumnLayout{anchors.fill:parent;anchors.margins:24;spacing:14
   RowLayout{Layout.fillWidth:true;ColumnLayout{spacing:3;StyledText{text:qsTr("Ma palette");font.pointSize:Appearance.font.size.title}StyledText{text:qsTr("Cinq couleurs, tout ton espace");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}}Item{Layout.fillWidth:true}IconButton{icon:"close";type:IconButton.Text;onClicked:studio.finish()}}
   RowLayout{Layout.fillWidth:true;spacing:7
    Repeater{model:studio.roles;StyledRect{id:role;required property var modelData;required property int index;objectName:"custom-role-"+modelData.key;Layout.fillWidth:true;implicitHeight:82;radius:Appearance.rounding.normal;color:Colours.palette.m3surfaceContainer;border.width:studio.selected===index?2:0;border.color:Colours.palette.m3primary
     Rectangle{anchors.horizontalCenter:parent.horizontalCenter;y:12;width:28;height:28;radius:Appearance.rounding.normal;color:studio.draft[role.modelData.key];border.width:1;border.color:Colours.palette.m3outlineVariant}
     StyledText{anchors.horizontalCenter:parent.horizontalCenter;y:52;text:role.modelData.label;font.pointSize:Appearance.font.size.small}
     MouseArea{anchors.fill:parent;onClicked:studio.selected=role.index}
    }}
   }
   RowLayout{Layout.fillWidth:true;Layout.fillHeight:true;spacing:18
    Item{Layout.preferredWidth:210;Layout.preferredHeight:210
     Canvas{id:wheel;objectName:"customColorWheel";anchors.fill:parent
      property real value:studio.brightness
      onValueChanged:requestPaint()
      onPaint:{const c=getContext("2d"),cx=width/2,cy=height/2,r=Math.min(width,height)/2-7;c.clearRect(0,0,width,height);for(let i=0;i<180;i++){let a=i*Math.PI*2/180,b=(i+1)*Math.PI*2/180+.005;let g=c.createRadialGradient(cx,cy,0,cx,cy,r);g.addColorStop(0,Qt.hsva(i/180,0,studio.brightness,1));g.addColorStop(1,Qt.hsva(i/180,1,studio.brightness,1));c.fillStyle=g;c.beginPath();c.moveTo(cx,cy);c.arc(cx,cy,r,a,b);c.closePath();c.fill()}}
      MouseArea{anchors.fill:parent;function choose(mouse){const dx=mouse.x-width/2,dy=mouse.y-height/2,r=Math.min(width,height)/2-7;studio.wheelColor((Math.atan2(dy,dx)/(2*Math.PI)+1)%1,Math.min(1,Math.sqrt(dx*dx+dy*dy)/r),studio.brightness)}onPressed:mouse=>choose(mouse);onPositionChanged:mouse=>{if(pressed)choose(mouse)}}
     }
     Rectangle{x:parent.width/2+Math.cos(studio.hue*Math.PI*2)*studio.saturation*(parent.width/2-7)-7;y:parent.height/2+Math.sin(studio.hue*Math.PI*2)*studio.saturation*(parent.height/2-7)-7;width:14;height:14;radius:Appearance.rounding.smaller;color:"transparent";border.width:3;border.color:"white";Rectangle{anchors.fill:parent;anchors.margins:3;radius:Appearance.rounding.tiny;color:"transparent";border.width:1;border.color:"#303030"}}
    }
    ColumnLayout{Layout.fillWidth:true;spacing:10
     RowLayout{Layout.fillWidth:true;StyledText{text:studio.roles[studio.selected].label;Layout.fillWidth:true}IconButton{objectName:"customEyedropper";icon:"colorize";type:IconButton.Text;onClicked:{studio.visible=false;Bridge.startColorPick()}}}
     StyledTextField{id:hex;objectName:"customHex";Layout.fillWidth:true;maximumLength:7;placeholderText:"#RRGGBB";onEditingFinished:{if(Bridge.validHex(text))studio.putColor(text,true);else text=studio.draft[studio.roles[studio.selected].key]}Binding{target:hex;property:"text";value:studio.draft[studio.roles[studio.selected].key];when:!hex.activeFocus}}
     StyledRect{Layout.fillWidth:true;implicitHeight:92;radius:Appearance.rounding.normal;color:Qt.alpha(studio.draft.background,studio.glass?1-studio.transparency/100:1);border.width:1;border.color:studio.draft.primary
      Column{anchors.fill:parent;anchors.margins:12;spacing:6;StyledText{text:qsTr("Ton espace");color:studio.draft.text;font.pointSize:Appearance.font.size.larger}Row{spacing:8;Repeater{model:[studio.draft.primary,studio.draft.secondary,studio.draft.tertiary];Rectangle{required property color modelData;width:24;height:12;radius:Appearance.rounding.smaller;color:modelData}}}}
     }
     RowLayout{Layout.fillWidth:true;StyledText{text:qsTr("Luminosité");font.pointSize:Appearance.font.size.small;Layout.fillWidth:true}StyledText{text:Math.round(valueSlider.value*100)+" %";font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}}
     StyledSlider{id:valueSlider;Layout.fillWidth:true;implicitHeight:22;from:0;to:1;value:studio.brightness;onMoved:studio.wheelColor(studio.hue,studio.saturation,value)}
    }
   }
   StyledRect{Layout.fillWidth:true;implicitHeight:studio.glass?102:54;radius:Appearance.rounding.normal;color:Colours.palette.m3surfaceContainer
    ColumnLayout{anchors.fill:parent;anchors.margins:12;spacing:8
     RowLayout{Layout.fillWidth:true;MaterialIcon{text:"blur_on";color:Colours.palette.m3primary}StyledText{text:qsTr("Verre transparent");Layout.fillWidth:true}StyledSwitch{objectName:"customGlassSwitch";checked:studio.glass;onClicked:studio.glass=checked}}
     RowLayout{visible:studio.glass;Layout.fillWidth:true;StyledSlider{id:transparencySlider;objectName:"customTransparency";Layout.fillWidth:true;implicitHeight:24;from:5;to:98;value:studio.transparency;onMoved:studio.transparency=value}StyledText{text:Math.round(studio.transparency)+" %";font.pointSize:Appearance.font.size.small;color:Colours.palette.m3primary}}
    }
   }
   RowLayout{Layout.fillWidth:true;IconTextButton{text:qsTr("Annuler");type:IconTextButton.Text;onClicked:studio.finish()}Item{Layout.fillWidth:true}IconTextButton{objectName:"applyCustomTheme";text:qsTr("Appliquer ma palette");icon:"check";onClicked:{Bridge.action("customTheme",{colors:studio.draft,glass:studio.glass,opacity:1-studio.transparency/100});studio.finish()}}}
  }
 }
 Window{title:"Winlestia · Pipette";transientParent:null;visible:Bridge.picking;flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.WindowDoesNotAcceptFocus|Qt.WindowTransparentForInput;color:"transparent";width:180;height:72;x:Bridge.pickX;y:Bridge.pickY
  Rectangle{anchors.fill:parent;radius:Appearance.rounding.normal;color:Colours.surfaceBase;border.width:1;border.color:Colours.palette.m3primary;Row{anchors.centerIn:parent;spacing:10;Rectangle{width:28;height:28;radius:Appearance.rounding.smaller;color:Bridge.pickColor}Column{spacing:4;StyledText{text:Bridge.pickColor;font.pointSize:Appearance.font.size.normal}StyledText{text:qsTr("Échap · annuler");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}}}}
 }
}
