// En-tête de l'onglet Espaces : salutation selon l'heure.
// Extrait de Workspaces.qml (pur découpage) : `root` désigne l'onglet parent.
import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes
import qs.components
import qs.components.controls
import qs.services
import qs.config

RowLayout {
    id: header
    required property var root
    Layout.fillWidth: true
    spacing: Appearance.spacing.normal
    opacity: he.alpha
    transform: Translate {
        y: he.dy
    }
    WorkspacesEntrance {
        id: he
        host: root
        order: 0
    }
    Item {
        implicitWidth: 52
        implicitHeight: 52
        WorkspacesBlob {
            anchors.fill: parent
            lobes: 9
            amp: 0.06
            color: Colours.palette.m3primaryContainer
            rotation: he.reveal * 40
        }
        MaterialIcon {
            anchors.centerIn: parent
            text: root.hour < 5 || root.hour >= 20 ? "bedtime" : root.hour < 12 ? "wb_twilight" : "wb_sunny"
            fill: 1
            color: Colours.palette.m3onPrimaryContainer
            font.pointSize: Appearance.font.size.large
        }
    }
    ColumnLayout {
        spacing: 0
        StyledText {
            text: root.hour < 5 ? qsTr("Bonne nuit") : root.hour < 12 ? qsTr("Bonjour") : root.hour < 18 ? qsTr("Bon après-midi")
                : qsTr("Bonsoir")
            font.pointSize: Appearance.font.size.small
            font.weight: 500
            color: Colours.palette.m3primary
        }
        StyledText {
            text: qsTr("Reprendre ton espace")
            font.pointSize: Appearance.font.size.title
            font.weight: 600
        }
    }
    Item {
        Layout.fillWidth: true
    }
    WorkspacesPill {
        host: root
        order: 1
        icon: "arrow_forward"
        text: qsTr("Tout afficher")
        filled: true
        onClicked: Bridge.navigate("workspace:all")
    }
}
