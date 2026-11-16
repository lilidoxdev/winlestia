import QtQuick
import qs.components
import qs.services
import qs.config

// Trois jauges : processeur (avec température), mémoire, disque.
LockCard {
    id: root

    readonly property int gaugeCount: 3
    readonly property real gaugeSize: (width - inset * 2 - Appearance.spacing.normal * (gaugeCount - 1)) / gaugeCount
    readonly property bool hasTemp: Number.isFinite(SystemUsage.cpuTemp) && SystemUsage.cpuTemp > 0

    implicitHeight: gaugeSize + inset * 2

    // Le backend n'échantillonne le processeur que sur demande : une référence tant que l'écran de verrouillage existe.
    Component.onCompleted: Bridge.usageRef(true)
    Component.onDestruction: Bridge.usageRef(false)

    Row {
        anchors.fill: parent
        spacing: Appearance.spacing.normal

        LockGauge {
            implicitWidth: root.gaugeSize
            icon: "memory"
            value: SystemUsage.cpuPerc
            badge: root.hasTemp ? `${Math.round(SystemUsage.cpuTemp)}°C` : ""
            shapePreset: "pentagon"
        }
        LockGauge {
            implicitWidth: root.gaugeSize
            icon: "memory_alt"
            value: SystemUsage.memPerc
            shapePreset: "slanted"
            shapeColour: Colours.palette.m3onTertiary
            fillColour: Qt.alpha(Colours.palette.m3tertiary, 0.3)
            textColour: Colours.palette.m3tertiary
        }
        LockGauge {
            implicitWidth: root.gaugeSize
            icon: "hard_disk"
            value: SystemUsage.storagePerc
            shapePreset: "gem"
            shapeColour: Colours.palette.m3secondaryContainer
            fillColour: Qt.alpha(Colours.palette.m3secondary, 0.4)
            textColour: Colours.palette.m3secondary
        }
    }
}
