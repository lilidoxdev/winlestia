// En-tête de l'espace : titre, compteur et actions.
// Extrait de WorkspaceBrowser.qml (pur découpage) : les ids du parent sont reçus en propriétés requises.
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.services
import qs.config

RowLayout {
    required property var browser
    Layout.fillWidth: true
    opacity: browser.pHead
    transform: Translate {
        y: browser.fx ? (1 - browser.pHead) * -14 : 0
    }
    Column {
        spacing: Appearance.padding.small
        StyledText {
            text: browser.title
            font.pointSize: Appearance.font.size.extraLarge
            font.weight: Font.Medium
        }
        Row {
            spacing: Appearance.spacing.small
            Rectangle {
                anchors.verticalCenter: parent.verticalCenter
                implicitWidth: Math.max(28, countText.implicitWidth + 16)
                implicitHeight: 22
                radius: Appearance.rounding.full
                color: Colours.palette.m3primaryContainer
                StyledText {
                    id: countText
                    anchors.centerIn: parent
                    text: browser.entries.length
                    font.pointSize: Appearance.font.size.small
                    font.weight: Font.Medium
                    color: Colours.palette.m3onPrimaryContainer
                }
            }
            StyledText {
                anchors.verticalCenter: parent.verticalCenter
                text: qsTr("accès · Clique pour ouvrir, clic droit pour organiser")
                font.pointSize: Appearance.font.size.small
                color: Colours.palette.m3outline
            }
        }
    }
    Item {
        Layout.fillWidth: true
    }
    IconButton {
        icon: "search"
        type: IconButton.Text
        onClicked: Bridge.navigate("palette")
    }
    IconButton {
        icon: "add"
        type: IconButton.Text
        onClicked: Bridge.action("addAccess", {
            board: browser.scope
        })
    }
    IconButton {
        icon: "create_new_folder"
        type: IconButton.Text
        visible: browser.scope === "projects" || browser.scope === "documents"
        onClicked: Bridge.action("addFolder", {
            board: browser.scope
        })
    }
    IconButton {
        icon: "close"
        type: IconButton.Text
        onClicked: Bridge.closePanel()
    }
}
