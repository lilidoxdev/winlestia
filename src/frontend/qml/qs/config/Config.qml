pragma Singleton
import QtQuick
QtObject {
 property LauncherConfig launcher: LauncherConfig {}
 property AppearanceConfig appearance: AppearanceConfig {}
 property DashboardConfig dashboard: DashboardConfig {}
 property QtObject services: QtObject {property bool useFahrenheit:false;property bool useTwelveHourClock:false;property int visualiserBars:48}
 property QtObject paths: QtObject {property string mediaGif: Bridge.assetRoot + "/bongocat.gif"}
 property QtObject border: QtObject {property int thickness:Bridge.frameEnabled?Bridge.frameThickness:0;property int rounding:26}
}
