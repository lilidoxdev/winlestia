import qs.components
import qs.components.misc
import qs.services
import qs.config
import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes

RowLayout {
    id: root

    readonly property int padding: Appearance.padding.large

    function displayTemp(temp: real): string {
        if (!Number.isFinite(temp))
            return "—";
        return `${Math.ceil(Config.services.useFahrenheit ? temp * 1.8 + 32 : temp)}°${Config.services.useFahrenheit ? "F" : "C"}`;
    }

    spacing: Appearance.spacing.large * 3

    property bool firstDataMarked: false
    Connections {
        target: SystemUsage
        ignoreUnknownSignals: true

        function onCpuPercChanged(): void {
            if (!root.firstDataMarked && Bridge.panelProfiling === true) {
                root.firstDataMarked = true;
                Bridge.panelMark("performance:first-data");
            }
        }
    }

    Ref {
        service: SystemUsage
    }

    Resource {
        Layout.alignment: Qt.AlignVCenter
        Layout.topMargin: root.padding
        Layout.bottomMargin: root.padding
        Layout.leftMargin: root.padding * 2

        visible: Number.isFinite(SystemUsage.gpuTemp) || Bridge.performance.gpu != null
        hasValue1: Number.isFinite(SystemUsage.gpuTemp)
        value1: Number.isFinite(SystemUsage.gpuTemp) ? Math.min(1, SystemUsage.gpuTemp / 90) : 0
        value2: SystemUsage.gpuPerc

        label1: Number.isFinite(SystemUsage.gpuTemp) ? root.displayTemp(SystemUsage.gpuTemp) : qsTr("N/D")
        label2: `${Math.round(SystemUsage.gpuPerc * 100)}%`

        sublabel1: qsTr("GPU temp")
        sublabel2: qsTr("Usage")
    }

    Resource {
        Layout.alignment: Qt.AlignVCenter
        Layout.topMargin: root.padding
        Layout.bottomMargin: root.padding

        primary: true

        hasValue1: Number.isFinite(SystemUsage.cpuTemp)
        value1: Number.isFinite(SystemUsage.cpuTemp) ? Math.min(1, SystemUsage.cpuTemp / 90) : 0
        value2: SystemUsage.cpuPerc

        label1: Number.isFinite(SystemUsage.cpuTemp) ? root.displayTemp(SystemUsage.cpuTemp) : qsTr("N/D")
        label2: `${Math.round(SystemUsage.cpuPerc * 100)}%`

        sublabel1: qsTr("CPU temp")
        sublabel2: qsTr("Usage")
    }

    Resource {
        Layout.alignment: Qt.AlignVCenter
        Layout.topMargin: root.padding
        Layout.bottomMargin: root.padding
        Layout.rightMargin: root.padding * 3

        value1: SystemUsage.memPerc
        value2: SystemUsage.storagePerc

        label1: {
            return SystemUsage.fmtKib(SystemUsage.memUsed, 1);
        }
        label2: {
            return SystemUsage.fmtKib(SystemUsage.storageUsed, 0);
        }

        sublabel1: qsTr("Memory")
        sublabel2: qsTr("Storage")
    }

    component Resource: Item {
        id: res

        required property real value1
        required property real value2
        required property string sublabel1
        required property string sublabel2
        required property string label1
        required property string label2

        property bool primary
        property bool hasValue1: true
        readonly property real primaryMult: primary ? 1.2 : 1

        readonly property real thickness: Config.dashboard.sizes.resourceProgessThickness * primaryMult

        property color fg1: Colours.palette.m3primary
        property color fg2: Colours.palette.m3secondary
        property color bg1: Colours.palette.m3primaryContainer
        property color bg2: Colours.palette.m3secondaryContainer

        implicitWidth: Config.dashboard.sizes.resourceSize * primaryMult
        implicitHeight: Config.dashboard.sizes.resourceSize * primaryMult

        Column {
            anchors.centerIn: parent

            StyledText {
                anchors.horizontalCenter: parent.horizontalCenter

                text: res.label1
                font.pointSize: Appearance.font.size.extraLarge * res.primaryMult
            }

            StyledText {
                anchors.horizontalCenter: parent.horizontalCenter

                text: res.sublabel1
                color: Colours.palette.m3onSurfaceVariant
                font.pointSize: Appearance.font.size.smaller * res.primaryMult
            }
        }

        Column {
            anchors.horizontalCenter: parent.right
            anchors.top: parent.verticalCenter
            anchors.horizontalCenterOffset: -res.thickness / 2
            anchors.topMargin: res.thickness / 2 + Appearance.spacing.small

            StyledText {
                anchors.horizontalCenter: parent.horizontalCenter

                text: res.label2
                font.pointSize: Appearance.font.size.smaller * res.primaryMult
            }

            StyledText {
                anchors.horizontalCenter: parent.horizontalCenter

                text: res.sublabel2
                color: Colours.palette.m3onSurfaceVariant
                font.pointSize: Appearance.font.size.small * res.primaryMult
            }
        }

        // Arcs GPU (Shape + PathAngleArc) : aucun raster ni repeint à chaque image des Behavior.
        Shape {
            id: gauge

            readonly property real radius: (Math.min(width, height) - res.thickness) / 2
            readonly property real arcStroke: res.thickness
            readonly property int capStyle: Appearance.rounding.scale === 0 ? ShapePath.SquareCap : ShapePath.RoundCap

            anchors.fill: parent
            preferredRendererType: Shape.CurveRenderer

            ShapePath {
                strokeWidth: res.hasValue1 ? gauge.arcStroke : -1
                strokeColor: res.bg1
                fillColor: "transparent"
                capStyle: gauge.capStyle

                PathAngleArc {
                    centerX: gauge.width / 2
                    centerY: gauge.height / 2
                    radiusX: gauge.radius
                    radiusY: gauge.radius
                    startAngle: 45
                    sweepAngle: 175
                }
            }

            ShapePath {
                strokeWidth: res.hasValue1 ? gauge.arcStroke : -1
                strokeColor: res.fg1
                fillColor: "transparent"
                capStyle: gauge.capStyle

                PathAngleArc {
                    centerX: gauge.width / 2
                    centerY: gauge.height / 2
                    radiusX: gauge.radius
                    radiusY: gauge.radius
                    startAngle: 45
                    sweepAngle: Math.max(0.01, 175 * res.value1)
                }
            }

            ShapePath {
                strokeWidth: gauge.arcStroke
                strokeColor: res.bg2
                fillColor: "transparent"
                capStyle: gauge.capStyle

                PathAngleArc {
                    centerX: gauge.width / 2
                    centerY: gauge.height / 2
                    radiusX: gauge.radius
                    radiusY: gauge.radius
                    startAngle: 230
                    sweepAngle: 130
                }
            }

            ShapePath {
                strokeWidth: gauge.arcStroke
                strokeColor: res.fg2
                fillColor: "transparent"
                capStyle: gauge.capStyle

                PathAngleArc {
                    centerX: gauge.width / 2
                    centerY: gauge.height / 2
                    radiusX: gauge.radius
                    radiusY: gauge.radius
                    startAngle: 230
                    sweepAngle: Math.max(0.01, 130 * res.value2)
                }
            }
        }

        Behavior on value1 {
            Anim {}
        }

        Behavior on value2 {
            Anim {}
        }

        Behavior on fg1 {
            CAnim {}
        }

        Behavior on fg2 {
            CAnim {}
        }

        Behavior on bg1 {
            CAnim {}
        }

        Behavior on bg2 {
            CAnim {}
        }
    }
}
