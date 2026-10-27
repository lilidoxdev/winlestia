import QtQuick
import QtQuick.Window
Window{
 id:logo;property var modelData;property string screenName:modelData?.name??"";title:"Winlestia · Logo";transientParent:null;color:"transparent"
 flags:Qt.Tool|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.NoDropShadowWindowHint|Qt.WindowDoesNotAcceptFocus|Qt.WindowTransparentForInput
 x:(modelData?.x??0)+(Bridge.frameEnabled?Bridge.frameThickness:0)+4;y:(modelData?.y??0)+(Bridge.frameEnabled?Bridge.frameThickness:0)+4;width:116;height:116
 visible:(Bridge.settings.desktopLogo??false)&&!Bridge.picking&&!Bridge.capture.busy&&!Bridge.fullscreenScreens.includes(screenName)
 Image{anchors.fill:parent;source:Bridge.localUrl(Bridge.assetRoot+"/winlestia-mark.png");sourceSize.width:232;sourceSize.height:232;fillMode:Image.PreserveAspectFit;opacity:Bridge.glassEnabled?Math.max(.1,Math.min(.35,Bridge.glassTintOpacity)):.23}
}
