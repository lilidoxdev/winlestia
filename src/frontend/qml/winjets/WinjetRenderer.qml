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
    // Vrai quand la couche bureau suspend le rendu (plein écran, carte recouverte, session verrouillée).
    property bool paused: false
    // Visible et non suspendu : seul état où un minuteur ou une animation a le droit de tourner.
    readonly property bool live: active && !paused

    // Échelle du contenu : rapport entre la taille réelle et la taille par défaut du manifeste (px), bornée 0,75 à 3.
    readonly property var baseSize: {
        const d = inst && inst.definition && inst.definition.size ? inst.definition.size.default : null;
        return d && d.length >= 2 && d[0] > 0 && d[1] > 0 ? d : [352, 168];
    }
    readonly property real scale: Math.max(0.75, Math.min(3.0, Math.min(width / baseSize[0], height / baseSize[1])))

    // Heure du contexte : à la minute (Time) sauf si un bloc demande les secondes (il appelle tick() chaque seconde).
    property date nowDate: Time.date
    property int secondsUsers: 0
    function tick(): void {
        nowDate = new Date();
    }
    onLiveChanged: if (live)
        tick()
    Connections {
        target: Time

        function onDateChanged(): void {
            if (renderer.secondsUsers === 0)
                renderer.nowDate = Time.date;
        }
    }

    // Sources de contexte optionnelles : calculées seulement si une expression de la mise en page les nomme (`media.` / `system.`)
    // ou si un bloc média les lit lui-même ; sinon aucune liaison ne se réévalue pour elles.
    readonly property string layoutsText: JSON.stringify(inst && inst.layouts ? inst.layouts : [])
    // Un bloc `media` lit Players.active de lui-même : la détection couvre donc la source d'expression et le bloc.
    readonly property bool usesMedia: layoutsText.indexOf("media.") >= 0 || layoutsText.indexOf('"type":"media"') >= 0
    readonly property bool usesSystem: layoutsText.indexOf("system.") >= 0
    // Sources à abonnement (disques 30 s, météo 15 min, fichiers récents) : lues seulement par une carte visible qui les cite.
    readonly property bool usesDisk: layoutsText.indexOf("system.disk") >= 0
    readonly property bool usesCpuHistory: layoutsText.indexOf("system.cpuHistory") >= 0
    readonly property bool usesWeather: layoutsText.indexOf("weather.") >= 0
    // `files.*` et l'action `open` : paquets intégrés (id « winlestia.* ») seulement.
    readonly property bool usesFiles: packageId.indexOf("winlestia.") === 0 && layoutsText.indexOf("files.") >= 0
    readonly property string weatherCity: {
        const city = inst?.settings?.city;
        return String(city !== undefined && city !== null ? city : "").trim();
    }
    readonly property var feeds: Bridge.winjets.feeds
    readonly property var player: Players.active
    readonly property var mediaCtx: {
        const p = usesMedia ? player : null;
        return {
            title: p?.trackTitle ?? "",
            artist: p?.trackArtist ?? "",
            album: p?.trackAlbum ?? "",
            art: p?.trackArtUrl ?? "",
            playing: p?.isPlaying ?? false,
            position: p?.position ?? 0,
            length: p?.length ?? 0,
            canNext: p?.canGoNext ?? false,
            canPrev: p?.canGoPrevious ?? false,
            app: p?.identity ?? ""
        };
    }
    readonly property var systemCtx: {
        if (!usesSystem)
            return {};
        const perf = Bridge.performance ?? {};
        const level = Number(perf.batteryPerc);
        return {
            cpu: Math.round(SystemUsage.cpuPerc * 100),
            ram: Math.round(SystemUsage.memPerc * 100),
            uptime: SysInfo.uptime,
            battery: isFinite(level) && level >= 0 ? level : null,
            charging: perf.batteryCharging === true,
            disk: usesDisk ? feeds.disk : [],
            diskMain: usesDisk ? feeds.diskMain : {},
            cpuHistory: usesCpuHistory ? cpuHistory : []
        };
    }
    readonly property var weatherCtx: usesWeather ? (feeds.revision >= 0 ? feeds.weatherFor(weatherCity) : {}) : {}
    readonly property var filesCtx: usesFiles ? {
        recent: feeds.filesRecent,
        folders: feeds.filesFolders
    } : {}

    // 40 derniers échantillons CPU, accumulés seulement tant qu'une carte visible lit `system.cpuHistory`.
    property var cpuHistory: []
    function sampleCpu(): void {
        const next = cpuHistory.slice(-39);
        next.push(Math.round(SystemUsage.cpuPerc * 100));
        cpuHistory = next;
    }
    Connections {
        target: SystemUsage
        enabled: renderer.usesCpuHistory && renderer.live

        function onCpuPercChanged(): void {
            renderer.sampleCpu();
        }
    }

    // Abonnements aux sources : une clé change quand une carte devient visible/masquée ou que la ville de l'instance change.
    property var heldFeeds: ({
            disk: false,
            files: false,
            cpu: false,
            weather: null
        })
    readonly property string feedKey: [
        usesDisk && live,
        usesFiles && live,
        usesCpuHistory && live,
        usesWeather && live ? "w:" + weatherCity : ""
    ].join("|")
    onFeedKeyChanged: syncFeeds()
    function syncFeeds(): void {
        const want = {
            disk: usesDisk && live,
            files: usesFiles && live,
            cpu: usesCpuHistory && live,
            weather: usesWeather && live ? weatherCity : null
        };
        const have = heldFeeds;
        if (want.disk !== have.disk)
            want.disk ? feeds.hold("disk", "") : feeds.release("disk", "");
        if (want.files !== have.files)
            want.files ? feeds.hold("files", "") : feeds.release("files", "");
        if (want.cpu && !have.cpu)
            sampleCpu();
        if (want.weather !== have.weather) {
            if (have.weather !== null)
                feeds.release("weather", have.weather);
            if (want.weather !== null)
                feeds.hold("weather", want.weather);
        }
        heldFeeds = want;
    }
    // SystemUsage n'échantillonne que s'il a des abonnés : un seul, et seulement tant que le widget le lit et est visible.
    readonly property bool holdsUsage: usesSystem && live
    property bool usageHeld: false
    onHoldsUsageChanged: syncUsage()
    function syncUsage(): void {
        if (holdsUsage === usageHeld)
            return;
        usageHeld = holdsUsage;
        SystemUsage.refCount += holdsUsage ? 1 : -1;
    }
    // Média : le backend ne sonde l'application de lecture que si un consommateur la demande (tableau de bord, centre de
    // contrôle, ou un Winjet de bureau). Le bureau n'a pas de panneau ouvert : la carte prend sa propre référence, sans
    // dépendre de `live` — une carte recouverte par une fenêtre reste à jour pour son retour, et la sonde est partagée.
    readonly property bool holdsMedia: usesMedia
    property bool mediaHeld: false
    onHoldsMediaChanged: syncMedia()
    function syncMedia(): void {
        if (holdsMedia === mediaHeld)
            return;
        mediaHeld = holdsMedia;
        Bridge.mediaRef(holdsMedia);
    }
    Component.onCompleted: {
        syncUsage();
        syncMedia();
        syncFeeds();
    }
    Component.onDestruction: {
        if (heldFeeds.disk)
            feeds.release("disk", "");
        if (heldFeeds.files)
            feeds.release("files", "");
        if (heldFeeds.weather !== null)
            feeds.release("weather", heldFeeds.weather);
        if (usageHeld)
            SystemUsage.refCount--;
        if (mediaHeld)
            Bridge.mediaRef(false);
    }

    // État visible des expressions : persisté + éphémère. La clé déclarée `hover` est masquée par `_hover`
    // (`state.hover` suit le survol éphémère, null hors survol) ; les clés `_x` restent lisibles telles quelles.
    readonly property var mergedState: {
        const out = Object.assign({}, inst.state ?? {}, transientState);
        for (const k in inst.state ?? {})
            if (k === "hover")
                out[k] = transientState._hover ?? null;
        return out;
    }

    readonly property var layouts: inst && inst.layouts ? inst.layouts : []
    readonly property int layoutIndex: Bridge.winjets.layoutIndex(layouts, width, height)
    readonly property var layout: layouts.length > layoutIndex ? layouts[layoutIndex] : null
    readonly property var ctx: ({
            settings: inst.settings ?? {},
            state: mergedState,
            data: inst.data ?? {},
            instance: {
                width: width,
                height: height
            },
            scale: scale,
            paused: paused,
            time: {
                hours: nowDate.getHours(),
                minutes: nowDate.getMinutes(),
                seconds: nowDate.getSeconds()
            },
            now: nowDate,
            media: mediaCtx,
            system: systemCtx,
            weather: weatherCtx,
            files: filesCtx
        })

    // Appelée par les blocs ; jamais de code du paquet, seulement ces actions fermées.
    function runAction(action): void {
        if (!action || editing || !inst.id)
            return;
        switch (action.type ?? action.action) {
        case "setState":
            setState(String(action.key ?? ""), action.value);
            break;
        case "refresh":
            Bridge.winjets.refresh(inst.id);
            break;
        case "openUrl":
            Bridge.winjets.openUrl(inst.packageId, String(action.url ?? ""));
            break;
        case "open":
            feeds.openPathFor(inst.packageId, String(action.path ?? ""));
            break;
        case "openDrive":
            feeds.openDrive(String(action.drive ?? ""));
            break;
        case "openSettings":
            Bridge.winjets.requestSettings(inst.id);
            break;
        case "media.playPause":
            if (player?.canTogglePlaying)
                player.togglePlaying();
            break;
        case "media.next":
            if (player?.canGoNext)
                player.next();
            break;
        case "media.prev":
            if (player?.canGoPrevious)
                player.previous();
            break;
        }
    }

    // État éphémère : clés commençant par `_` (ex. `state._hover`). Vivent ici seulement, jamais envoyées au backend ni
    // enregistrées ; lisibles dans les expressions comme les autres clés d'état.
    property var transientState: ({})

    property var sentState: ({})
    onInstChanged: sentState = ({})

    // Clé `_…` : mémoire locale seulement. Autre clé : persistée une fois par changement de valeur réel.
    function setState(key: string, value): void {
        if (key === "")
            return;
        if (key.charAt(0) === "_") {
            if (transientState[key] === value)
                return;
            const next = Object.assign({}, transientState);
            if (value === null || value === undefined)
                delete next[key];
            else
                next[key] = value;
            transientState = next;
            return;
        }
        if (!inst.id)
            return;
        // Valeur déjà en place, ou déjà envoyée tant que l'état backend de la clé n'a pas bougé depuis l'envoi
        // (s'il a changé — rejet, correction — l'envoi optimiste est périmé et ne masque plus rien).
        const backend = inst.state ? JSON.stringify(inst.state[key]) : undefined;
        const sent = sentState[key];
        const current = sent && sent.base === backend ? sent.value : inst.state ? inst.state[key] : undefined;
        if (current !== undefined && JSON.stringify(current) === JSON.stringify(value))
            return;
        sentState[key] = { value: value, base: backend };
        Bridge.winjets.setState(inst.id, key, value);
    }

    // Clé de dossier du paquet pour les images (`assets/` uniquement, résolu côté pont).
    readonly property string packageId: inst && inst.packageId ? inst.packageId : ""

    BlockNode {
        anchors.fill: parent
        anchors.margins: WinjetTheme.pad * renderer.scale
        spec: renderer.layout ? renderer.layout.root : null
        ctx: renderer.ctx
        host: renderer
    }
}
