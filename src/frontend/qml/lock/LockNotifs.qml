import QtQuick
import qs.components
import qs.services
import qs.config

// Notifications : Winlestia n'a pas encore de données. `model` accepte une liste d'objets { summary, body } pour plus tard.
LockCard {
    id: root

    property var model: []

    StyledText {
        id: head

        text: `${root.model.length} ${root.model.length === 1 ? qsTr("notification") : qsTr("notifications")}`
        font.family: Appearance.font.family.mono
        font.pointSize: Appearance.font.size.normal
        font.weight: Font.DemiBold
    }

    ListView {
        anchors.top: head.bottom
        anchors.topMargin: Appearance.spacing.normal
        anchors.bottom: parent.bottom
        width: parent.width
        clip: true
        spacing: Appearance.spacing.small
        model: root.model
        visible: count > 0

        delegate: StyledRect {
            id: item

            required property var modelData

            width: ListView.view.width
            implicitHeight: col.implicitHeight + Appearance.padding.normal * 2
            radius: Appearance.rounding.small
            color: Colours.layer(Colours.solid(Colours.palette.m3surfaceContainerHigh), 2)

            Column {
                id: col

                anchors.fill: parent
                anchors.margins: Appearance.padding.normal

                StyledText {
                    width: parent.width
                    elide: Text.ElideRight
                    text: item.modelData.summary ?? ""
                    font.weight: Font.DemiBold
                }
                StyledText {
                    width: parent.width
                    elide: Text.ElideRight
                    text: item.modelData.body ?? ""
                    color: Colours.palette.m3onSurfaceVariant
                }
            }
        }
    }

    Column {
        anchors.centerIn: parent
        visible: root.model.length === 0
        spacing: Appearance.spacing.normal

        MaterialIcon {
            anchors.horizontalCenter: parent.horizontalCenter
            text: "notifications_off"
            color: Colours.palette.m3outline
            font.pointSize: Appearance.font.size.extraLarge * 1.5
        }
        StyledText {
            anchors.horizontalCenter: parent.horizontalCenter
            text: qsTr("Aucune notification")
            color: Colours.palette.m3outline
        }
    }
}
