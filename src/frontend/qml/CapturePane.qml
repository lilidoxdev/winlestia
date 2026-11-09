import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config

ColumnLayout {
    spacing: Appearance.spacing.large
    RowLayout {
        Layout.fillWidth: true
        spacing: Appearance.spacing.normal
        MaterialIcon {
            text: qsTr("screenshot_monitor")
            font.pointSize: Appearance.font.size.extraLarge
            color: Colours.palette.m3primary
        }
        StyledText {
            text: qsTr("Capture d’écran")
            font.pointSize: Appearance.font.size.title
            Layout.fillWidth: true
        }
        IconButton {
            icon: "close"
            type: IconButton.Text
            onClicked: Bridge.closePanel()
        }
    }
    StyledText {
        Layout.fillWidth: true
        text: qsTr("Sélectionne, annote et partage ce qui compte.")
        font.pointSize: Appearance.font.size.normal
        color: Colours.palette.m3outline
        wrapMode: Text.Wrap
    }
    Repeater {
        model: [
            {
                icon: "crop_free",
                name: qsTr("Sélectionner une zone"),
                detail: qsTr("Dessiner, surligner, copier, enregistrer ou épingler"),
                mode: "region"
            },
            {
                icon: "document_scanner",
                name: qsTr("Extraire du texte"),
                detail: qsTr("Le texte reconnu rejoint le presse-papiers"),
                mode: "text"
            },
            {
                icon: "desktop_windows",
                name: qsTr("Copier cet écran"),
                detail: qsTr("Une image complète de l’écran actuel"),
                mode: "screen"
            }
        ]
        StyledRect {
            id: card
            required property var modelData
            Layout.fillWidth: true
            implicitHeight: 95
            radius: Appearance.rounding.large
            color: Colours.palette.m3surfaceContainer
            StateLayer {
                function onClicked() {
                    Bridge.capture.start(card.modelData.mode);
                }
            }
            RowLayout {
                anchors.fill: parent
                anchors.margins: Appearance.padding.large
                spacing: Appearance.spacing.larger
                MaterialIcon {
                    text: card.modelData.icon
                    font.pointSize: Appearance.font.size.extraLarge
                    color: Colours.palette.m3primary
                }
                ColumnLayout {
                    Layout.fillWidth: true
                    spacing: Appearance.spacing.small
                    StyledText {
                        text: card.modelData.name
                        font.pointSize: Appearance.font.size.normal
                    }
                    StyledText {
                        Layout.fillWidth: true
                        text: card.modelData.detail
                        font.pointSize: Appearance.font.size.small
                        wrapMode: Text.Wrap
                        color: Colours.palette.m3outline
                    }
                }
                MaterialIcon {
                    text: qsTr("chevron_right")
                    font.pointSize: Appearance.font.size.larger
                    color: Colours.palette.m3outline
                }
            }
        }
    }
    StyledText {
        text: qsTr("Ctrl + F3 · capture rapide d’une zone")
        font.pointSize: Appearance.font.size.small
        color: Colours.palette.m3outline
    }
}
