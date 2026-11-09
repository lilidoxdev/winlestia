pragma Singleton
import QtQuick

QtObject {
    readonly property string osPrettyName: Bridge.sysInfo.osName || "Windows"
    readonly property string osName: "Windows"
    readonly property string osLogo: Bridge.localUrl(Bridge.assetRoot + "/windows.svg")
    readonly property string wm: "Winlestia"
    readonly property string userName: Bridge.sysInfo.userName || ""
    readonly property string uptime: Bridge.sysInfo.uptime || "—"
}
