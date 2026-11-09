import QtQuick
import QtQuick.Window
import qs.components
import qs.config

// Mode édition, un par écran : fenêtre de repères d'alignement (transparente aux clics) et barre « Terminé » en haut au centre.
QtObject {
    id: overlay

    required property string screenName
    readonly property var area: {
        Bridge.winjets.revision;
        return Bridge.winjets.workArea(screenName);
    }
    readonly property var screenGuides: Bridge.winjets.guides.filter(g => g.screen === screenName)

    property var guideWindow: Window {
        visible: !!overlay.area && overlay.area.w > 0
        x: overlay.area.x ?? 0
        y: overlay.area.y ?? 0
        width: overlay.area.w ?? 1
        height: overlay.area.h ?? 1
        color: "transparent"
        flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowDoesNotAcceptFocus | Qt.WindowTransparentForInput | Qt.NoDropShadowWindowHint

        Repeater {
            model: overlay.screenGuides

            Rectangle {
                required property var modelData
                readonly property bool vertical: modelData.axis === "x"

                x: vertical ? modelData.pos : modelData.from
                y: vertical ? modelData.from : modelData.pos
                width: vertical ? 1 : modelData.to - modelData.from
                height: vertical ? modelData.to - modelData.from : 1
                color: Colours.palette.m3primary
            }
        }
    }

    property var barWindow: Window {
        visible: !!overlay.area && overlay.area.w > 0
        width: bar.implicitWidth
        height: bar.implicitHeight
        x: (overlay.area.x ?? 0) + ((overlay.area.w ?? 0) - width) / 2
        y: (overlay.area.y ?? 0) + 16
        color: "transparent"
        flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.WindowDoesNotAcceptFocus | Qt.NoDropShadowWindowHint

        Rectangle {
            id: bar

            implicitWidth: label.implicitWidth + done.implicitWidth + 48
            implicitHeight: 48
            anchors.fill: parent
            radius: height / 2
            color: Colours.palette.m3surfaceContainerHigh
            border.width: 1
            border.color: WinjetTheme.border

            StyledText {
                id: label

                anchors.left: parent.left
                anchors.leftMargin: 20
                anchors.verticalCenter: parent.verticalCenter
                text: qsTr("Modification des Winjets")
                color: Colours.palette.m3onSurface
                font.pointSize: Appearance.font.size.normal
            }

            Rectangle {
                id: done

                anchors.right: parent.right
                anchors.rightMargin: 8
                anchors.verticalCenter: parent.verticalCenter
                implicitWidth: doneLabel.implicitWidth + 32
                width: implicitWidth
                height: 32
                radius: 16
                color: Colours.palette.m3primary

                StyledText {
                    id: doneLabel

                    anchors.centerIn: parent
                    text: qsTr("Terminé")
                    color: Colours.palette.m3onPrimary
                    font.pointSize: Appearance.font.size.normal
                    font.weight: Font.Medium
                }
                MouseArea {
                    anchors.fill: parent
                    cursorShape: Qt.PointingHandCursor
                    onClicked: Bridge.winjets.setEditMode(false)
                }
            }
        }
    }
}
