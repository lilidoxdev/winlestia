import QtQuick
import qs.components
import qs.services
import qs.config
import "."

// Petit aperçu décoratif (≈ 120×72) qui montre en direct ce que change un réglage.
// kind : theme | glass | motion | reducedEffects | accentBorders | activeBorderOnly | screenFrame | desktopLogo | desktopIndicator
//        | quietFullscreen | topActivationMargin
// value : mode (theme), opacité du verre en % (glass), échelle 0..1 (motion), booléen (interrupteurs), marge en px (topActivationMargin).
// value2 : épaisseur en px pour screenFrame. Tout est dessiné avec la palette ; rien ne tourne au repos, seules les
// transitions de valeur s'animent (et pas du tout avec les effets allégés).
Item {
    id: root

    property string kind
    property var value
    property var value2
    property string label: qsTr("Aperçu")

    readonly property var pal: Colours.palette
    readonly property bool flag: value === true
    readonly property bool animated: !Appearance.reducedEffects
    // Décalage unique joué quand la valeur de « motion » change.
    property real slide: 0

    implicitWidth: 120
    implicitHeight: 72
    Accessible.role: Accessible.Graphic
    Accessible.name: label

    // Teinte opaque : la palette du verre est presque transparente, donc on teinte la base opaque du thème.
    function tone(c: color, a: real): color {
        return Qt.tint(Colours.surfaceBase, Qt.alpha(c, a));
    }
    function clamp(v: real, lo: real, hi: real): real {
        return Math.max(lo, Math.min(hi, v));
    }

    onValueChanged: {
        if (kind === "motion" && animated && Number(value) > 0.01)
            slideAnim.restart();
    }

    Anim {
        id: slideAnim

        target: root
        property: "slide"
        from: -root.width * 0.22
        to: 0
    }

    component Lines: Column {
        id: lines

        property color tint

        spacing: 3

        Repeater {
            model: [0.9, 0.6, 0.78]

            Rectangle {
                required property real modelData

                width: lines.width * modelData
                height: 3
                radius: 1.5
                color: lines.tint
            }
        }
    }

    component Stripes: Row {
        Repeater {
            model: 7

            Rectangle {
                required property int index

                width: parent.width / 7
                height: parent.height
                color: index % 3 === 0 ? root.tone(root.pal.m3primary, 0.55) : index % 3 === 1 ? root.tone(root.pal.m3tertiary, 0.5) : root.tone(root.pal.m3secondary, 0.45)
            }
        }
    }

    component Win: Rectangle {
        id: win

        property bool outlined: false

        radius: Appearance.rounding.tiny * 1.5
        color: root.tone(root.pal.m3onSurface, 0.16)
        border.width: outlined ? 2 : 0
        border.color: root.pal.m3primary

        Behavior on border.width {
            enabled: root.animated

            Anim {
                duration: Appearance.anim.durations.small
            }
        }

        Rectangle {
            x: 4
            y: 4
            width: parent.width - 8
            height: 5
            radius: 2.5
            color: root.tone(root.pal.m3onSurface, 0.3)
        }
        Lines {
            x: 5
            y: 14
            width: parent.width - 10
            tint: root.tone(root.pal.m3onSurface, 0.28)
        }
    }

    Rectangle {
        id: desk

        anchors.fill: parent
        radius: Appearance.rounding.small
        color: root.tone(root.pal.m3onSurface, 0.06)
        border.width: 1
        border.color: Qt.alpha(root.pal.m3outlineVariant, 0.8)

        Rectangle {
            id: stage

            anchors.fill: parent
            anchors.margins: 3
            radius: Math.max(0, Appearance.rounding.small - 3)
            color: root.tone(root.pal.m3primary, 0.08)

            // ---- theme ----
            Rectangle {
                id: themeView

                readonly property string mode: String(root.value)
                readonly property var wall: Bridge.wallpaperInfo?.colors ?? []
                readonly property bool hasWall: mode === "auto" && wall.length > 0
                readonly property color base: mode === "light" ? (Bridge.dark ? root.pal.m3onSurface : Colours.surfaceBase)
                    : mode === "dark" ? (Bridge.dark ? Colours.surfaceBase : root.pal.m3onSurface)
                    : hasWall ? root.tone(wall[0], 0.3) : Colours.surfaceBase
                readonly property color accent: mode === "light" ? (Bridge.dark ? root.pal.m3inversePrimary : root.pal.m3primary)
                    : mode === "dark" ? (Bridge.dark ? root.pal.m3primary : root.pal.m3inversePrimary)
                    : hasWall ? wall[0] : root.pal.m3primary
                readonly property var swatches: hasWall ? wall.slice(0, 4) : [root.pal.m3primary, root.pal.m3secondary, root.pal.m3tertiary]

                visible: root.kind === "theme"
                anchors.fill: parent
                radius: stage.radius
                color: base

                Rectangle {
                    x: 6
                    y: 5
                    width: parent.width - 12
                    height: 8
                    radius: 4
                    color: Qt.alpha(themeView.accent, 0.3)

                    Rectangle {
                        x: 4
                        anchors.verticalCenter: parent.verticalCenter
                        width: 4
                        height: 4
                        radius: 2
                        color: themeView.accent
                    }
                }
                Rectangle {
                    x: 6
                    y: 19
                    width: parent.width * 0.55
                    height: parent.height - 25
                    radius: Appearance.rounding.tiny * 1.5
                    color: Qt.tint(themeView.base, Qt.alpha(themeView.accent, 0.2))

                    Rectangle {
                        x: 5
                        y: 5
                        width: parent.width - 10
                        height: 4
                        radius: 2
                        color: themeView.accent
                    }
                    Lines {
                        x: 5
                        y: 14
                        width: parent.width - 10
                        tint: Qt.alpha(themeView.accent, 0.5)
                    }
                }
                Rectangle {
                    visible: themeView.mode !== "auto"
                    x: parent.width * 0.55 + 12
                    y: 19
                    width: parent.width - x - 6
                    height: parent.height - 25
                    radius: Appearance.rounding.tiny * 1.5
                    color: Qt.tint(themeView.base, Qt.alpha(themeView.accent, 0.1))
                }
                Column {
                    visible: themeView.mode === "auto"
                    x: parent.width * 0.55 + 12
                    y: 19
                    spacing: 4

                    Repeater {
                        model: themeView.swatches

                        Rectangle {
                            required property var modelData

                            width: 9
                            height: 9
                            radius: 4.5
                            color: modelData
                        }
                    }
                }
            }

            // ---- glass : la valeur est l'opacité du panneau en % ----
            Item {
                visible: root.kind === "glass"
                anchors.fill: parent

                Stripes {
                    anchors.fill: parent
                }
                Rectangle {
                    x: parent.width * 0.2
                    y: parent.height * 0.16
                    width: parent.width * 0.6
                    height: parent.height * 0.68
                    radius: Appearance.rounding.smaller
                    color: root.tone(root.pal.m3onSurface, 0.2)
                    border.width: 1
                    border.color: Qt.alpha(root.pal.m3onSurface, 0.35)
                    opacity: root.clamp(Number(root.value) / 100, 0, 1)

                    Rectangle {
                        x: 6
                        y: 6
                        width: parent.width - 12
                        height: 5
                        radius: 2.5
                        color: root.pal.m3primary
                    }
                    Lines {
                        x: 6
                        y: 17
                        width: parent.width - 12
                        tint: root.pal.m3onSurface
                    }
                }
            }

            // ---- motion : traînée statique, un seul glissement quand la valeur change ----
            Item {
                id: motionView

                readonly property real v: Number(root.value)
                readonly property int ghosts: v <= 0.01 ? 0 : v <= 0.75 ? 2 : 4

                visible: root.kind === "motion"
                anchors.fill: parent

                Item {
                    x: root.slide
                    width: parent.width
                    height: parent.height

                    Repeater {
                        model: motionView.ghosts

                        Rectangle {
                            required property int index
                            readonly property int k: index + 1

                            x: parent.width * 0.5 - parent.width * 0.05 * k * (1 + 0.25 * k)
                            y: parent.height * 0.25
                            width: parent.width * 0.38
                            height: parent.height * 0.5
                            radius: Appearance.rounding.smaller
                            color: root.tone(root.pal.m3primary, 0.5)
                            opacity: 0.4 * (1 - index / motionView.ghosts)
                        }
                    }
                    Rectangle {
                        x: parent.width * 0.5
                        y: parent.height * 0.25
                        width: parent.width * 0.38
                        height: parent.height * 0.5
                        radius: Appearance.rounding.smaller
                        color: root.tone(root.pal.m3onSurface, 0.2)
                        border.width: 1
                        border.color: root.pal.m3primary

                        Rectangle {
                            x: 5
                            y: 5
                            width: parent.width - 10
                            height: 4
                            radius: 2
                            color: root.pal.m3primary
                        }
                        Lines {
                            x: 5
                            y: 14
                            width: parent.width - 10
                            tint: Qt.alpha(root.pal.m3onSurface, 0.4)
                        }
                    }
                }
            }

            // ---- reducedEffects : verre et ombre douce quand c'est désactivé, aplat quand c'est activé ----
            Item {
                id: fxView

                visible: root.kind === "reducedEffects"
                anchors.fill: parent

                Stripes {
                    anchors.fill: parent
                    opacity: root.flag ? 0 : 1

                    Behavior on opacity {
                        enabled: root.animated

                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }
                }
                Repeater {
                    model: 3

                    Rectangle {
                        required property int index

                        x: fxView.width * 0.2 - index * 2
                        y: fxView.height * 0.2 + 2 + index * 2
                        width: fxView.width * 0.6 + index * 4
                        height: fxView.height * 0.6 + index * 2
                        radius: Appearance.rounding.smaller + index * 2
                        color: Qt.alpha(root.pal.m3shadow, 0.1)
                        opacity: root.flag ? 0 : 1

                        Behavior on opacity {
                            enabled: root.animated

                            Anim {
                                duration: Appearance.anim.durations.small
                            }
                        }
                    }
                }
                Rectangle {
                    x: fxView.width * 0.2
                    y: fxView.height * 0.2
                    width: fxView.width * 0.6
                    height: fxView.height * 0.6
                    radius: Appearance.rounding.smaller
                    color: Qt.alpha(root.tone(root.pal.m3onSurface, 0.2), root.flag ? 1 : 0.72)
                    border.width: 1
                    border.color: Qt.alpha(root.pal.m3onSurface, root.flag ? 0.12 : 0.35)

                    Behavior on color {
                        enabled: root.animated

                        ColorAnimation {
                            duration: Appearance.anim.durations.small
                        }
                    }

                    Rectangle {
                        x: 6
                        y: 6
                        width: parent.width - 12
                        height: 5
                        radius: 2.5
                        color: root.pal.m3primary
                    }
                    Lines {
                        x: 6
                        y: 17
                        width: parent.width - 12
                        tint: Qt.alpha(root.pal.m3onSurface, 0.4)
                    }
                }
            }

            // ---- accentBorders ----
            Win {
                visible: root.kind === "accentBorders"
                x: parent.width * 0.18
                y: parent.height * 0.14
                width: parent.width * 0.64
                height: parent.height * 0.72
                outlined: root.flag
            }

            // ---- activeBorderOnly : valeur vraie = seule la fenêtre de devant est cerclée ----
            Item {
                visible: root.kind === "activeBorderOnly"
                anchors.fill: parent

                Win {
                    x: parent.width * 0.08
                    y: parent.height * 0.1
                    width: parent.width * 0.52
                    height: parent.height * 0.62
                    outlined: !root.flag
                }
                Win {
                    x: parent.width * 0.38
                    y: parent.height * 0.3
                    width: parent.width * 0.52
                    height: parent.height * 0.6
                    color: root.tone(root.pal.m3onSurface, 0.26)
                    outlined: true
                }
            }

            // ---- screenFrame : value = actif, value2 = épaisseur en px (4..40) ----
            Item {
                visible: root.kind === "screenFrame"
                anchors.fill: parent

                Win {
                    x: parent.width * 0.28
                    y: parent.height * 0.28
                    width: parent.width * 0.44
                    height: parent.height * 0.44
                }
                Rectangle {
                    anchors.fill: parent
                    radius: stage.radius
                    color: "transparent"
                    border.width: root.flag ? 1 + (root.clamp(Number(root.value2), 4, 40) - 4) / 36 * 12 : 0
                    border.color: Qt.alpha(root.pal.m3primary, 0.85)

                    Behavior on border.width {
                        enabled: root.animated

                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }
                }
            }

            // ---- desktopLogo ----
            Item {
                visible: root.kind === "desktopLogo"
                anchors.fill: parent

                Rectangle {
                    anchors.centerIn: parent
                    width: 28
                    height: 28
                    radius: Appearance.rounding.smaller
                    color: root.pal.m3primaryContainer
                    opacity: root.flag ? 1 : 0

                    Behavior on opacity {
                        enabled: root.animated

                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }

                    StyledText {
                        anchors.centerIn: parent
                        text: "W"
                        font.pointSize: Appearance.font.size.large
                        font.weight: Font.Bold
                        color: root.pal.m3onPrimaryContainer
                    }
                }
            }

            // ---- desktopIndicator ----
            Item {
                visible: root.kind === "desktopIndicator"
                anchors.fill: parent

                Rectangle {
                    x: parent.width * 0.25
                    y: parent.height - 16
                    width: parent.width * 0.5
                    height: 12
                    radius: 6
                    color: root.tone(root.pal.m3onSurface, 0.2)
                }
                Row {
                    x: (parent.width - width) / 2
                    y: parent.height - 27
                    spacing: 4
                    opacity: root.flag ? 1 : 0

                    Behavior on opacity {
                        enabled: root.animated

                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }

                    Repeater {
                        model: 4

                        Rectangle {
                            required property int index

                            width: index === 1 ? 12 : 5
                            height: 5
                            radius: 2.5
                            color: index === 1 ? root.pal.m3primary : root.pal.m3outline
                        }
                    }
                }
            }

            // ---- quietFullscreen : vrai = l'indicateur est masqué (barré) pendant la vidéo ----
            Rectangle {
                id: videoView

                visible: root.kind === "quietFullscreen"
                anchors.fill: parent
                radius: stage.radius
                gradient: Gradient {
                    orientation: Gradient.Horizontal

                    GradientStop {
                        position: 0
                        color: root.tone(root.pal.m3tertiary, 0.55)
                    }
                    GradientStop {
                        position: 1
                        color: root.tone(root.pal.m3primary, 0.6)
                    }
                }

                MaterialIcon {
                    anchors.horizontalCenter: parent.horizontalCenter
                    y: parent.height * 0.2
                    text: "play_arrow"
                    color: Qt.alpha(root.pal.m3onSurface, 0.8)
                    font.pointSize: Appearance.font.size.large
                }
                Rectangle {
                    id: pill

                    x: (parent.width - width) / 2
                    y: parent.height - 22
                    width: parent.width * 0.5
                    height: 16
                    radius: 8
                    color: root.pal.m3inverseSurface
                    opacity: root.flag ? 0.25 : 1

                    Behavior on opacity {
                        enabled: root.animated

                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }

                    MaterialIcon {
                        x: 5
                        anchors.verticalCenter: parent.verticalCenter
                        text: "volume_up"
                        color: root.pal.m3inverseOnSurface
                        font.pointSize: Appearance.font.size.small
                    }
                    Rectangle {
                        x: 22
                        anchors.verticalCenter: parent.verticalCenter
                        width: parent.width - 30
                        height: 3
                        radius: 1.5
                        color: root.pal.m3inverseOnSurface
                    }
                }
                Rectangle {
                    x: pill.x - 3
                    y: pill.y + pill.height / 2 - 1
                    width: pill.width + 6
                    height: 2
                    radius: 1
                    rotation: -14
                    color: root.pal.m3error
                    opacity: root.flag ? 1 : 0

                    Behavior on opacity {
                        enabled: root.animated

                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }
                }
            }

            // ---- topActivationMargin : value = marge en px (0..160) ----
            Item {
                id: topView

                readonly property real band: 3 + root.clamp(Number(root.value), 0, 160) / 160 * height * 0.45

                visible: root.kind === "topActivationMargin"
                anchors.fill: parent

                Win {
                    x: parent.width * 0.2
                    y: parent.height * 0.5
                    width: parent.width * 0.6
                    height: parent.height * 0.42
                }
                Rectangle {
                    width: parent.width
                    height: topView.band
                    radius: stage.radius
                    color: Qt.alpha(root.pal.m3primary, 0.28)

                    Behavior on height {
                        enabled: root.animated

                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }

                    Rectangle {
                        anchors.left: parent.left
                        anchors.right: parent.right
                        anchors.bottom: parent.bottom
                        height: 2
                        color: root.pal.m3primary
                    }
                }
                MaterialIcon {
                    x: parent.width * 0.55
                    y: topView.band - 4
                    text: "near_me"
                    color: root.pal.m3onSurface
                    font.pointSize: Appearance.font.size.normal
                }
            }
        }
    }
}
