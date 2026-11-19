import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes
import qs.components
import qs.components.controls
import qs.services
import qs.config

Item {
    id: ai
    property string appId
    property string appName
    property real size: 40
    implicitWidth: size
    implicitHeight: size
    Image {
        id: img
        anchors.fill: parent
        fillMode: Image.PreserveAspectFit
        source: ai.appId ? Bridge.iconUrl(ai.appId, Bridge.iconRevision) : ""
        asynchronous: true
        mipmap: true
        sourceSize: Qt.size(ai.size * 2, ai.size * 2)
        visible: status === Image.Ready
    }
    WorkspacesLetter {
        anchors.fill: parent
        size: ai.size
        name: ai.appName
        visible: img.status !== Image.Ready
    }
}
