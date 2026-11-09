import QtQuick
import QtQuick.Layouts
import ".."
import "../blocks"

// Conteneur `grid` : `columns` colonnes de largeur égale, gap et padding comme les autres conteneurs.
Item {
    id: box

    property var spec: null
    property var ctx: ({})
    property var host: null

    readonly property real pad: spec && spec.padding >= 0 ? Number(spec.padding) : 0
    readonly property int cols: spec && spec.columns >= 1 ? Math.min(12, Math.floor(spec.columns)) : 2

    implicitWidth: lay.implicitWidth + pad * 2
    implicitHeight: lay.implicitHeight + pad * 2

    GridLayout {
        id: lay

        anchors.fill: parent
        anchors.margins: box.pad
        columns: box.cols
        columnSpacing: box.spec && box.spec.gap >= 0 ? Number(box.spec.gap) : 8
        rowSpacing: columnSpacing

        Repeater {
            model: box.spec && box.spec.children ? box.spec.children : []

            BlockNode {
                required property var modelData

                spec: modelData
                ctx: box.ctx
                host: box.host
                parentKind: "grid"
                parentAlign: box.spec && box.spec.align ? box.spec.align : ""
                Layout.preferredWidth: 1
            }
        }
    }

    ActionArea {
        spec: box.spec
        host: box.host
        z: -1
    }
}
