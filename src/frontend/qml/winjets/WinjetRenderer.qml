import QtQuick
import qs.services

// Interprète la définition d'un Winjet (données seules) : choisit la mise en page selon la taille réelle, recalculée à chaque
// redimensionnement, puis déroule l'arbre de blocs. Sert aussi d'hôte (`host`) aux blocs : actions et état d'instance.
Item {
    id: renderer

    // Instantané renvoyé par Bridge.winjets.instance(id) : layouts, settings, state, data, id, packageId.
    property var inst: ({})
    property bool editing: false
    // Faux quand la carte n'est pas visible : les motifs animés s'arrêtent.
    property bool active: true

    readonly property var layouts: inst && inst.layouts ? inst.layouts : []
    readonly property int layoutIndex: Bridge.winjets.layoutIndex(layouts, width, height)
    readonly property var layout: layouts.length > layoutIndex ? layouts[layoutIndex] : null
    readonly property var ctx: ({
            settings: inst.settings ?? {},
            state: inst.state ?? {},
            data: inst.data ?? {},
            instance: {
                width: width,
                height: height
            },
            now: Time.date
        })

    // Appelée par les blocs ; jamais de code du paquet, seulement ces trois actions fermées.
    function runAction(action): void {
        if (!action || editing || !inst.id)
            return;
        switch (action.type) {
        case "setState":
            setState(String(action.key ?? ""), action.value);
            break;
        case "refresh":
            Bridge.winjets.refresh(inst.id);
            break;
        case "openUrl":
            Bridge.winjets.openUrl(inst.packageId, String(action.url ?? ""));
            break;
        case "openSettings":
            Bridge.winjets.requestSettings(inst.id);
            break;
        }
    }

    function setState(key: string, value): void {
        if (key !== "" && inst.id)
            Bridge.winjets.setState(inst.id, key, value);
    }

    // Clé de dossier du paquet pour les images (`assets/` uniquement, résolu côté pont).
    readonly property string packageId: inst && inst.packageId ? inst.packageId : ""

    BlockNode {
        anchors.fill: parent
        anchors.margins: WinjetTheme.pad
        spec: renderer.layout ? renderer.layout.root : null
        ctx: renderer.ctx
        host: renderer
    }
}
