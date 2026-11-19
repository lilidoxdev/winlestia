import QtQuick
import qs.components.controls
import qs.config
import qs.services
import "DockItems.js" as DockItems

// Bouton personnalisable du dock (barre ou tiroir), identifié par son id du catalogue DockItems.js.
IconButton {
    id: slot

    required property string itemId

    // Un menu du dock (running, power) doit s'ouvrir ancré sur ce bouton.
    signal menuRequested(string name)

    readonly property string kind: DockItems.kind(itemId)
    readonly property var quick: (Bridge.quick ?? {})[itemId]
    readonly property bool available: kind !== "toggle" || (quick?.available ?? false)
    readonly property bool on: {
        if (itemId === "autoTile")
            return Bridge.settings.autoTile ?? false;
        return kind === "toggle" ? (quick?.on ?? false) : false;
    }
    readonly property string title: qsTr(DockItems.label(itemId))
    readonly property string hint: {
        if (itemId === "autoTile") {
            if (!on)
                return qsTr("Organisation automatique : désactivée");
            return qsTr("Organisation automatique : activée") + "
" + qsTr("Ctrl+Alt+flèches : changer de fenêtre");
        }
        if (kind === "toggle") {
            if (!available)
                return title + " : " + qsTr("indisponible");
            return title + " : " + (on ? qsTr("activé") : qsTr("désactivé"));
        }
        return kind === "shortcut" ? title : "";
    }

    objectName: {
        if (itemId === "autoTile")
            return "dockAutoTile";
        if (itemId === "running")
            return "dockRunning";
        return itemId === "network" ? "dockNetwork" : "dock-" + itemId;
    }
    icon: {
        if (itemId !== "network")
            return DockItems.icon(itemId);
        if (Bridge.networkKind === "ethernet")
            return "lan";
        return Bridge.networkKind === "none" ? "wifi_off" : "wifi";
    }
    type: IconButton.Text
    checked: on
    color: on ? Colours.palette.m3secondaryContainer : "transparent"
    disabled: !available
    opacity: available ? 1 : 0.38
    Accessible.name: itemId === "autoTile" ? qsTr("Organisation automatique") : title
    Accessible.checked: on

    onClicked: {
        if (itemId === "autoTile")
            Bridge.action("preference", {
                key: "autoTile",
                enabled: !on
            });
        else if (kind === "toggle" || kind === "shortcut")
            Bridge.action("quickToggle", {
                id: itemId
            });
        else if (itemId === "running" || itemId === "power")
            menuRequested(itemId);
        else if (itemId === "volume" || itemId === "network")
            Bridge.navigate("controls");
        else if (itemId === "settings")
            Bridge.action("settings", {});
    }

    HoverHandler {
        id: hover
    }

    ShellToolTip {
        visible: hover.hovered && slot.hint !== ""
        delay: 400
        text: slot.hint
    }
}
