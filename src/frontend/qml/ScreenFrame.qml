// One surface owns the union of frame, panel and dock silhouettes.
import QtQuick
import QtQuick.Window
import QtQuick.Shapes
import qs.services
Window {
    id:surface
    property var modelData
    property string screenName:modelData?.name??""
    property var drawing:Bridge.surfaceForScreen(screenName,Bridge.surfaceRevision)
    title:"Panneaux · Cadre "+screenName
    transientParent:null
    flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.NoDropShadowWindowHint|Qt.WindowDoesNotAcceptFocus|Qt.WindowTransparentForInput
    x:modelData?.x??0;y:modelData?.y??0;width:modelData?.width??1;height:modelData?.height??1
    color:"transparent";visible:drawing.visible??false
    Shape {
        anchors.fill:parent;preferredRendererType:Shape.CurveRenderer
        ShapePath {strokeWidth:-1;fillColor:Colours.surfaceColor;fillRule:ShapePath.WindingFill;PathSvg{path:surface.drawing.path??""}}
        ShapePath {
            strokeWidth:Bridge.glassEnabled?1:0;fillColor:"transparent";strokeColor:Qt.alpha(Colours.palette.m3onSurface,.23)
            PathSvg{path:surface.drawing.outline??""}
        }

    }
}
