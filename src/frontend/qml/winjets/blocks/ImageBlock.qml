import QtQuick
import qs.components

// Bloc `image` : `src` sous assets/ du paquet (refusé ailleurs), fit contain|cover, radius.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property string url: spec && host
        ? Bridge.winjets.assetUrl(host.packageId, Bridge.winjets.renderText(spec.src ?? "", ctx))
        : ""

    implicitWidth: 64 * sc
    implicitHeight: 64 * sc

    StyledClippingRect {
        anchors.fill: parent
        radius: block.spec && block.spec.radius >= 0 ? Number(block.spec.radius) : 0

        Image {
            anchors.fill: parent
            source: block.url
            asynchronous: true
            cache: true
            fillMode: block.spec && block.spec.fit === "cover" ? Image.PreserveAspectCrop : Image.PreserveAspectFit
            sourceSize.width: Math.max(1, Math.ceil(block.width * 2))
            sourceSize.height: Math.max(1, Math.ceil(block.height * 2))
        }
    }

    ActionArea {
        spec: block.spec
        host: block.host
    }
}
