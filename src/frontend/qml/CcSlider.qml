// Curseur épais du Centre de contrôle : partie remplie primaire, piste secondaryContainer, icône dans le remplissage, valeur à droite.
import QtQuick
import QtQuick.Templates as T
import qs.components
import qs.services
import qs.config

T.Slider {
    id: root
    property string icon: ""
    property real source: 0
    property bool showValue: true
    property bool iconActive: true
    readonly property real fillWidth: visualPosition * width
    signal iconClicked

    implicitHeight: 44
    implicitWidth: 200
    from: 0
    to: 100
    stepSize: 1
    Binding {
        target: root
        property: "value"
        value: root.source
        when: !root.pressed
    }
    background: Rectangle {
        radius: 14
        color: Colours.palette.m3secondaryContainer
        clip: true
        Rectangle {
            width: Math.max(root.fillWidth, root.pressed || root.value > 0 ? 28 : 0)
            height: parent.height
            radius: 14
            color: Colours.palette.m3primary
        }
    }
    handle: Item {
        width: 0
        height: 0
    }
    MaterialIcon {
        id: glyph
        x: 12
        anchors.verticalCenter: parent.verticalCenter
        text: root.icon
        fill: root.iconActive ? 1 : 0
        color: root.fillWidth > 36 ? Colours.palette.m3onPrimary : Colours.palette.m3onSecondaryContainer
    }
    StyledText {
        visible: root.showValue
        anchors.right: parent.right
        anchors.rightMargin: 14
        anchors.verticalCenter: parent.verticalCenter
        text: Math.round(root.value) + " %"
        font.pointSize: Appearance.font.size.small
        opacity: .75
        color: root.fillWidth > root.width - 56 ? Colours.palette.m3onPrimary : Colours.palette.m3onSecondaryContainer
    }
    MouseArea {
        visible: root.icon !== ""
        x: 0
        width: 44
        height: parent.height
        cursorShape: Qt.PointingHandCursor
        onClicked: root.iconClicked()
    }
}
