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
 readonly property string lang: Bridge.language || "en"
 function fmtKib(value, decimals) {
  const units = lang === "fr" ? ["Ko", "Mo", "Go"] : ["KB", "MB", "GB"];
  let n = Number(value) || 0, u = 0;
  while (n >= 1024 && u < 2) { n /= 1024; u++ }
  n = decimals > 0 ? +n.toFixed(decimals) : Math.floor(n);
  return Qt.locale(lang).toString(n, "f", decimals > 0 && n % 1 !== 0 ? decimals : 0) + units[u];
 }
}
