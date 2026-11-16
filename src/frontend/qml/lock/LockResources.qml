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

    Row {
        anchors.fill: parent
        spacing: Appearance.spacing.normal

        LockGauge {
            implicitWidth: root.gaugeSize
            icon: "memory"
            value: SystemUsage.cpuPerc
            badge: root.hasTemp ? `${Math.round(SystemUsage.cpuTemp)}°` : ""
        }
        LockGauge {
            implicitWidth: root.gaugeSize
            icon: "memory_alt"
            value: SystemUsage.memPerc
            fillColour: Colours.palette.m3secondary
        }
        LockGauge {
            implicitWidth: root.gaugeSize
            icon: "hard_disk"
            value: SystemUsage.storagePerc
            fillColour: Colours.palette.m3tertiary
        }
    }
}
