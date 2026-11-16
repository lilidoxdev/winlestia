import QtQuick
import qs.components
import qs.services
import qs.config

// Pilule du mot de passe : cadenas, une petite forme par caractère saisi, bouton rond (flèche quand il y a du texte).
// Le texte n'est jamais conservé : le champ est vidé après chaque envoi.
StyledRect {
    id: root

    required property real centerScale
    property bool blocked: false
    readonly property bool hasText: input.text.length > 0
    readonly property real buttonSize: 56 * centerScale
    readonly property real dotSize: 14 * centerScale

    function focusInput(): void {
        input.forceActiveFocus();
    }

    function submit(): void {
        if (!hasText || blocked)
            return;
        Bridge.action("lockUnlock", {
            password: input.text
        });
        input.text = "";
    }

    implicitHeight: buttonSize + Appearance.padding.small * 2
    radius: Appearance.rounding.full
    color: Colours.layer(Colours.solid(Colours.palette.m3surfaceContainer), 2)

    TextInput {
        id: input

        anchors.fill: parent
        opacity: 0
        echoMode: TextInput.Password
        inputMethodHints: Qt.ImhSensitiveData | Qt.ImhNoPredictiveText
        focus: true
        onAccepted: root.submit()
        Keys.onEscapePressed: input.text = ""
    }

    MaterialIcon {
        id: lockIcon

        anchors.verticalCenter: parent.verticalCenter
        anchors.left: parent.left
        anchors.leftMargin: Appearance.padding.large
        text: "lock"
        color: Colours.palette.m3onSurfaceVariant
        font.pointSize: Appearance.font.size.large
    }

    Item {
        anchors.left: lockIcon.right
        anchors.right: button.left
        anchors.margins: Appearance.padding.normal
        anchors.verticalCenter: parent.verticalCenter
        implicitHeight: root.dotSize
        clip: true

        StyledText {
            anchors.centerIn: parent
            visible: !root.hasText
            text: qsTr("Mot de passe")
            color: Colours.palette.m3outline
        }

        Row {
            id: dots

            anchors.centerIn: parent
            spacing: Appearance.spacing.small

            Repeater {
                model: input.text.length

                Rectangle {
                    implicitWidth: root.dotSize
                    implicitHeight: root.dotSize
                    radius: index % 2 === 0 ? width / 2 : width * 0.3
                    rotation: index % 2 === 0 ? 0 : 45
                    color: Colours.palette.m3onSurface
                    scale: 0

                    Component.onCompleted: scale = 1

                    Behavior on scale {
                        Anim {
                            duration: Appearance.anim.durations.expressiveFastSpatial
                            easing.bezierCurve: Appearance.anim.curves.expressiveFastSpatial
                        }
                    }
                }
            }
        }
    }

    StyledRect {
        id: button

        anchors.verticalCenter: parent.verticalCenter
        anchors.right: parent.right
        anchors.rightMargin: Appearance.padding.small
        implicitWidth: root.buttonSize
        implicitHeight: root.buttonSize
        radius: Appearance.rounding.full
        color: root.hasText ? Colours.palette.m3primary : Colours.solid(Colours.palette.m3surfaceContainerHigh)

        StateLayer {
            color: root.hasText ? Colours.palette.m3onPrimary : Colours.palette.m3onSurface

            function onClicked(): void {
                root.submit();
                root.focusInput();
            }
        }

        MaterialIcon {
            anchors.centerIn: parent
            text: root.hasText ? "arrow_forward" : "lock_open"
            color: root.hasText ? Colours.palette.m3onPrimary : Colours.palette.m3onSurfaceVariant
            font.pointSize: Appearance.font.size.large
        }
    }
}
