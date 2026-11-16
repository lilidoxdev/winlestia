import QtQuick
import QtQuick.Layouts
import qs.components
import qs.config

// Trois colonnes : météo / fetch / média, centre (horloge, avatar, mot de passe), ressources / notifications.
RowLayout {
    id: root

    property real screenHeight: 1440
    readonly property real centerWidthBase: 600
    readonly property real centerScale: Math.min(1, screenHeight / 1440)

    function focusInput(): void {
        center.focusInput();
    }

    spacing: Appearance.spacing.large * 2

    // Entrée échelonnée après la métamorphose de la carte : colonne gauche, centre, colonne droite (0 → 3).
    readonly property real slideDistance: Appearance.padding.large * 2
    property real stage: 0
    function reveal(i: int): real {
        return Math.max(0, Math.min(1, stage - i));
    }

    SequentialAnimation {
        running: true

        PauseAnimation {
            duration: Appearance.anim.durations.expressiveFastSpatial
        }
        NumberAnimation {
            target: root
            property: "stage"
            to: 3
            duration: Appearance.anim.durations.expressiveEffects * 2
            easing.type: Easing.OutCubic
        }
    }

    ColumnLayout {
        Layout.fillWidth: true
        Layout.fillHeight: true
        spacing: Appearance.spacing.normal
        opacity: root.reveal(0)
        transform: Translate {
            y: (1 - root.reveal(0)) * root.slideDistance
        }

        LockWeather {
            Layout.fillWidth: true
        }
        LockFetch {
            Layout.fillWidth: true
            Layout.fillHeight: true
        }
        LockMedia {
            Layout.fillWidth: true
        }
    }

    LockCenter {
        id: center

        Layout.preferredWidth: root.centerWidthBase * root.centerScale
        Layout.fillWidth: false
        Layout.fillHeight: true
        centerScale: root.centerScale
        opacity: root.reveal(1)
        transform: Translate {
            y: (1 - root.reveal(1)) * root.slideDistance
        }
    }

    ColumnLayout {
        Layout.fillWidth: true
        Layout.fillHeight: true
        spacing: Appearance.spacing.normal
        opacity: root.reveal(2)
        transform: Translate {
            y: (1 - root.reveal(2)) * root.slideDistance
        }

        LockResources {
            Layout.fillWidth: true
        }
        LockNotifs {
            Layout.fillWidth: true
            Layout.fillHeight: true
        }
    }
}
