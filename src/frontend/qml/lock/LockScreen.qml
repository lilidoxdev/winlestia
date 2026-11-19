import QtQuick
import QtQuick.Window
import QtQuick.Effects
import qs.components
import qs.services
import qs.config

// Une fenêtre par écran : fond flouté + carte. Port de LockSurface.qml (Caelestia). L'écran principal reçoit le clavier.
Window {
    id: root

    property var modelData
    property bool primary: false
    readonly property bool wantLocked: Bridge.lock.locked ?? false
    readonly property real heightMult: 0.7
    readonly property real ratio: 16 / 9
    readonly property real dimAmount: 0.35
    readonly property int focusRetryMs: 700
    property int screenIndex: 0
    readonly property real cardRadius: Appearance.rounding.large * 1.5
    readonly property int tileSize: lockIcon.implicitHeight + Appearance.padding.large * 4

    signal closed

    title: "Panneaux · Verrouillage " + (modelData?.name ?? "")
    transientParent: null
    flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint
    x: modelData?.x ?? 0
    y: modelData?.y ?? 0
    width: modelData?.width ?? 1
    height: modelData?.height ?? 1
    // Toujours transparente : l'opacité est portée par le fond (opaque à 1), qui se fond vers le bureau réel au déverrouillage.
    color: "transparent"
    visible: true

    property bool acknowledged: false

    function takeFocus(): void {
        if (primary)
            Bridge.focusLockWindow(root);
        requestActivate();
        if (primary)
            content.focusInput();
    }

    // Une seule fois par verrou : le backend relâche le verrou s'il ne reçoit pas cet accusé.
    function acknowledge(): void {
        if (!primary || acknowledged || !visible || !wantLocked)
            return;
        acknowledged = true;
        Bridge.action("lockShown", {});
    }

    onWantLockedChanged: if (!wantLocked && !unlockAnim.running)
        unlockAnim.start()
    onActiveChanged: if (!active && wantLocked)
        Qt.callLater(takeFocus)
    onVisibleChanged: acknowledge()
    Component.onCompleted: {
        Bridge.registerLockWindow(root);
        initAnim.start();
        takeFocus();
        acknowledge();
    }
    Component.onDestruction: Bridge.unregisterLockWindow(root)

    // Tant que l'écran est verrouillé, on reprend le focus si une autre fenêtre l'a volé.
    Timer {
        running: root.primary && root.wantLocked && !root.active
        interval: root.focusRetryMs
        repeat: true
        onTriggered: root.takeFocus()
    }

    SequentialAnimation {
        id: unlockAnim

        ParallelAnimation {
            Anim {
                target: lockContent
                properties: "implicitWidth,implicitHeight"
                to: root.tileSize
            }
            Anim {
                target: lockBg
                property: "radius"
                to: root.tileSize / 4
            }
            Anim {
                target: content
                property: "scale"
                to: 0
            }
            Anim {
                target: content
                property: "opacity"
                to: 0
                duration: Appearance.anim.durations.small
            }
            Anim {
                target: lockIcon
                property: "opacity"
                to: 1
                duration: Appearance.anim.durations.large
            }
        }
        // Inverse de l'engagement : la tuile tourne et rétrécit pendant que le fond flouté se fond vers le bureau réel.
        ParallelAnimation {
            Anim {
                target: lockContent
                property: "scale"
                to: 0
                duration: Appearance.anim.durations.expressiveFastSpatial
                easing.bezierCurve: Appearance.anim.curves.standardAccel
            }
            Anim {
                target: lockContent
                property: "rotation"
                to: 180
                duration: Appearance.anim.durations.expressiveFastSpatial
                easing.bezierCurve: Appearance.anim.curves.standardAccel
            }
            Anim {
                target: lockContent
                property: "opacity"
                to: 0
                duration: Appearance.anim.durations.expressiveFastSpatial
            }
            Anim {
                target: background
                property: "opacity"
                to: 0
                duration: Appearance.anim.durations.large
            }
        }
        ScriptAction {
            script: {
                Bridge.releaseLockShots();
                root.closed();
            }
        }
    }

    ParallelAnimation {
        id: initAnim

        Anim {
            target: background
            property: "opacity"
            to: 1
            duration: Appearance.anim.durations.small
        }
        SequentialAnimation {
            ParallelAnimation {
                Anim {
                    target: lockContent
                    property: "scale"
                    to: 1
                    duration: Appearance.anim.durations.expressiveFastSpatial
                    easing.bezierCurve: Appearance.anim.curves.expressiveFastSpatial
                }
                Anim {
                    target: lockContent
                    property: "rotation"
                    to: 360
                    duration: Appearance.anim.durations.expressiveFastSpatial
                    easing.bezierCurve: Appearance.anim.curves.standardAccel
                }
            }
            ParallelAnimation {
                Anim {
                    target: lockIcon
                    property: "rotation"
                    to: 360
                    easing.bezierCurve: Appearance.anim.curves.standardDecel
                }
                Anim {
                    target: lockIcon
                    property: "opacity"
                    to: 0
                    duration: Appearance.anim.durations.expressiveEffects
                    easing.bezierCurve: Appearance.anim.curves.expressiveEffects
                }
                Anim {
                    target: content
                    property: "opacity"
                    to: 1
                    duration: Appearance.anim.durations.expressiveEffects
                    easing.bezierCurve: Appearance.anim.curves.expressiveEffects
                }
                Anim {
                    target: content
                    property: "scale"
                    to: 1
                }
                Anim {
                    target: lockBg
                    property: "radius"
                    to: root.cardRadius
                }
                Anim {
                    target: lockContent
                    property: "implicitWidth"
                    to: root.height * root.heightMult * root.ratio
                }
                Anim {
                    target: lockContent
                    property: "implicitHeight"
                    to: root.height * root.heightMult
                }
            }
        }
    }

    // Capture du bureau prise à l'engagement du verrou, floutée une fois (image fixe, aucun coût ensuite).
    // Sans capture : fond plein opaque.
    Item {
        id: background

        anchors.fill: parent
        opacity: 0
        layer.enabled: true
        layer.effect: MultiEffect {
            autoPaddingEnabled: false
            blurEnabled: true
            blur: 1
            blurMax: 64
            blurMultiplier: 1
        }

        Rectangle {
            anchors.fill: parent
            color: Colours.solid(Colours.palette.m3surface)
        }
        Image {
            anchors.fill: parent
            source: "image://lockshot/" + root.screenIndex + "?" + Bridge.lockShotSeq
            visible: status === Image.Ready
            fillMode: Image.PreserveAspectCrop
            cache: false
            smooth: true
        }
        Rectangle {
            anchors.fill: parent
            color: Qt.alpha(Colours.palette.m3scrim, root.dimAmount)
        }
    }

    Item {
        id: lockContent

        anchors.centerIn: parent
        implicitWidth: root.tileSize
        implicitHeight: root.tileSize
        visible: root.primary
        rotation: 180
        scale: 0

        StyledRect {
            id: lockBg

            anchors.fill: parent
            color: Colours.solid(Colours.palette.m3surface)
            radius: root.tileSize / 4
        }

        MaterialIcon {
            id: lockIcon

            anchors.centerIn: parent
            text: "lock"
            font.pointSize: Appearance.font.size.extraLarge * 2
            font.weight: Font.Bold
            rotation: 180
        }

        LockContent {
            id: content

            anchors.centerIn: parent
            width: lockContent.implicitWidth - Appearance.padding.large * 2
            height: lockContent.implicitHeight - Appearance.padding.large * 2
            screenHeight: root.height
            opacity: 0
            scale: 0
        }
    }

    // Les événements souris ne traversent jamais la fenêtre ; les touches non gérées sont avalées.
    MouseArea {
        anchors.fill: parent
        z: -1
        hoverEnabled: true
        acceptedButtons: Qt.AllButtons
        onWheel: wheel => wheel.accepted = true
    }
    Item {
        focus: !root.primary
        Keys.onPressed: event => event.accepted = true
    }
}
