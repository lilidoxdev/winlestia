// Pastille de batterie des périphériques Bluetooth/XInput connectés (casque, manette, souris, clavier) à côté de l'horloge du dock.
// Un groupe par appareil visible : icône du type, jauge dessinée, pourcentage ; séparateur fin entre les groupes.
// Données : Bridge.peripherals.devices ; masquée si le réglage est coupé ou s'il n'y a aucun appareil visible. Clic = panneau Contrôles
// (carte Bluetooth), comme le bouton réseau du dock. Aucune animation ni minuteur : tout se redessine à l'état reçu.
import QtQuick
import qs.components
import qs.components.controls
import qs.config
import qs.services
import "DockItems.js" as DockItems

Item {
    id: root
    objectName: "dockPeripherals"

    // Seuil de batterie faible (couleur d'erreur), hors charge.
    readonly property int lowLevel: 20
    readonly property bool enabledSetting: Bridge.settings?.peripheralBattery ?? Bridge.peripherals?.enabled ?? false
    readonly property var devices: {
        // Les listes d'un QVariantMap arrivent comme des séquences Qt, pas comme des Array : Array.from les normalise.
        const all = Bridge.peripherals?.devices;
        return all && typeof all.length === "number" ? Array.from(all).filter(d => d && !d.hidden && root.knownPercent(d.percent)) : [];
    }
    readonly property bool shown: enabledSetting && devices.length > 0

    function isLow(d): bool {
        return d.percent <= lowLevel && d.charging !== true;
    }
    // Une valeur hors 0..100 (sentinelle, octet mal décodé) vaut « inconnu » : l'appareil n'est pas affiché.
    function knownPercent(p): bool {
        return typeof p === "number" && isFinite(p) && p >= 0 && p <= 100;
    }
    // Pas de préfixe « ~ » : dans la police du dock il se lisait comme un signe moins (« -75 % »).
    // Le caractère approximatif est donné par l'infobulle.
    function percentText(d): string {
        return Math.round(d.percent) + " %";
    }
    function hintText(d): string {
        const state = d.charging === true ? qsTr("en charge") : d.charging === false ? qsTr("sur batterie") :
            qsTr("état de charge inconnu");
        return (d.kind === "laptop" ? qsTr("Batterie de l'ordinateur") : (d.name ?? "")) + " — " + Math.round(d.percent) + " % — " + state +
            (d.approximate ? "\n" + qsTr("niveau approximatif") : "");
    }

    visible: shown
    implicitWidth: shown ? groups.implicitWidth + Appearance.padding.normal * 2 : 0
    implicitHeight: 32
    Accessible.name: qsTr("Batterie des périphériques")

    StyledRect {
        anchors.fill: parent
        radius: Appearance.rounding.full
        color: Qt.alpha(Colours.palette.m3secondaryContainer, 1)
    }

    Row {
        id: groups
        anchors.centerIn: parent
        spacing: Appearance.spacing.small

        Repeater {
            model: root.shown ? root.devices : []

            Row {
                id: group
                required property var modelData
                required property int index
                readonly property bool low: root.isLow(modelData)
                objectName: "dockPeripheralGroup"
                spacing: Appearance.spacing.small
                height: root.implicitHeight

                Rectangle {
                    visible: group.index > 0
                    anchors.verticalCenter: parent.verticalCenter
                    width: 1
                    height: 16
                    color: Colours.palette.m3outlineVariant
                }

                MaterialIcon {
                    anchors.verticalCenter: parent.verticalCenter
                    text: DockItems.peripheralIcon(group.modelData.kind)
                    font.pointSize: Appearance.font.size.normal
                    color: Colours.palette.m3onSecondaryContainer
                }

                // Jauge : contour, remplissage proportionnel, bout de borne.
                Item {
                    objectName: "dockPeripheralGauge"
                    anchors.verticalCenter: parent.verticalCenter
                    width: 22
                    height: 11
                    Rectangle {
                        id: body
                        width: 20
                        height: parent.height
                        radius: 3
                        color: "transparent"
                        border.width: 1
                        border.color: Colours.palette.m3onSurfaceVariant
                        Rectangle {
                            objectName: "dockPeripheralFill"
                            x: 2
                            y: 2
                            height: body.height - 4
                            width: Math.max(group.modelData.percent > 0 ? 1 : 0,
                                Math.round((body.width - 4) * Math.min(100, group.modelData.percent) / 100))
                            radius: 1
                            color: group.low ? Colours.palette.m3error : Colours.palette.m3primary
                        }
                    }
                    Rectangle {
                        anchors.left: body.right
                        anchors.verticalCenter: parent.verticalCenter
                        width: 2
                        height: 4
                        radius: 1
                        color: Colours.palette.m3onSurfaceVariant
                    }
                }

                MaterialIcon {
                    visible: group.modelData.charging === true
                    anchors.verticalCenter: parent.verticalCenter
                    text: "bolt"
                    fill: 1
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3primary
                }

                StyledText {
                    objectName: "dockPeripheralText"
                    anchors.verticalCenter: parent.verticalCenter
                    text: root.percentText(group.modelData)
                    font.pointSize: Appearance.font.size.small
                    color: group.low ? Colours.palette.m3error : Colours.palette.m3onSecondaryContainer
                }

                HoverHandler {
                    id: hover
                    cursorShape: Qt.PointingHandCursor
                }
                TapHandler {
                    onTapped: Bridge.navigate("controls")
                }
                ShellToolTip {
                    visible: hover.hovered
                    delay: 400
                    text: root.hintText(group.modelData)
                }
            }
        }
    }
}
