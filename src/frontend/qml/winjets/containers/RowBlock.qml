import QtQuick
import QtQuick.Layouts
import ".."
import "../blocks"

// Conteneur `row` : gap, padding, align (alignement par défaut des enfants).
Item {
    id: box

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property real pad: spec && spec.padding >= 0 ? Number(spec.padding) : 0
    readonly property real padPx: pad * sc

    implicitWidth: lay.implicitWidth + padPx * 2
    implicitHeight: lay.implicitHeight + padPx * 2

    RowLayout {
        id: lay

        anchors.fill: parent
        anchors.margins: box.padPx
        spacing: box.spec && box.spec.gap >= 0 ? Number(box.spec.gap) * box.sc : 8 * box.sc

        Repeater {
            model: box.spec && box.spec.children ? box.spec.children : []

            BlockNode {
                required property var modelData

                spec: modelData
                ctx: box.ctx
                host: box.host
                parentKind: "row"
                parentAlign: box.spec && box.spec.align ? box.spec.align : ""
            }
        }
    }

    ActionArea {
        spec: box.spec
        host: box.host
        z: -1
    }
}
