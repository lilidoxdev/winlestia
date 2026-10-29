import QtQuick
import Quickshell
import qs.modules.dashboard
import qs.config

Item {
    id: host
    width: 980; height: 620
    function setTab(i) { st.currentTab = i }
    PersistentProperties { id: st; property int currentTab: 0; property date currentDate: new Date(); reloadableId: "dashboardStateHarness" }
    PersistentProperties { id: vis; property bool dashboard: true; reloadableId: "visHarness" }
    Content { id: content; visibilities: vis; state: st; facePicker: null }

    // Walk object tree: count items, visible items, running animations/timers, repeater delegates.
    function walk(o, acc) {
        if (!o) return;
        acc.objs++;
        if (o.running !== undefined && o.running === true) {
            if (o.interval !== undefined) acc.timers++;
            else if (o.duration !== undefined || o.loops !== undefined) acc.anims++;
        }
        if (o.count !== undefined && o.delegate !== undefined && o.model !== undefined) acc.repeaters++, acc.delegates += o.count;
        if (o.width !== undefined && o.opacity !== undefined) { acc.items++; if (o.visible) acc.visItems++; if (o.layer && o.layer.enabled) acc.layers++; }
        const d = o.data;
        if (d) for (let i = 0; i < d.length; i++) walk(d[i], acc);
    }
    function panes(o, out) {
        if (!o) return out;
        if (o.paneIndex !== undefined) { out[o.paneIndex] = o; }
        const d = o.data;
        if (d) for (let i = 0; i < d.length; i++) panes(d[i], out);
        return out;
    }
    function stats(i) {
        const acc = {objs:0, items:0, visItems:0, timers:0, anims:0, repeaters:0, delegates:0, layers:0, loaded:false};
        const p = panes(content, [])[i];
        if (p && p.item) { acc.loaded = true; walk(p.item, acc); }
        return acc;
    }
    function allLoaded() { const p = panes(content, []); return p.map(x => x.active); }
}
