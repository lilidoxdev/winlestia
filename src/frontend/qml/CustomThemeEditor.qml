// Studio de thème personnalisé Winlestia : aperçu du tableau de bord à gauche ; liste des rôles, sélecteur de couleur, hex + pipette et
// verre à droite.
// Le chrome n'utilise que Colours.palette ; les couleurs éditées n'apparaissent que dans l'aperçu, les pastilles et le sélecteur.
import QtQuick
import QtQuick.Window
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.config
import qs.services
import "settings"

Window {
    id: studio

    objectName: "customThemeStudio"
    title: "Winlestia · Palette"
    transientParent: null
    color: "transparent"
    flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint
    width: Math.min(1120, Bridge.screenWidth - 32)
    height: Math.min(720, Bridge.screenHeight - 40)
    x: Bridge.screenX + (Bridge.screenWidth - width) / 2
    y: Bridge.screenY + (Bridge.screenHeight - height) / 2

    property var roles: [
        {
            key: "background",
            label: qsTr("Fond")
        },
        {
            key: "text",
            label: qsTr("Texte")
        },
        {
            key: "primary",
            label: qsTr("Principal")
        },
        {
            key: "secondary",
            label: qsTr("Secondaire")
        },
        {
            key: "tertiary",
            label: qsTr("Troisième accent")
        }
    ]
    property var draft: ({
            background: "#fff8f6",
            text: "#261916",
            primary: "#895143",
            secondary: "#775b52",
            tertiary: "#68613c"
        })
    property int selected: 0
    property real hue: 0
    property real saturation: 0
    property real brightness: 1
    property bool glass: false
    property real transparency: 88
    // L'aperçu passe au-dessus des contrôles sur les petits écrans.
    readonly property bool narrow: width < 860
    readonly property string selectedKey: roles[selected].key

    function syncHSV(): void {
        const value = Bridge.colorHSV(draft[roles[selected].key]);
        hue = value[0];
        saturation = value[1];
        brightness = value[2];
    }

    function openEditor(): void {
        draft = Object.assign({}, draft, Bridge.settings.customPalette ?? {});
        glass = Bridge.settings.customGlass ?? false;
        transparency = (1 - (Bridge.settings.customGlassOpacity ?? 0.12)) * 100;
        syncHSV();
        Bridge.editTheme(true);
        visible = true;
        requestActivate();
    }

    function finish(): void {
        visible = false;
        Bridge.editTheme(false);
    }

    function putColor(value: string, sync: bool): void {
        if (!Bridge.validHex(value))
            return;
        const copy = Object.assign({}, draft);
        copy[roles[selected].key] = value.toLowerCase();
        draft = copy;
        if (sync)
            syncHSV();
    }

    function wheelColor(h: real, s: real, v: real): void {
        hue = h;
        saturation = s;
        brightness = v;
        putColor(Bridge.hsvColor(h, s, v), false);
    }

    onSelectedChanged: syncHSV()
    onClosing: event => {
        event.accepted = false;
        finish();
    }

    Shortcut {
        sequence: "Escape"
        enabled: studio.visible
        onActivated: studio.finish()
    }

    Connections {
        target: Bridge

        function onColorPicked(value: string): void {
            if (value)
                studio.putColor(value, true);
            studio.visible = true;
            studio.requestActivate();
        }
    }

    Rectangle {
        anchors.fill: parent
        radius: Appearance.rounding.small
        color: Colours.surfaceBase
        border.width: 1
        border.color: Qt.alpha(Colours.palette.m3outlineVariant, 0.7)

        ColumnLayout {
            anchors.fill: parent
            anchors.margins: Appearance.padding.large
            spacing: Appearance.spacing.normal

            RowLayout {
                Layout.fillWidth: true
                Layout.leftMargin: Appearance.padding.smaller

                ColumnLayout {
                    spacing: 1

                    StyledText {
                        text: qsTr("Ma palette")
                        font.pointSize: Appearance.font.size.large
                        font.weight: Font.Medium
                    }

                    StyledText {
                        text: qsTr("Cinq couleurs, tout ton espace")
                        font.pointSize: Appearance.font.size.small
                        color: Colours.palette.m3outline
                    }
                }

                Item {
                    Layout.fillWidth: true
                }

                IconButton {
                    icon: "close"
                    type: IconButton.Text
                    onClicked: studio.finish()
                }
            }

            GridLayout {
                Layout.fillWidth: true
                Layout.fillHeight: true
                columns: studio.narrow ? 1 : 2
                columnSpacing: Appearance.spacing.normal
                rowSpacing: Appearance.spacing.normal

                // Live preview
                ColumnLayout {
                    Layout.fillWidth: true
                    Layout.fillHeight: !studio.narrow
                    Layout.preferredHeight: studio.narrow ? 200 : -1
                    spacing: Appearance.spacing.small

                    StyledText {
                        Layout.leftMargin: Appearance.padding.smaller
                        text: qsTr("Aperçu en direct")
                        font.pointSize: Appearance.font.size.small
                        font.weight: Font.Medium
                        color: Colours.palette.m3outline
                    }

                    // Fond neutre (formes QML simples, sans image) pour juger la palette équitablement.
                    Rectangle {
                        id: stage

                        objectName: "previewStage"
                        Layout.fillWidth: true
                        Layout.fillHeight: true
                        radius: Appearance.rounding.small
                        clip: true
                        gradient: Gradient {
                            GradientStop {
                                position: 0
                                color: "#a3a6aa"
                            }
                            GradientStop {
                                position: 1
                                color: "#6a6d72"
                            }
                        }

                        Rectangle {
                            x: parent.width * 0.55
                            y: -parent.height * 0.25
                            width: parent.height * 1.1
                            height: width
                            radius: width / 2
                            color: "#ffffff"
                            opacity: 0.10
                        }

                        Rectangle {
                            x: -parent.width * 0.12
                            y: parent.height * 0.5
                            width: parent.height * 0.9
                            height: width
                            radius: width / 2
                            color: "#000000"
                            opacity: 0.10
                        }

                        Rectangle {
                            anchors.horizontalCenter: parent.horizontalCenter
                            anchors.bottom: parent.bottom
                            width: parent.width
                            height: parent.height * 0.18
                            color: "#000000"
                            opacity: 0.08
                        }

                        ThemePreviewMock {
                            id: mock

                            objectName: "themePreview"
                            readonly property real fit: Math.max(0.1, Math.min((stage.width - 24) / implicitWidth,
                                (stage.height - 24) / implicitHeight))

                            colors: studio.draft
                            glass: studio.glass
                            glassAlpha: 1 - studio.transparency / 100
                            scale: fit
                            transformOrigin: Item.TopLeft
                            x: (stage.width - implicitWidth * fit) / 2
                            y: (stage.height - implicitHeight * fit) / 2
                        }
                    }
                }

                // Controls
                Flickable {
                    id: side

                    objectName: "customSide"

                    Layout.preferredWidth: studio.narrow ? -1 : 336
                    Layout.fillWidth: studio.narrow
                    Layout.fillHeight: true
                    contentWidth: width
                    contentHeight: controls.implicitHeight
                    clip: true
                    boundsBehavior: Flickable.StopAtBounds

                    ColumnLayout {
                        id: controls

                        width: side.width
                        spacing: Appearance.spacing.small

                        ThemeStudioRoles {
                            studio: studio
                        }

                        ThemeStudioPicker {
                            studio: studio
                        }

                        ThemeStudioGlass {
                            studio: studio
                        }
                    }
                }
            }

            RowLayout {
                Layout.fillWidth: true
                spacing: Appearance.spacing.small

                Item {
                    Layout.fillWidth: true
                }

                IconTextButton {
                    text: qsTr("Annuler")
                    type: IconTextButton.Text
                    onClicked: studio.finish()
                }

                IconTextButton {
                    objectName: "applyCustomTheme"
                    text: qsTr("Appliquer")
                    icon: "check"
                    onClicked: {
                        Bridge.action("customTheme", {
                            colors: studio.draft,
                            glass: studio.glass,
                            opacity: 1 - studio.transparency / 100
                        });
                        studio.finish();
                    }
                }
            }
        }
    }

    Window {
        title: "Winlestia · Pipette"
        transientParent: null
        visible: Bridge.picking
        flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.WindowDoesNotAcceptFocus | Qt.WindowTransparentForInput
        color: "transparent"
        width: 180
        height: 72
        x: Bridge.pickX
        y: Bridge.pickY

        Rectangle {
            anchors.fill: parent
            radius: Appearance.rounding.small
            color: Colours.surfaceBase
            border.width: 1
            border.color: Colours.palette.m3primary

            Row {
                anchors.centerIn: parent
                spacing: 10

                Rectangle {
                    width: 28
                    height: 28
                    radius: Appearance.rounding.tiny
                    color: Bridge.pickColor
                }

                Column {
                    spacing: 4

                    StyledText {
                        text: Bridge.pickColor
                        font.pointSize: Appearance.font.size.normal
                    }

                    StyledText {
                        text: qsTr("Échap · annuler")
                        font.pointSize: Appearance.font.size.small
                        color: Colours.palette.m3outline
                    }
                }
            }
        }
    }
}
