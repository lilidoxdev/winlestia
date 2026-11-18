// Tuile à bascule Material 3 : éteinte = surfaceContainerHighest + icône contour ; allumée = primaire + icône pleine.
import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config

Item {
    id: root
    property string icon: ""
    property string title: ""
    property string subtitle: ""
    property bool checked: false
    property bool chevron: false
    property string tip: ""
    signal toggled
    signal opened

    implicitHeight: 64
    readonly property color onColour: root.checked ? Colours.palette.m3onPrimary : Colours.palette.m3onSurface
    readonly property color onSub: root.checked ? Colours.palette.m3onPrimary : Colours.palette.m3onSurfaceVariant
    Rectangle {
        id: body
        anchors.fill: parent
        radius: root.checked ? 16 : height / 2
        color: root.checked ? Colours.palette.m3primary : Colours.palette.m3surfaceContainerHighest
        Behavior on color {
            ColorAnimation {
                duration: Appearance.anim.durations.expressiveSlowEffects
                easing.type: Easing.BezierSpline
                easing.bezierCurve: Appearance.anim.curves.expressiveSlowEffects
            }
        }
        Behavior on radius {
            NumberAnimation {
                duration: Appearance.anim.durations.expressiveSlowEffects
                easing.type: Easing.BezierSpline
                easing.bezierCurve: Appearance.anim.curves.expressiveSlowEffects
            }
        }
    }
    RowLayout {
        anchors.fill: parent
        anchors.leftMargin: 12
        anchors.rightMargin: root.chevron ? 2 : 10
        spacing: 8
        MaterialIcon {
            text: root.icon
            fill: root.checked ? 1 : 0
            color: root.onColour
            font.pointSize: Appearance.font.size.large
            Behavior on fill {
                Anim {}
            }
        }
        ColumnLayout {
            Layout.fillWidth: true
            spacing: 0
            StyledText {
                Layout.fillWidth: true
                text: root.title
                elide: Text.ElideRight
                font.pointSize: Appearance.font.size.normal
                font.weight: Font.Medium
                color: root.onColour
            }
            StyledText {
                Layout.fillWidth: true
                text: root.subtitle
                elide: Text.ElideRight
                font.pointSize: Appearance.font.size.small
                color: root.onSub
                opacity: .85
            }
        }
        Item {
            visible: root.chevron
            Layout.preferredWidth: 24
            Layout.preferredHeight: 32
            MaterialIcon {
                anchors.centerIn: parent
                text: "chevron_right"
                color: root.onColour
            }
        }
    }
    HoverHandler {
        id: hover
    }
    ShellToolTip {
        visible: hover.hovered && root.tip !== ""
        delay: 600
        text: root.tip
    }
    MouseArea {
        objectName: "tileBody"
        width: root.chevron ? root.width - 36 : root.width
        height: root.height
        cursorShape: Qt.PointingHandCursor
        onClicked: root.toggled()
    }
    MouseArea {
        objectName: "tileOpen"
        visible: root.chevron
        x: root.width - 36
        width: 36
        height: root.height
        cursorShape: Qt.PointingHandCursor
        onClicked: root.opened()
    }
}
