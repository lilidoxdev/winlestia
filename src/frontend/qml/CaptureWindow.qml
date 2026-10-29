import QtQuick
import QtQuick.Window
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config

Window {
    id:root;objectName:"captureWindow";title:"Panneaux · Capture";transientParent:null
    flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.NoDropShadowWindowHint
    x:Bridge.screenX;y:Bridge.screenY;width:Bridge.screenWidth;height:Bridge.screenHeight
    visible:Bridge.capture.visible;color:"transparent"
    property rect selection:Qt.rect(0,0,0,0)
    property bool selected:false
    property string tool:"pen"
    property var strokes:[]
    property color ink:"#ab4938"
    property bool drawing:false
    property string loadedSource:""
    component CaptureAction:IconButton{
        id:action
        required property string hint
        property bool hovered:stateLayer.containsMouse
        Accessible.name:hint
        Tooltip{target:action;text:action.hint}
    }
    function finish(action){if(selected)Bridge.capture.export(action,selection,strokes)}
    onVisibleChanged:if(visible){if(loadedSource!==Bridge.capture.source){loadedSource=Bridge.capture.source;selected=false;strokes=[];selection=Qt.rect(0,0,0,0);tool="pen";canvas.requestPaint()}requestActivate()}
    Image{anchors.fill:parent;source:Bridge.capture.visible?Bridge.capture.source:"";fillMode:Image.Stretch;cache:false}
    Rectangle{x:0;y:0;width:root.width;height:root.selection.y;color:Qt.alpha(Colours.palette.m3scrim,.53)}
    Rectangle{x:0;y:root.selection.y;width:root.selection.x;height:root.selection.height;color:Qt.alpha(Colours.palette.m3scrim,.53)}
    Rectangle{x:root.selection.x+root.selection.width;y:root.selection.y;width:root.width-x;height:root.selection.height;color:Qt.alpha(Colours.palette.m3scrim,.53)}
    Rectangle{x:0;y:root.selection.y+root.selection.height;width:root.width;height:root.height-y;color:Qt.alpha(Colours.palette.m3scrim,.53)}
    Rectangle{x:root.selection.x;y:root.selection.y;width:root.selection.width;height:root.selection.height;color:"transparent";border.width:2;border.color:Colours.palette.m3primary}
    Canvas{
        id:canvas;anchors.fill:parent;renderStrategy:Canvas.Cooperative
        onPaint:{let ctx=getContext("2d");ctx.reset();ctx.save();ctx.beginPath();ctx.rect(root.selection.x,root.selection.y,root.selection.width,root.selection.height);ctx.clip();
            for(let s of root.strokes){ctx.strokeStyle=s.color;ctx.fillStyle=s.color;ctx.globalAlpha=s.tool==="highlight"?.3:1;ctx.lineWidth=s.width;ctx.lineCap="round";ctx.lineJoin="round";ctx.beginPath();for(let i=0;i<s.points.length;i++){let p=s.points[i];if(i===0)ctx.moveTo(p.x,p.y);else ctx.lineTo(p.x,p.y)}if(s.points.length===1){ctx.arc(s.points[0].x,s.points[0].y,s.width/2,0,Math.PI*2);ctx.fill()}else ctx.stroke()}ctx.restore()
        }
    }
    MouseArea{
        anchors.fill:parent;cursorShape:Qt.CrossCursor
        property point origin
        onPressed:mouse=>{origin=Qt.point(mouse.x,mouse.y);if(root.selected){if(mouse.x<root.selection.x||mouse.y<root.selection.y||mouse.x>root.selection.x+root.selection.width||mouse.y>root.selection.y+root.selection.height)return;root.drawing=true;root.strokes=root.strokes.concat([{tool:root.tool,color:root.ink.toString(),width:root.tool==="highlight"?22:4,points:[{x:mouse.x,y:mouse.y}]}]);canvas.requestPaint()}else root.selection=Qt.rect(mouse.x,mouse.y,0,0)}
        onPositionChanged:mouse=>{if(!pressed)return;if(!root.selected)root.selection=Qt.rect(Math.min(origin.x,mouse.x),Math.min(origin.y,mouse.y),Math.abs(mouse.x-origin.x),Math.abs(mouse.y-origin.y));else if(root.drawing){let points=root.strokes[root.strokes.length-1].points;if(points.length<12000)points.push({x:mouse.x,y:mouse.y});canvas.requestPaint()}}
        onReleased:{root.drawing=false;if(!root.selected&&root.selection.width>3&&root.selection.height>3){root.selected=true;if(Bridge.capture.textOnly)root.finish("ocr")}}
    }
    StyledRect{
        anchors.horizontalCenter:parent.horizontalCenter;anchors.top:parent.top;anchors.topMargin:20
        width:Math.min(toolbar.implicitWidth+32,parent.width-32);height:64;radius:Appearance.rounding.large;color:Colours.palette.m3surface
        RowLayout{id:toolbar;anchors.centerIn:parent;spacing:8
            StyledText{text:root.selected?Math.round(root.selection.width)+" × "+Math.round(root.selection.height):"Glisse pour sélectionner";font.pointSize:Appearance.font.size.small;Layout.leftMargin:8;Layout.rightMargin:8}
            CaptureAction{hint:"Nouvelle sélection";icon:"crop_free";type:IconButton.Text;onClicked:{root.selected=false;root.strokes=[];root.selection=Qt.rect(0,0,0,0);canvas.requestPaint()}}
            CaptureAction{hint:"Crayon";icon:"edit";toggle:true;checked:root.tool==="pen";type:IconButton.Tonal;onClicked:root.tool="pen"}
            CaptureAction{hint:"Surligneur";icon:"ink_highlighter";toggle:true;checked:root.tool==="highlight";type:IconButton.Tonal;onClicked:root.tool="highlight"}
            CaptureAction{hint:"Changer de couleur";icon:"palette";type:IconButton.Text;onClicked:root.ink=root.ink.toString()==="#ab4938"?"#32909f":root.ink.toString()==="#32909f"?"#f9d465":"#ab4938"}
            CaptureAction{hint:"Annuler le dernier trait · Ctrl Z";icon:"undo";type:IconButton.Text;disabled:root.strokes.length===0;onClicked:{root.strokes=root.strokes.slice(0,-1);canvas.requestPaint()}}
            CaptureAction{hint:"Extraire le texte";icon:"document_scanner";type:IconButton.Text;disabled:!root.selected;onClicked:root.finish("ocr")}
            CaptureAction{hint:"Épingler sur le bureau";icon:"keep";type:IconButton.Text;disabled:!root.selected;onClicked:root.finish("pin")}
            CaptureAction{hint:"Enregistrer en PNG · Ctrl S";icon:"save";type:IconButton.Text;disabled:!root.selected;onClicked:root.finish("save")}
            IconTextButton{icon:"content_copy";text:qsTr("Copier");enabled:root.selected;onClicked:root.finish("copy")}
            CaptureAction{hint:"Fermer · Échap";icon:"close";type:IconButton.Text;onClicked:Bridge.capture.cancel()}
        }
    }
    Shortcut{sequence:"Escape";enabled:root.visible;onActivated:Bridge.capture.cancel()}
    StyledRect{anchors.bottom:parent.bottom;anchors.bottomMargin:20;anchors.horizontalCenter:parent.horizontalCenter;width:Math.min(600,parent.width-40);height:errorLabel.implicitHeight+24;radius:Appearance.rounding.large;color:Colours.palette.m3surface;visible:Bridge.capture.errorText.length>0;StyledText{id:errorLabel;anchors.centerIn:parent;width:parent.width-28;wrapMode:Text.Wrap;text:Bridge.capture.errorText}}
    Shortcut{sequence:"Ctrl+C";enabled:root.visible&&root.selected;onActivated:root.finish("copy")}
    Shortcut{sequence:"Ctrl+S";enabled:root.visible&&root.selected;onActivated:root.finish("save")}
    Shortcut{sequence:"Ctrl+Z";enabled:root.visible;onActivated:{root.strokes=root.strokes.slice(0,-1);canvas.requestPaint()}}
    Shortcut{sequence:"Return";enabled:root.visible&&root.selected;onActivated:root.finish("copy")}
}
