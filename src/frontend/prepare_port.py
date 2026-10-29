from pathlib import Path
import shutil,re,json,hashlib
root=Path(__file__).resolve().parent;src=root/"upstream";q=root/"qml";records=[]
if (root/"qml"/"Main.qml").exists():
    raise SystemExit("Extraction initiale uniquement : les adaptations Windows existent déjà. Construire les sources présentes ; voir PORTAGE.md.")
def put(rel,text):
 p=q/rel;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(text,encoding="utf-8")
def tree(part,dest):
 for p in (src/part).rglob("*"):
  if p.is_file():
   t=q/dest/p.relative_to(src/part);t.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,t)
   records.append({"source":str(p.relative_to(src)).replace("\\","/"),"destination":str(t.relative_to(root)).replace("\\","/"),"sha256":hashlib.sha256(p.read_bytes()).hexdigest()})
tree("components","qs/components");tree("modules/dashboard","qs/modules/dashboard")
for name in ["AppearanceConfig.qml","DashboardConfig.qml"]:
 p=src/"config"/name;put("qs/config/"+name,p.read_text(encoding="utf-8"));records.append({"source":"config/"+name,"destination":"qml/qs/config/"+name,"sha256":hashlib.sha256(p.read_bytes()).hexdigest()})
put("qs/config/Appearance.qml",re.sub(r"readonly property AppearanceConfig\.\w+","readonly property var",(src/"config/Appearance.qml").read_text(encoding="utf-8")))
for name in ["bongocat.gif","dino.png","kurukuru.gif","logo.svg"]:
 p=root/"assets"/name;p.parent.mkdir(exist_ok=True);shutil.copy2(src/"assets"/name,p)
