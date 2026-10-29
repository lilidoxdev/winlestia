// Generic hidden edge rail (Windows window adapter, same recipe as Dock.qml): `side` is "left" or "right".
// Fixed-size window; only the bump silhouette (bumpWidth/bumpHeight, read by surface_geometry) and the content animate.
// Instances provide contentW/contentHeight (+ optional chevronW/panelW for a drawer) and place their content with edgeX/slide.
import QtQuick
import QtQuick.Window
import qs.components
import qs.config
import qs.services

Window {
    id:rail
    // A transient Window is deferred while its owning dashboard is hidden on Windows.
    transientParent:null
    color:"transparent"
    flags:(Bridge.preview?Qt.Window:Qt.Tool)|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint|Qt.NoDropShadowWindowHint|Qt.WindowDoesNotAcceptFocus
    property string side:"right"
    readonly property bool isLeft:side==="left"
    property real contentW:54
    property real contentHeight:0
    property real chevronW:0
    property real panelW:0
    property bool shown:Bridge.preview
    property bool expanded:false
    property real reveal:expanded?1:0
    Behavior on reveal {enabled:!Appearance.reducedEffects;Anim {duration:Appearance.anim.durations.expressiveDefaultSpatial;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
    readonly property real railW:contentW+2*Appearance.padding.large+chevronW
    readonly property real fillet:26
    readonly property real baseHeight:Math.min(Math.ceil(contentHeight)+2*(fillet+Appearance.padding.large),Math.max(200,Bridge.screenHeight-80))
    readonly property real fullHeight:panelW>0?Math.max(baseHeight,Math.min(Bridge.screenHeight-160,520)):baseHeight
    readonly property real bumpWidth:railW+panelW*reveal
    readonly property real bumpHeight:baseHeight+(fullHeight-baseHeight)*reveal
    property real offset:shown?0:railW
    // Bump width last sent to the native backdrop (-1 without native glass); content follows the narrower of the two so it never sticks out of either.
    property real nativeBump:-1
    readonly property real contentBump:nativeBump<0?bumpWidth:Math.max(railW,Math.min(nativeBump,bumpWidth))
    // Inner edge of the displayed bump in window coordinates, and the signed slide of the content while hidden.
    readonly property real edgeX:isLeft?contentBump:width-contentBump
    readonly property real slide:isLeft?-offset:offset
    onOffsetChanged:Bridge.updateSurfaces()
    onBumpWidthChanged:Bridge.updateSurfaces()
    // The screen frame is another topmost window; raise the rail above it each time it slides out.
    onShownChanged:{if(!shown)expanded=false;if(shown&&!Bridge.preview)Qt.callLater(rail.raise)}
    onVisibleChanged:if(visible&&!Bridge.preview)Qt.callLater(rail.raise)
    readonly property real inset:Bridge.screenFullscreen?0:Math.max(0,Config.border.thickness)
    // The silhouette (surface_geometry.rail_path) is drawn by the shared screen surface: no own background here.
    width:railW+panelW
    height:fullHeight
    x:isLeft?Bridge.screenX+inset:Bridge.screenX+Bridge.screenWidth-width-inset
    y:Bridge.screenY+(Bridge.screenHeight-height)/2
    visible:shown||offset<bumpWidth-1
    Behavior on offset {Anim {duration:Appearance.anim.durations.expressiveFastSpatial;easing.bezierCurve:Appearance.anim.curves.emphasizedDecel}}
}
