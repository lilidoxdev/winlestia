// Same silhouette as the native backdrop; the material matches the frame and dashboard.
import QtQuick
import QtQuick.Shapes
import qs.services
import qs.config
ShapePath {
    id:root
    required property Item wrapper
    readonly property real r:Config.border.rounding
    readonly property real w:wrapper.width+2*r
    readonly property real h:wrapper.height
    strokeWidth:-1;fillColor:Colours.surfaceColor
    PathQuad{x:root.r;y:root.h-root.r;controlX:root.r;controlY:root.h}
    PathLine{x:root.r;y:root.r}
    PathQuad{x:2*root.r;y:0;controlX:root.r;controlY:0}
    PathLine{x:root.w-2*root.r;y:0}
    PathQuad{x:root.w-root.r;y:root.r;controlX:root.w-root.r;controlY:0}
    PathLine{x:root.w-root.r;y:root.h-root.r}
    PathQuad{x:root.w;y:root.h;controlX:root.w-root.r;controlY:root.h}
    PathLine{x:0;y:root.h}
}
