// Winlestia custom theme studio: live dashboard preview on the left, role list, colour picker, hex + eyedropper and glass on the right.
// Chrome uses Colours.palette only; the edited colours appear solely inside the preview, the swatches and the picker.
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

    property var roles: [{key: "background", label: qsTr("Fond")}, {key: "text", label: qsTr("Texte")}, {key: "primary", label: qsTr("Principal")}, {key: "secondary", label: qsTr("Secondaire")}, {key: "tertiary", label: qsTr("Troisième accent")}]
    property var draft: ({background: "#fff8f6", text: "#261916", primary: "#895143", secondary: "#775b52", tertiary: "#68613c"})
    property int selected: 0
    property real hue: 0
    property real saturation: 0
    property real brightness: 1
    property bool glass: false
    property real transparency: 88
    // Preview stacks above the controls on small screens.
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

                    // Neutral backdrop (plain QML shapes, no image) so the palette is judged fairly.
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
                            readonly property real fit: Math.max(0.1, Math.min((stage.width - 24) / implicitWidth, (stage.height - 24) / implicitHeight))

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

                        SettingsSection {
                            label: qsTr("Couleurs")

                            Repeater {
                                model: studio.roles

                                Rectangle {
                                    id: role

                                    required property var modelData
                                    required property int index
                                    readonly property bool current: studio.selected === index

                                    objectName: "custom-role-" + modelData.key
                                    Layout.fillWidth: true
                                    implicitHeight: 28
                                    radius: Appearance.rounding.smaller
                                    color: current ? Qt.alpha(Colours.palette.m3primary, 0.14) : Qt.alpha(Colours.palette.m3onSurface, hover.hovered ? 0.06 : 0)

                                    Behavior on color {
                                        enabled: !Appearance.reducedEffects
                                        ColorAnimation {
                                            duration: Appearance.anim.durations.small
                                        }
                                    }

                                    HoverHandler {
                                        id: hover
                                    }

                                    TapHandler {
                                        onTapped: studio.selected = role.index
                                    }

                                    Rectangle {
                                        visible: role.current
                                        anchors.verticalCenter: parent.verticalCenter
                                        x: 0
                                        width: 3
                                        height: 18
                                        radius: 1
                                        color: Colours.palette.m3primary
                                    }

                                    RowLayout {
                                        anchors.fill: parent
                                        anchors.leftMargin: Appearance.padding.normal
                                        anchors.rightMargin: Appearance.padding.normal
                                        spacing: Appearance.spacing.normal

                                        Rectangle {
                                            objectName: "custom-swatch-" + role.modelData.key
                                            Layout.alignment: Qt.AlignVCenter
                                            implicitWidth: 22
                                            implicitHeight: 22
                                            radius: Appearance.rounding.tiny
                                            color: studio.draft[role.modelData.key]
                                            border.width: 1
                                            border.color: Colours.palette.m3outline
                                        }

                                        StyledText {
                                            Layout.fillWidth: true
                                            Layout.alignment: Qt.AlignVCenter
                                            text: role.modelData.label
                                            elide: Text.ElideRight
                                            font.pointSize: Appearance.font.size.normal
                                            font.weight: role.current ? Font.Medium : Font.Normal
                                        }

                                        StyledText {
                                            Layout.alignment: Qt.AlignVCenter
                                            text: String(studio.draft[role.modelData.key]).toUpperCase()
                                            font.pointSize: Appearance.font.size.small
                                            color: role.current ? Colours.palette.m3primary : Colours.palette.m3outline
                                        }
                                    }
                                }
                            }
                        }

                        SettingsSection {
                            label: ""

                            ColumnLayout {
                                Layout.fillWidth: true
                                Layout.margins: Appearance.padding.smaller
                                spacing: Appearance.spacing.small

                                // Hue across, intensity (saturation) down, both at the current clarity.
                                Item {
                                    id: square

                                    objectName: "customColorPicker"
                                    Layout.fillWidth: true
                                    implicitHeight: 96

                                    Rectangle {
                                        anchors.fill: parent
                                        radius: Appearance.rounding.tiny
                                        gradient: Gradient {
                                            orientation: Gradient.Horizontal

                                            GradientStop {
                                                position: 0
                                                color: Qt.hsva(0, 1, studio.brightness, 1)
                                            }
                                            GradientStop {
                                                position: 1 / 6
                                                color: Qt.hsva(1 / 6, 1, studio.brightness, 1)
                                            }
                                            GradientStop {
                                                position: 2 / 6
                                                color: Qt.hsva(2 / 6, 1, studio.brightness, 1)
                                            }
                                            GradientStop {
                                                position: 3 / 6
                                                color: Qt.hsva(3 / 6, 1, studio.brightness, 1)
                                            }
                                            GradientStop {
                                                position: 4 / 6
                                                color: Qt.hsva(4 / 6, 1, studio.brightness, 1)
                                            }
                                            GradientStop {
                                                position: 5 / 6
                                                color: Qt.hsva(5 / 6, 1, studio.brightness, 1)
                                            }
                                            GradientStop {
                                                position: 1
                                                color: Qt.hsva(0, 1, studio.brightness, 1)
                                            }
                                        }
                                    }

                                    Rectangle {
                                        anchors.fill: parent
                                        radius: Appearance.rounding.tiny
                                        gradient: Gradient {
                                            GradientStop {
                                                position: 0
                                                color: Qt.hsva(0, 0, studio.brightness, 0)
                                            }
                                            GradientStop {
                                                position: 1
                                                color: Qt.hsva(0, 0, studio.brightness, 1)
                                            }
                                        }
                                    }

                                    Rectangle {
                                        x: studio.hue * square.width - width / 2
                                        y: (1 - studio.saturation) * square.height - height / 2
                                        width: 16
                                        height: 16
                                        radius: 8
                                        color: "transparent"
                                        border.width: 2
                                        border.color: "#ffffff"

                                        Rectangle {
                                            anchors.fill: parent
                                            anchors.margins: -1
                                            radius: 9
                                            color: "transparent"
                                            border.width: 1
                                            border.color: "#80000000"
                                            z: -1
                                        }
                                    }

                                    MouseArea {
                                        function choose(mouse: var): void {
                                            const h = Math.max(0, Math.min(0.999, mouse.x / width));
                                            const s = 1 - Math.max(0, Math.min(1, mouse.y / height));
                                            studio.wheelColor(h, s, studio.brightness);
                                        }

                                        anchors.fill: parent
                                        onPressed: mouse => choose(mouse)
                                        onPositionChanged: mouse => {
                                            if (pressed)
                                                choose(mouse);
                                        }
                                    }
                                }

                                RowLayout {
                                    Layout.fillWidth: true
                                    Layout.topMargin: Appearance.spacing.smaller

                                    StyledText {
                                        text: qsTr("Clarté")
                                        font.pointSize: Appearance.font.size.normal
                                    }

                                    StyledText {
                                        Layout.fillWidth: true
                                        text: qsTr("Du sombre au lumineux")
                                        elide: Text.ElideRight
                                        font.pointSize: Appearance.font.size.small
                                        color: Colours.palette.m3outline
                                    }

                                    StyledText {
                                        text: Math.round(studio.brightness * 100) + " %"
                                        font.pointSize: Appearance.font.size.small
                                        font.weight: Font.Medium
                                        color: Colours.palette.m3primary
                                    }
                                }

                                // Black to the brightest version of the current colour.
                                Item {
                                    id: clarity

                                    objectName: "customClarity"
                                    Layout.fillWidth: true
                                    implicitHeight: 22

                                    Rectangle {
                                        anchors.verticalCenter: parent.verticalCenter
                                        width: parent.width
                                        height: 10
                                        radius: Appearance.rounding.tiny
                                        gradient: Gradient {
                                            orientation: Gradient.Horizontal

                                            GradientStop {
                                                position: 0
                                                color: "#000000"
                                            }
                                            GradientStop {
                                                position: 1
                                                color: Qt.hsva(studio.hue, studio.saturation, 1, 1)
                                            }
                                        }
                                    }

                                    Rectangle {
                                        anchors.verticalCenter: parent.verticalCenter
                                        x: studio.brightness * (clarity.width - width)
                                        width: 8
                                        height: 22
                                        radius: Appearance.rounding.tiny
                                        color: Colours.palette.m3onSurface
                                        border.width: 2
                                        border.color: Colours.surfaceBase
                                    }

                                    MouseArea {
                                        function choose(mouse: var): void {
                                            studio.wheelColor(studio.hue, studio.saturation, Math.max(0, Math.min(1, mouse.x / width)));
                                        }

                                        anchors.fill: parent
                                        onPressed: mouse => choose(mouse)
                                        onPositionChanged: mouse => {
                                            if (pressed)
                                                choose(mouse);
                                        }
                                    }
                                }

                                RowLayout {
                                    Layout.fillWidth: true
                                    Layout.topMargin: 0
                                    spacing: Appearance.spacing.small

                                    StyledTextField {
                                        id: hex

                                        objectName: "customHex"
                                        Layout.fillWidth: true
                                        maximumLength: 7
                                        placeholderText: "#RRGGBB"
                                        onEditingFinished: {
                                            if (Bridge.validHex(text))
                                                studio.putColor(text, true);
                                            else
                                                text = studio.draft[studio.selectedKey];
                                        }

                                        Binding {
                                            target: hex
                                            property: "text"
                                            value: studio.draft[studio.selectedKey]
                                            when: !hex.activeFocus
                                        }
                                    }

                                    IconTextButton {
                                        objectName: "customEyedropper"
                                        Layout.alignment: Qt.AlignVCenter
                                        icon: "colorize"
                                        text: qsTr("Pipette")
                                        type: IconTextButton.Tonal
                                        onClicked: {
                                            studio.visible = false;
                                            Bridge.startColorPick();
                                        }
                                    }
                                }
                            }
                        }

                        SettingsSection {
                            label: ""

                            Rectangle {
                                Layout.fillWidth: true
                                implicitHeight: glassRow.implicitHeight + Appearance.padding.normal * 2
                                radius: Appearance.rounding.small
                                color: Qt.alpha(Colours.palette.m3onSurface, glassHover.hovered ? 0.06 : 0)

                                HoverHandler {
                                    id: glassHover
                                }

                                TapHandler {
                                    onTapped: studio.glass = !studio.glass
                                }

                                RowLayout {
                                    id: glassRow

                                    anchors.left: parent.left
                                    anchors.right: parent.right
                                    anchors.verticalCenter: parent.verticalCenter
                                    anchors.leftMargin: Appearance.padding.larger
                                    anchors.rightMargin: Appearance.padding.larger
                                    spacing: Appearance.spacing.normal

                                    ColumnLayout {
                                        Layout.fillWidth: true
                                        spacing: 1

                                        StyledText {
                                            Layout.fillWidth: true
                                            text: qsTr("Verre transparent")
                                            font.pointSize: Appearance.font.size.normal
                                        }

                                        StyledText {
                                            Layout.fillWidth: true
                                            wrapMode: Text.WordWrap
                                            text: qsTr("Le fond laisse voir le bureau")
                                            font.pointSize: Appearance.font.size.small
                                            color: Colours.palette.m3outline
                                        }
                                    }

                                    SettingsSwitch {
                                        objectName: "customGlassSwitch"
                                        value: studio.glass
                                        onToggled: studio.glass = checked
                                    }
                                }
                            }

                            SettingsSliderRow {
                                title: qsTr("Transparence")
                                hint: ""
                                shown: studio.glass
                                from: 5
                                to: 98
                                value: studio.transparency
                                valueText: Math.round(studio.transparency) + " %"
                                slider.objectName: "customTransparency"
                                onMoved: v => studio.transparency = v
                            }
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
                        Bridge.action("customTheme", {colors: studio.draft, glass: studio.glass, opacity: 1 - studio.transparency / 100});
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
