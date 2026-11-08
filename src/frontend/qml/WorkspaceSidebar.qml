// Barre latérale de navigation entre les espaces.
// Extrait de WorkspaceBrowser.qml (pur découpage) : les ids du parent sont reçus en propriétés requises.
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.services
import qs.config

StyledRect {
    id: side
    required property var browser
    Layout.preferredWidth: 252
    Layout.minimumWidth: 252
    Layout.maximumWidth: 252
    Layout.fillWidth: false
    Layout.fillHeight: true
    radius: Appearance.rounding.large
    color: Colours.palette.m3surfaceContainer
    opacity: browser.pSide
    transform: Translate {
        x: browser.fx ? (1 - browser.pSide) * -24 : 0
    }
    ColumnLayout {
        anchors.fill: parent
        anchors.margins: Appearance.padding.large
        spacing: Appearance.spacing.small
        RowLayout {
            Layout.leftMargin: Appearance.padding.small
            Layout.topMargin: Appearance.padding.small
            Layout.bottomMargin: Appearance.spacing.normal
            spacing: Appearance.spacing.normal
            StyledRect {
                implicitWidth: 40
                implicitHeight: 40
                radius: Appearance.rounding.full
                color: Colours.palette.m3primaryContainer
                MaterialIcon {
                    anchors.centerIn: parent
                    text: qsTr("space_dashboard")
                    fill: 1
                    font.pointSize: Appearance.font.size.larger
                    color: Colours.palette.m3onPrimaryContainer
                }
            }
            Column {
                StyledText {
                    text: qsTr("Ton espace")
                    font.pointSize: Appearance.font.size.large
                    font.weight: Font.Medium
                }
                StyledText {
                    text: browser.counts.all + " " + qsTr("accès")
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3outline
                }
            }
        }
        Item {
            Layout.fillWidth: true
            implicitHeight: browser.navY(6) + 48
            // Sliding selection pill: one rectangle that springs between items.
            Rectangle {
                width: parent.width
                height: 48
                radius: Appearance.rounding.full
                color: Colours.palette.m3secondaryContainer
                visible: browser.navIndex >= 0
                y: browser.navY(Math.max(0, browser.navIndex))
                Behavior on y {
                    WorkspaceSpring {}
                }
            }
            Rectangle {
                y: browser.navY(3) + 48 + 8
                width: parent.width
                height: 1
                color: Colours.palette.m3outlineVariant
            }
            Repeater {
                model: browser.navItems
                WorkspaceNavItem {
                    required property var modelData
                    required property int index
                    width: parent.width
                    y: browser.navY(index)
                    icon: modelData.icon
                    label: qsTr(modelData.name)
                    count: browser.counts[modelData.id] ?? 0
                    selected: browser.scope === modelData.id
                    onTriggered: browser.selectBoard(modelData.id)
                }
            }
        }
        Item {
            Layout.fillHeight: true
        }
        IconTextButton {
            Layout.fillWidth: true
            icon: "content_paste"
            text: qsTr("Presse-papiers")
            type: IconTextButton.Text
            onClicked: Bridge.navigate("clipboard")
        }
        IconTextButton {
            Layout.fillWidth: true
            icon: "settings"
            text: qsTr("Personnaliser")
            type: IconTextButton.Text
            onClicked: Bridge.navigate("settings")
        }
    }
}
