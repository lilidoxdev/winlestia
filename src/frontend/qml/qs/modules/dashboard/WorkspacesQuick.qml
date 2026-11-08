// Actions rapides de l'onglet Espaces.
// Extrait de Workspaces.qml (pur découpage) : `root` désigne l'onglet parent.
import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes
import qs.components
import qs.components.controls
import qs.services
import qs.config

StyledRect {
    required property var root
    Layout.fillWidth: true
    implicitHeight: 56
    radius: Appearance.rounding.large
    color: Colours.palette.m3surfaceContainer
    opacity: qe.alpha
    transform: Translate {
        y: qe.dy
    }
    WorkspacesEntrance {
        id: qe
        host: root
        order: 8
    }
    RowLayout {
        anchors.fill: parent
        anchors.leftMargin: Appearance.padding.normal
        anchors.rightMargin: Appearance.padding.normal
        spacing: Appearance.spacing.small
        WorkspacesPill {
            host: root
            order: 9
            icon: "content_paste"
            text: qsTr("Presse-papiers")
            onClicked: Bridge.navigate("clipboard")
        }
        WorkspacesPill {
            host: root
            order: 10
            icon: "search"
            text: qsTr("Commande rapide")
            onClicked: Bridge.navigate("palette")
        }
        Item {
            Layout.fillWidth: true
        }
        WorkspacesPill {
            host: root
            order: 12
            icon: "tune"
            text: qsTr("Personnaliser")
            onClicked: Bridge.navigate("settings")
        }
    }
}
