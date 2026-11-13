import QtQuick
import qs.config
import qs.services
import ".."

// Bloc `visualizer` : barres arrondies m3primary alignées en bas, niveaux de Bridge.audioBars (0-100) ou, à défaut, de
// Bridge.spectrum (0-1), rééchantillonnés sur `bars` (8 à 64, 24 par défaut). Hors lecture, masqué ou suspendu : barres plates au
// repos, flux libéré, aucun minuteur. Aucune animation : la fluidité vient du rythme des mises à jour du spectre.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property int count: {
        const v = spec ? Number(Bridge.winjets.resolve(spec.bars, ctx)) : NaN;
        return isFinite(v) ? Math.max(8, Math.min(64, Math.round(v))) : 24;
    }
    readonly property bool wanted: visible && (!host || (host.live && !host.editing)) && !Appearance.reducedEffects && (Players.active?.isPlaying ?? false)
    // Vrai tant que ce bloc détient une référence sur le flux audio.
    property bool subscribed: false
    readonly property real rest: Math.max(2, 3 * sc)
    readonly property real gap: Math.max(1, 2 * sc)
    readonly property real barWidth: Math.max(1, (width - gap * (count - 1)) / count)

    readonly property var levels: {
        if (!wanted)
            return [];
        const bars = Bridge.audioBars;
        const fromBars = bars && bars.length > 0;
        const raw = fromBars ? bars : Bridge.spectrum;
        const scale = fromBars ? 100 : 1;
        const n = raw ? raw.length : 0;
        const out = [];
        for (let i = 0; i < count; i++) {
            const a = n > 0 ? Math.floor(i * n / count) : 0;
            const b = Math.max(a + 1, Math.floor((i + 1) * n / count));
            let peak = 0;
            for (let j = a; j < b && j < n; j++)
                peak = Math.max(peak, Number(raw[j]) || 0);
            out.push(Math.max(0, Math.min(1, peak / scale)));
        }
        return out;
    }

    function syncSubscription(): void {
        if (wanted === subscribed)
            return;
        subscribed = wanted;
        wanted ? WinjetAudio.acquire() : WinjetAudio.release();
    }

    onWantedChanged: syncSubscription()
    Component.onCompleted: syncSubscription()
    Component.onDestruction: {
        if (subscribed)
            WinjetAudio.release();
    }

    implicitWidth: 200 * sc
    implicitHeight: 56 * sc

    Repeater {
        model: block.count

        Rectangle {
            required property int index

            x: index * (block.barWidth + block.gap)
            width: block.barWidth
            height: Math.max(block.rest, (block.levels[index] ?? 0) * block.height)
            y: block.height - height
            radius: Math.min(width / 2, Appearance.rounding.tiny * block.sc)
            color: WinjetTheme.accent
        }
    }
}
