import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config

// Carte de la visite guidée : icône, titre, texte, touches, contenu libre, points de progression et boutons.
StyledRect {
    id: card

    property string icon
    property string title
    property string body
    property var combos: []
    property int index: 0
    property int count: 1
    property string nextText: qsTr("Suivant")
    default property alias extra: extraColumn.data

    signal skip
    signal previous
    signal next

    implicitWidth: 480
    // Largeur utile du texte : un Text qui retourne à la ligne doit recevoir une largeur explicite dans une mise en page.
    readonly property real inner: width - Appearance.padding.large * 2
    implicitHeight: column.implicitHeight + Appearance.padding.large * 2
    radius: Appearance.rounding.large
    // Aucun verre natif derrière la carte : la teinte verre (alpha ≤ 6 %) la rendait illisible. Couleur pleine de la palette, quasi opaque.
    color: Qt.alpha(Colours.solid(Colours.palette.m3surfaceContainer), 0.96)
    opacity: 1
    border.width: 1
    border.color: Colours.palette.m3outlineVariant

    ColumnLayout {
        id: column

        anchors.fill: parent
        anchors.margins: Appearance.padding.large
        spacing: Appearance.spacing.normal

        RowLayout {
            Layout.fillWidth: true
            spacing: Appearance.spacing.normal

            StyledRect {
                implicitWidth: 44
                implicitHeight: 44
                radius: Appearance.rounding.full
                color: Colours.palette.m3primaryContainer

                MaterialIcon {
                    anchors.centerIn: parent
                    text: card.icon
                    font.pointSize: Appearance.font.size.large
                    color: Colours.palette.m3onPrimaryContainer
                }
            }

            StyledText {
                Layout.fillWidth: true
                Layout.maximumWidth: card.inner - 44 - Appearance.spacing.normal
                text: card.title
                wrapMode: Text.WordWrap
                font.pointSize: Appearance.font.size.large
                font.weight: Font.Medium
                color: Colours.palette.m3onSurface
            }
        }

        StyledText {
            Layout.fillWidth: true
            Layout.maximumWidth: card.inner
            visible: card.body !== ""
            text: card.body
            wrapMode: Text.WordWrap
            color: Colours.palette.m3onSurfaceVariant
        }

        Flow {
            Layout.fillWidth: true
            visible: card.combos.length > 0
            spacing: Appearance.spacing.normal

            Repeater {
                model: card.combos

                KeyCap {
                    required property string modelData

                    combo: modelData
                }
            }
        }

        ColumnLayout {
            id: extraColumn

            Layout.fillWidth: true
            spacing: Appearance.spacing.small
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.topMargin: Appearance.padding.small
            spacing: Appearance.spacing.small

            Row {
                spacing: 4

                Repeater {
                    model: card.count

                    Rectangle {
                        required property int index

                        width: index === card.index ? 16 : 5
                        height: 5
                        radius: 3
                        color: index === card.index ? Colours.palette.m3primary : Colours.palette.m3outlineVariant

                        Behavior on width {
                            Anim {}
                        }
                    }
                }
            }

            Item {
                Layout.fillWidth: true
            }

            TextButton {
                objectName: "tutorialSkip"
                text: qsTr("Passer")
                type: TextButton.Text
                onClicked: card.skip()
            }

            TextButton {
                objectName: "tutorialPrevious"
                visible: card.index > 0
                text: qsTr("Précédent")
                type: TextButton.Tonal
                onClicked: card.previous()
            }

            TextButton {
                objectName: "tutorialNext"
                text: card.nextText
                type: TextButton.Filled
                onClicked: card.next()
            }
        }
    }
}
