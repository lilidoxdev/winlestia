// Masque Border.qml de Caelestia, avec un hôte Windows natif laissant passer les clics.
pragma ComponentBehavior: Bound

import QtQuick
import QtQuick.Shapes
import qs.config

Item {
    id: root

    required property Item bar
    property color tint: "transparent"

    readonly property real edge: Config.border.thickness
    readonly property real rounding: Config.border.rounding
    readonly property real innerLeft: root.bar.implicitWidth
    readonly property real innerTop: root.edge
    readonly property real innerRight: root.width - root.edge
    readonly property real innerBottom: root.height - root.edge

    anchors.fill: parent

    // L'anneau est dessiné comme une seule forme even-odd : pas de couche ni de texture de masque, donc
    // l'alpha de remplissage arrive inchangé à l'écran et correspond exactement au tableau de bord.
    Shape {
        anchors.fill: parent
        preferredRendererType: Shape.CurveRenderer

        ShapePath {
            fillColor: root.tint
            strokeWidth: -1
            fillRule: ShapePath.OddEvenFill

            startX: 0
            startY: 0
            PathLine {
                x: root.width
                y: 0
            }
            PathLine {
                x: root.width
                y: root.height
            }
            PathLine {
                x: 0
                y: root.height
            }
            PathLine {
                x: 0
                y: 0
            }

            PathMove {
                x: root.innerLeft + root.rounding
                y: root.innerTop
            }
            PathLine {
                x: root.innerRight - root.rounding
                y: root.innerTop
            }
            PathArc {
                x: root.innerRight
                y: root.innerTop + root.rounding
                radiusX: root.rounding
                radiusY: root.rounding
                direction: PathArc.Clockwise
            }
            PathLine {
                x: root.innerRight
                y: root.innerBottom - root.rounding
            }
            PathArc {
                x: root.innerRight - root.rounding
                y: root.innerBottom
                radiusX: root.rounding
                radiusY: root.rounding
                direction: PathArc.Clockwise
            }
            PathLine {
                x: root.innerLeft + root.rounding
                y: root.innerBottom
            }
            PathArc {
                x: root.innerLeft
                y: root.innerBottom - root.rounding
                radiusX: root.rounding
                radiusY: root.rounding
                direction: PathArc.Clockwise
            }
            PathLine {
                x: root.innerLeft
                y: root.innerTop + root.rounding
            }
            PathArc {
                x: root.innerLeft + root.rounding
                y: root.innerTop
                radiusX: root.rounding
                radiusY: root.rounding
                direction: PathArc.Clockwise
            }
        }
    }
}