shutil.copy2(src/"LICENSE",root/"LICENSE")
(root/"UPSTREAM.json").write_text(json.dumps({"repository":"https://github.com/caelestia-dots/shell","tag":"v1.4.0","commit":"04bfc87f65d6e13b287358e1024d9bbc055b0c58","license":"GPL-3.0","files":records},indent=2),encoding="utf-8")
put("Quickshell/PersistentProperties.qml",'import QtQuick\nQtObject { property string reloadableId: "" }\n')
put("Quickshell/Singleton.qml",'import QtQuick\nQtObject { default property list<QtObject> data }\n')
put("Quickshell/Variants.qml",'''import QtQuick
import QtQml.Models
Instantiator {
 property var instances: []
 onObjectAdded: (index, object) => {let list=instances.slice();list.splice(index,0,object);instances=list}
 onObjectRemoved: (index, object) => {let list=instances.slice();list.splice(index,1);instances=list}
}
''')
put("Quickshell/Quickshell.qml",'''pragma Singleton
import QtQuick
QtObject {
 function iconPath(name) { return Bridge.iconPath(name) }
 function execDetached(args) { Bridge.action("launchKnown", {args:args}) }
}
''')
put("Quickshell/QsWindow.qml",'''pragma Singleton
import QtQuick
QtObject { readonly property var window: Bridge.window }
''')
put("Quickshell/Widgets/IconImage.qml",'''import QtQuick
Image { property real implicitSize: 24;implicitWidth:implicitSize;implicitHeight:implicitSize;fillMode:Image.PreserveAspectFit }
''')
put("Quickshell/Widgets/ClippingRectangle.qml",'''import QtQuick
import Qt5Compat.GraphicalEffects
Rectangle {
 id: root
 layer.enabled: true
 layer.effect: OpacityMask { maskSource: Rectangle {width:root.width;height:root.height;radius:root.radius} }
}
''')
put("Quickshell/Io/JsonObject.qml",'import QtQuick\nQtObject {default property list<QtObject> data}\n')
put("Caelestia/Services/ServiceRef.qml",'import QtQuick\nQtObject { required property var service; Component.onCompleted: { if(service)service.refCount++ } Component.onDestruction: { if(service)service.refCount-- } }\n')
put("qs/config/Config.qml",'''pragma Singleton
import QtQuick
QtObject {
 property AppearanceConfig appearance: AppearanceConfig {}
 property DashboardConfig dashboard: DashboardConfig {}
 property QtObject services: QtObject {property bool useFahrenheit:false;property bool useTwelveHourClock:false;property int visualiserBars:48}
 property QtObject paths: QtObject {property string mediaGif: Bridge.assetRoot + "/bongocat.gif"}
 property QtObject border: QtObject {property int thickness:0}
}
''')
put("qs/components/images/CachingImage.qml",'''import QtQuick
Image {property string path:""; source: Bridge.localUrl(path);asynchronous:true;fillMode:Image.PreserveAspectCrop}
''')
put("qs/components/effects/ColouredIcon.qml",'''import Quickshell.Widgets
import QtQuick
IconImage {required property color colour;asynchronous:true}
''')
put("qs/components/filedialog/FileDialog.qml",'''import QtQuick
QtObject {
 property string title:"";property string filterLabel:"";property var filters:[]
 signal accepted(string path)
 function open() { const result=Bridge.pickAvatar(); if(result)accepted(result) }
}
''')
put("qs/utils/Paths.qml",'''pragma Singleton
import QtQuick
QtObject {
 readonly property string home: Bridge.configRoot
 readonly property string imagecache: Bridge.configRoot+"/images"
 function absolutePath(path) {return Bridge.localUrl(path)}
 function shortenHome(path) {return path}
}
''')
put("qs/utils/Images.qml",'''pragma Singleton
import QtQuick
QtObject {readonly property var validImageExtensions:["*.png","*.jpg","*.jpeg","*.webp"]}
''')
put("qs/services/Time.qml",'''pragma Singleton
import QtQuick
QtObject {
 property date date: new Date()
 property Timer ticker: Timer {interval:1000;running:true;repeat:true;onTriggered: Time.date = new Date()}
 function format(pattern) {return Qt.formatDateTime(date,pattern)}
}
''')
put("qs/services/SysInfo.qml",'''pragma Singleton
import QtQuick
QtObject {
 readonly property string osPrettyName: Bridge.sysInfo.osName || "Windows"
 readonly property string osName: "Windows"
 readonly property string osLogo: Bridge.assetRoot + "/windows.svg"
 readonly property string wm: "Panneaux"
 readonly property string uptime: Bridge.sysInfo.uptime || "â€”"
}
''')
put("qs/services/SystemUsage.qml",'''pragma Singleton
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
''')
put("qs/services/Players.qml",'''pragma Singleton
import QtQuick
QtObject {
 readonly property var active: Bridge.player.available ? Bridge.player : null
 readonly property var list: Bridge.player.available ? [Bridge.player] : []
 property var manualActive: null
 function getIdentity(player) {return player.identity}
}
''')
put("qs/services/Audio.qml",'''pragma Singleton
import QtQuick
QtObject {
 property QtObject cava: QtObject {property int refCount:0;readonly property var values:Bridge.spectrum}
 property QtObject beatTracker: QtObject {property int refCount:0;property real bpm:100}
}
''')
put("qs/services/Weather.qml",'''pragma Singleton
import QtQuick
QtObject {
 readonly property string icon: "sunny"
 readonly property string temp: Bridge.weather.temperature || "â€”Â°C"
 readonly property string description: Bridge.weather.description || "Choisir une ville"
 function reload() {Bridge.refreshWeather()}
}
''')
palette={
"m3background":("#fff8f6","#101518"),"m3surface":("#fff8f6","#101518"),"m3onSurface":("#261916","#e5ebef"),
"m3surfaceContainer":("#fae8e3","#182025"),"m3surfaceContainerLow":("#fff0eb","#151c21"),"m3surfaceContainerHigh":("#f5e0d9","#242d33"),
"m3surfaceContainerHighest":("#eed9d2","#303b42"),"m3primary":("#895143","#afd4f0"),"m3secondary":("#775b52","#c3c8d0"),
"m3tertiary":("#68613c","#c7cea0"),"m3primaryContainer":("#ffdace","#304b60"),"m3secondaryContainer":("#f5dcd3","#404851"),
"m3onPrimary":("#ffffff","#12354a"),"m3onSecondary":("#ffffff","#253039"),"m3onPrimaryContainer":("#351006","#d1e7fa"),
"m3onSecondaryContainer":("#2b1811","#e3e6ed"),"m3onSurfaceVariant":("#54433e","#bdc7cf"),"m3outline":("#85726a","#8897a2"),
"m3outlineVariant":("#d6c3bc","#3c4a54"),"m3error":("#b3261e","#ffb4ab"),"m3onError":("#ffffff","#690005"),
"m3shadow":("#000000","#000000"),"m3scrim":("#000000","#000000"),"m3inverseSurface":("#352f2c","#e0e3e6"),
"m3inverseOnSurface":("#fff0eb","#2e3134"),"m3inversePrimary":("#ffb5a0","#40657f")}
used=set()
for p in (q/"qs/components").rglob("*.qml"):used.update(re.findall(r"(?:palette|tPalette)\.(m3\w+)",p.read_text(encoding="utf-8")))
for key in used:palette.setdefault(key,("#f5ded5","#263640"))
props="\n".join(f'property color {name}: Bridge.dark ? "{colors[1]}" : "{colors[0]}"' for name,colors in palette.items())
put("qs/services/Colours.qml",'''pragma Singleton
import QtQuick
QtObject {
 readonly property bool light: !Bridge.dark
 property QtObject palette: QtObject {'''+props+'''}
 readonly property var tPalette: palette
 function layer(color,level) {return Qt.tint(color,Qt.alpha(palette.m3primary,Math.min(.18,level*.025)))}
 function onColour(color) {return palette.m3onSurface}
}
''')
p=q/"qs/modules/dashboard/Content.qml";t=p.read_text(encoding="utf-8").replace('sourceComponent: Weather {}','sourceComponent: Workspaces {}');p.write_text(t,encoding="utf-8")
p=q/"qs/modules/dashboard/Tabs.qml";t=p.read_text(encoding="utf-8").replace('iconName: "cloud"','iconName: "workspaces"').replace('qsTr("Weather")','qsTr("Workspaces")');p.write_text(t,encoding="utf-8")
p=q/"qs/modules/dashboard/Performance.qml";t=p.read_text(encoding="utf-8").replace('function displayTemp(temp: real): string {','function displayTemp(temp: real): string {\n        if (!Number.isFinite(temp)) return "â€”";',1).replace('value1: Math.min(1, SystemUsage.cpuTemp / 90)','value1: Number.isFinite(SystemUsage.cpuTemp) ? Math.min(1, SystemUsage.cpuTemp / 90) : 0',1).replace('label1: root.displayTemp(SystemUsage.cpuTemp)','label1: Number.isFinite(SystemUsage.cpuTemp) ? root.displayTemp(SystemUsage.cpuTemp) : qsTr("N/D")',1);p.write_text(t,encoding="utf-8")
p=q/"qs/modules/dashboard/Media.qml";t=p.read_text(encoding="utf-8").replace('required property MprisPlayer modelData','required property var modelData');p.write_text(t,encoding="utf-8")
def qmldirs():
 for folder in [q]+[p for p in q.rglob("*") if p.is_dir()]:
  files=list(folder.glob("*.qml"))
  if not files:continue
  module=".".join(folder.relative_to(q).parts);lines=(["module "+module] if module else [])
  for p in files:
   singleton="pragma Singleton" in p.read_text(encoding="utf-8")
   lines.append(("singleton " if singleton else "")+p.stem+" "+p.name)
  (folder/"qmldir").write_text("\n".join(lines)+"\n",encoding="utf-8")
qmldirs()
print("Copied upstream files:",len(records))
