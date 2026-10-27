import "../services"
import qs.components
import qs.services
import qs.config
import Quickshell
import Quickshell.Widgets
import QtQuick

Item {
    id: root

    required property var modelData
    required property PersistentProperties visibilities

    implicitHeight: Config.launcher.sizes.itemHeight

    anchors.left: parent?.left
    anchors.right: parent?.right

    StateLayer {
        radius: Appearance.rounding.normal

        function onClicked(): void {
            Apps.launch(root.modelData);
            root.visibilities.launcher = false;
        }
    }

    Component.onCompleted: {if(modelData.kind==="app"||modelData.kind==="systemRecent")Bridge.action("icon",{id:modelData.id})}
    Item {
        anchors.fill: parent
        anchors.leftMargin: Appearance.padding.larger
        anchors.rightMargin: Appearance.padding.larger
        anchors.margins: Appearance.padding.smaller

        IconImage {
            id: icon

            source: Bridge.iconUrl(root.modelData.id,Bridge.iconRevision)
            implicitSize: parent.height * 0.8

            anchors.verticalCenter: parent.verticalCenter
        }

        MaterialIcon {anchors.centerIn:icon;text:root.modelData.icon||"apps";visible:!icon.source.toString()}
        Item {
            anchors.left: icon.right
            anchors.leftMargin: Appearance.spacing.normal
            anchors.verticalCenter: icon.verticalCenter

            implicitWidth: parent.width - icon.width
            implicitHeight: name.implicitHeight + comment.implicitHeight

            StyledText {
                id: name

                text: root.modelData?.name ?? ""
                font.pointSize: Appearance.font.size.normal
            }

            StyledText {
                id: comment

                text: (root.modelData?.comment || root.modelData?.genericName || root.modelData?.name) ?? ""
                font.pointSize: Appearance.font.size.small
                color: Colours.palette.m3outline

                elide: Text.ElideRight
                width: root.width - icon.width - Appearance.rounding.normal * 2

                anchors.top: name.bottom
            }
        }
    }
}
