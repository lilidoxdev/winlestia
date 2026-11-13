import QtQuick
import ".."
import "../blocks"

// Conteneur `stack` : les enfants sont superposés sur toute la surface (le dernier est dessus).
Item {
    id: box

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property real pad: spec && spec.padding >= 0 ? Number(spec.padding) : 0
    readonly property real padPx: pad * sc
    property real childWidth: 0
    property real childHeight: 0

    implicitWidth: childWidth + padPx * 2
    implicitHeight: childHeight + padPx * 2

    Item {
        id: area

        anchors.fill: parent
        anchors.margins: box.padPx

        Repeater {
            model: box.spec && box.spec.children ? box.spec.children : []

            BlockNode {
                required property var modelData

                anchors.fill: parent
                spec: modelData
                ctx: box.ctx
                host: box.host
                parentKind: "stack"
                parentAlign: box.spec && box.spec.align ? box.spec.align : ""
                onLoaded: {
                    box.childWidth = Math.max(box.childWidth, item.implicitWidth);
                    box.childHeight = Math.max(box.childHeight, item.implicitHeight);
                }
            }
        }
    }

    ActionArea {
        spec: box.spec
        host: box.host
        z: -1
    }
}
