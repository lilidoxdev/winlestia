pragma Singleton
import QtQuick
QtObject {
 property int refCount:0
 readonly property real gpuTemp: Bridge.performance.gpuTemp ?? NaN
 readonly property real gpuPerc: (Bridge.performance.gpu ?? 0)/100
 readonly property real cpuTemp: Bridge.performance.cpuTemp ?? NaN
 readonly property string cpuTempSource: Bridge.performance.cpuTempSource ?? ""
 readonly property real cpuPerc: (Bridge.performance.cpu ?? 0)/100
 readonly property real memPerc: Bridge.performance.memPercent ?? 0
 readonly property real memUsed: Bridge.performance.memUsedKiB ?? 0
 readonly property real storagePerc: Bridge.performance.diskPercent ?? 0
 readonly property real storageUsed: Bridge.performance.diskUsedKiB ?? 0
 function formatKib(value) { let unit="KiB";if(value>=1048576){value/=1048576;unit="GiB"}else if(value>=1024){value/=1024;unit="MiB"}return {value:value,unit:unit} }
}
