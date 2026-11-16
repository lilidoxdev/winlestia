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

    ColumnLayout {
        Layout.fillWidth: true
        Layout.fillHeight: true
        spacing: Appearance.spacing.normal

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
    }

    ColumnLayout {
        Layout.fillWidth: true
        Layout.fillHeight: true
        spacing: Appearance.spacing.normal

        LockResources {
            Layout.fillWidth: true
        }
        LockNotifs {
            Layout.fillWidth: true
            Layout.fillHeight: true
        }
    }
}
