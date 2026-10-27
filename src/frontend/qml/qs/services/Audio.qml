pragma Singleton
import QtQuick
QtObject {
 property QtObject cava: QtObject {property int refCount:0;readonly property var values:Bridge.spectrum}
 property QtObject beatTracker: QtObject {property int refCount:0;property real bpm:100}
}
