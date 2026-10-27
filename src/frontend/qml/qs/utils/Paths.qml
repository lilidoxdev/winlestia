pragma Singleton
import QtQuick
QtObject {
 readonly property string home: Bridge.configRoot
 readonly property string imagecache: Bridge.configRoot+"/images"
 function absolutePath(path) {return Bridge.localUrl(path)}
 function shortenHome(path) {return path}
}
