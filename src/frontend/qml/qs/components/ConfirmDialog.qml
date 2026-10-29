import qs.services
import qs.config
import qs.components.controls
import QtQuick
import QtQuick.Layouts

Dialog {
    id: root

    property string message
    property string confirmText: qsTr("Confirmer")
    property string cancelText: qsTr("Annuler")
    property bool destructive

    signal confirmed

    StyledText {
        Layout.fillWidth: true
        visible: root.message !== ""
        text: root.message
        wrapMode: Text.WordWrap
        font.pointSize: Appearance.font.size.normal
        color: Colours.palette.m3onSurfaceVariant
    }

    actions: [
        Item {
            Layout.fillWidth: true
        },
        TextButton {
            type: TextButton.Text
            text: root.cancelText
            onClicked: root.close()
        },
        TextButton {
            type: TextButton.Text
            text: root.confirmText
            inactiveOnColour: root.destructive ? Colours.palette.m3error : Colours.palette.m3primary
            onClicked: {
                root.confirmed();
                root.close();
            }
        }
    ]
}
