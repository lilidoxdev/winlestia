import QtQuick
import qs.components
import qs.services
import qs.config

// Mauvais mot de passe : texte m3error, apparition puis deux clignotements ; affiche aussi l'attente quand les essais sont bloqués.
Item {
    id: root

    readonly property var lockState: Bridge.lock
    readonly property bool locked: lockState.locked ?? false
    readonly property int retryAfter: lockState.retryAfter ?? 0
    readonly property int failures: lockState.failures ?? 0
    readonly property int flashes: 2
    readonly property real popFrom: 0.7
    readonly property bool waiting: remaining > 0
    property int seenFailures: 0
    property real deadline: 0
    property int remaining: 0
    property bool wrong: false
    property string message: ""

    function refresh(): void {
        remaining = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
        const lines = [];
        if (wrong)
            lines.push(qsTr("Mot de passe incorrect"));
        if (remaining > 0)
            lines.push(qsTr("Réessayez dans %1 s").arg(remaining));
        message = lines.join("
");
    }

    function startCountdown(): void {
        deadline = retryAfter > 0 ? Date.now() + retryAfter * 1000 : 0;
    }

    function tick(): void {
        refresh();
        if (remaining === 0 && deadline > 0) {
            deadline = 0;
            Bridge.action("state", {});
        }
    }

    // Chaque nouvel échec (même délai que le précédent) relance le décompte et le clignotement.
    onLockStateChanged: {
        if (!locked) {
            seenFailures = 0;
            wrong = false;
            deadline = 0;
            refresh();
            return;
        }
        if (failures > seenFailures) {
            wrong = true;
            startCountdown();
            refresh();
            flashAnim.restart();
        } else if (remaining === 0 && retryAfter > 0) {
            startCountdown();
            refresh();
        }
        seenFailures = failures;
    }
    Component.onCompleted: seenFailures = failures

    implicitHeight: label.implicitHeight

    // Seulement tant qu'une attente est en cours et verrouillé.
    Timer {
        running: root.locked && root.waiting
        interval: 1000
        repeat: true
        onTriggered: root.tick()
    }

    StyledText {
        id: label

        anchors.horizontalCenter: parent.horizontalCenter
        width: parent.width
        horizontalAlignment: Text.AlignHCenter
        wrapMode: Text.WordWrap
        text: root.message
        color: Colours.palette.m3error
        font.pointSize: Appearance.font.size.larger
        opacity: 0
    }

    SequentialAnimation {
        id: flashAnim

        ParallelAnimation {
            Anim {
                target: label
                property: "scale"
                from: root.popFrom
                to: 1
                duration: Appearance.anim.durations.expressiveFastSpatial
                easing.bezierCurve: Appearance.anim.curves.expressiveFastSpatial
            }
            Anim {
                target: label
                property: "opacity"
                from: 0
                to: 1
                duration: Appearance.anim.durations.expressiveFastEffects
            }
        }
        SequentialAnimation {
            loops: root.flashes

            Anim {
                target: label
                property: "opacity"
                to: 0.3
                duration: Appearance.anim.durations.small
            }
            Anim {
                target: label
                property: "opacity"
                to: 1
                duration: Appearance.anim.durations.small
            }
        }
    }
}
