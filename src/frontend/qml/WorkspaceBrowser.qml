// Bibliothèque Windows (« Ton espace »), composée à partir de la typographie, des ondulations, des menus et du mouvement d'origine de
// Caelestia.
// Modèle de performance : le catalogue est indexé une fois par changement de catalogue (hors du chemin d'ouverture), le modèle de tuiles
// est un
// ListModel persistant corrigé de façon incrémentale (jamais reconstruit à l'ouverture ni au filtrage), les délégués sont recyclés, et
// chaque animation dépend de la visibilité et de la préférence « Effets allégés ».
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.services
import qs.config

Item {
    id: browser
    objectName: "workspaceBrowser"
    implicitHeight: Math.min(760, Bridge.screenHeight - 90)
    focus: visible
    property string scope: "apps"
    property string category: "Tout"
    readonly property bool dialogOpen: editor.open
    // Le champ de recherche vit dans WorkspaceSearchRow : on l'expose sous l'ancien nom `query`.
    readonly property alias query: searchRow.queryField
    property bool favoritesOnly: false
    property bool recentSort: false
    property bool listMode: false
    property string selectedId: ""
    property string selectedName: ""
    property string selectedCategory: ""
    property bool selectedFavorite: false
    property string selectedKind: "app"
    property bool flatMode: false
    property var collapsed: ({})
    property var flat: []
    property var secInfo: ({})
    property var secOrder: []
    property int cur: -1
    property var lineSigs: []
    // « Effets allégés » : ni rebond, ni décalage, ni zoom ; seulement de courts fondus (les durées sont déjà réduites).
    readonly property bool fx: !Appearance.reducedEffects
    // Progression d'entrée par région (1 = terminée). Pilotée par openAnim seule, qui ne tourne que visible.
    property real pPanel: 1
    property real pSide: 1
    property real pHead: 1
    property real pBody: 1
    property bool bulk: false
    property bool stale: true
    property var shown: []
    property var sigs: ({})
    property Item tipTarget: null
    property string tipText: ""
    function fold(text) {
        return String(text).toLocaleLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "");
    }

    // ---- Données : indexées une fois par changement de catalogue, jamais à l'ouverture ----------------------------
    property var index: []
    property var counts: ({
            apps: 0,
            games: 0,
            projects: 0,
            documents: 0,
            favorites: 0,
            recent: 0,
            all: 0
        })
    function rebuild() {
        stale = false;
        const raw = Bridge.apps || [];
        // Dédoublonnage : chemin cible normalisé, sinon nom + type. On garde l'entrée aux meilleures métadonnées (favori, récent,
        // catégorie).
        const score = e => (e.favorite ? 4 : 0) + (e.recent >= 0 ? 2 : 0) + (e.category ? 1 : 0);
        const seen = new Map(), list = [];
        for (const e of raw) {
            const k = e.target ? "p:"
                + String(e.target).toLowerCase().replace(/\//g, "\\").replace(/\\+$/, "") : "n:" + fold(e.name) + "|" + (e.kind || "app");
            const at = seen.get(k);
            if (at === undefined) {
                seen.set(k, list.length);
                list.push(e);
            } else if (score(e) > score(list[at]))
                list[at] = e;
        }
        const c = {
            apps: 0,
            games: 0,
            projects: 0,
            documents: 0,
            favorites: 0,
            recent: 0,
            all: list.length
        };
        const idx = new Array(list.length);
        for (let i = 0; i < list.length; i++) {
            const e = list[i];
            if (e.board in c)
                c[e.board]++;
            if (e.favorite)
                c.favorites++;
            if (e.recent >= 0)
                c.recent++;
            const nk = fold(e.name);
            idx[i] = {
                entry: e,
                key: nk + " " + fold(e.category || ""),
                nk: nk
            };
        }
        counts = c;
        index = idx;
    }
    readonly property var spaces: [
        {
            id: "apps",
            name: "Applications",
            icon: "apps"
        },
        {
            id: "games",
            name: "Jeux",
            icon: "sports_esports"
        },
        {
            id: "projects",
            name: "Projets",
            icon: "code_blocks"
        },
        {
            id: "documents",
            name: "Documents",
            icon: "folder_open"
        }
    ]
    readonly property var shortcuts: [
        {
            id: "favorites",
            name: "Favoris",
            icon: "star"
        },
        {
            id: "recent",
            name: "Récents",
            icon: "history"
        },
        {
            id: "all",
            name: "Tout afficher",
            icon: "grid_view"
        }
    ]
    readonly property var navItems: spaces.concat(shortcuts)
    readonly property int navIndex: navItems.findIndex(n => n.id === scope)
    function navY(i) {
        return i * 52 + (i >= 4 ? 17 : 0);
    }
    readonly property string title: scope === "documents" ? qsTr("Documents & médias") : scope === "favorites" ? qsTr("Favoris")
        : scope === "recent" ? qsTr("Récemment ouverts") : qsTr(spaces.find(s => s.id === scope)?.name || "Tout ton espace")
    readonly property var base: index.filter(i => {
        const e = i.entry;
        return (scope === "all" || scope === "favorites" || scope === "recent" || e.board === scope) && (!(favoritesOnly
            || scope === "favorites") || e.favorite) && (scope !== "recent" || e.recent >= 0);
    })
    readonly property var categoryCounts: {
        const m = {};
        for (const i of base) {
            const c = i.entry.category;
            if (c)
                m[c] = (m[c] || 0) + 1;
        }
        return m;
    }
    readonly property var categories: ["Tout"].concat(Object.keys(categoryCounts).sort((a, b) => a.localeCompare(b)))
    readonly property var entries: {
        const words = fold(query.text).split(" ").filter(w => w);
        const values = base.filter(i => (category === "Tout" || i.entry.category === category) && words.every(w => i.key.includes(w)));
        if (recentSort || scope === "recent")
            values.sort((a, b) => (a.entry.recent < 0 ? 9999 : a.entry.recent) - (b.entry.recent < 0 ? 9999 : b.entry.recent));
        else
            values.sort((a, b) => a.nk < b.nk ? -1 : a.nk > b.nk ? 1 : 0);
        return values.map(i => i.entry);
    }
    onEntriesChanged: syncModel()
    onFlatModeChanged: syncModel()
    readonly property int tileCount: entries.length
    function isCollapsed(key) {
        return query.text ? false : (key in collapsed ? collapsed[key] : key === "tools");
    }
    function toggleSection(key) {
        const c = Object.assign({}, collapsed);
        c[key] = !isCollapsed(key);
        collapsed = c;
        syncModel();
    }
    function isTool(e) {
        return fold(e.category || "").startsWith("windows et peri");
    }

    // ---- Sections : Favoris, Récents, catégories A-Z, Dossiers, Outils Windows (ou lettres en mode plat A-Z) ----
    function buildSections() {
        const list = entries, out = [];
        if (flatMode) {
            const m = new Map();
            for (const e of list) {
                const f = fold(e.name).charAt(0).toUpperCase();
                const k = /[A-Z]/.test(f) ? f : "#";
                if (!m.has(k))
                    m.set(k, []);
                m.get(k).push(e);
            }
            for (const k of Array.from(m.keys()).sort((a, b) => a === "#" ? -1 : b === "#" ? 1 : a < b ? -1 : 1))
                out.push({
                    key: "l:" + k,
                    label: k,
                    icon: "",
                    items: m.get(k)
                });
            return out;
        }
        if (scope === "recent") {
            out.push({
                key: "rec",
                label: qsTr("Récents"),
                icon: "history",
                items: list
            });
            return out;
        }
        const byName = (a, b) => fold(a.name) < fold(b.name) ? -1 : fold(a.name) > fold(b.name) ? 1 : 0;
        const top = category === "Tout" && !query.text && scope !== "favorites";
        if (top) {
            const fav = list.filter(e => e.favorite);
            if (fav.length)
                out.push({
                    key: "fav",
                    label: qsTr("Favoris"),
                    icon: "star",
                    items: fav
                });
            const rec = list.filter(e => e.recent >= 0).sort((a, b) => a.recent - b.recent).slice(0, grid.columns);
            if (rec.length)
                out.push({
                    key: "rec",
                    label: qsTr("Récents"),
                    icon: "history",
                    items: rec
                });
        }
        const cats = new Map(), folders = [], tools = [];
        for (const e of list) {
            if (isTool(e))
                tools.push(e);
            else if (e.kind === "folder")
                folders.push(e);
            else {
                const c = e.category || qsTr("Autres");
                if (!cats.has(c))
                    cats.set(c, []);
                cats.get(c).push(e);
            }
        }
        for (const c of Array.from(cats.keys()).sort((a, b) => a.localeCompare(b)))
            out.push({
                key: "c:" + c,
                label: c,
                icon: "label",
                items: cats.get(c).sort(byName)
            });
        if (folders.length)
            out.push({
                key: "folders",
                label: qsTr("Dossiers et projets"),
                icon: "folder",
                items: folders.sort(byName)
            });
        if (tools.length)
            out.push({
                key: "tools",
                label: qsTr("Outils Windows"),
                icon: "settings",
                items: tools.sort(byName)
            });
        return out;
    }

    // ---- Modèle de lignes : une par ligne de tuiles (ou une souche de hauteur nulle pour une section repliée), corrigé sur place ----
    ListModel {
        id: rows
    }
    function syncModel() {
        const cols = Math.max(1, grid.columns);
        const secs = buildSections(), flatNew = [], lines = [], info = {};
        for (const sec of secs) {
            const closed = isCollapsed(sec.key);
            info[sec.key] = {
                label: sec.label,
                count: sec.items.length,
                icon: sec.icon,
                closed: closed
            };
            if (closed) {
                lines.push({
                    sec: sec.key,
                    first: 0,
                    n: 0
                });
                continue;
            }
            for (let i = 0; i < sec.items.length; i += cols) {
                lines.push({
                    sec: sec.key,
                    first: flatNew.length,
                    n: Math.min(cols, sec.items.length - i)
                });
                for (let j = i; j < Math.min(i + cols, sec.items.length); j++)
                    flatNew.push(sec.items[j]);
            }
        }
        flat = flatNew;
        secInfo = info;
        secOrder = secs.map(x => x.key);
        const sigs = lines.map(l => l.sec + "|" + l.first + "|" + l.n + "|" + l.n);
        for (let i = 0; i < lines.length; i++) {
            if (i >= rows.count)
                rows.append(lines[i]);
            else if (lineSigs[i] !== sigs[i])
                rows.set(i, lines[i]);
        }
        if (rows.count > lines.length)
            rows.remove(lines.length, rows.count - lines.length);
        lineSigs = sigs;
        if (cur >= flatNew.length)
            cur = -1;
    }
    function lineOfTile(t) {
        for (let i = 0; i < lineSigs.length; i++) {
            const p = lineSigs[i].split("|");
            const f = +p[1], n = +p[2];
            if (n > 0 && t >= f && t < f + n)
                return i;
        }
        return 0;
    }
    function markBulk() {
        bulk = true;
        bulkTimer.restart();
    }
    function replay() {
        if (fx && visible)
            revealAnim.restart();
    }
    function selectBoard(board) {
        markBulk();
        scope = board;
        category = "Tout";
        query.text = "";
        favoritesOnly = false;
        grid.positionViewAtBeginning();
        replay();
    }
    function hoverTip(target, text) {
        tipTarget = target;
        tipText = text || "";
        tip.shown = false;
        if (target)
            tipTimer.restart();
        else
            tipTimer.stop();
    }
    function focusSearch() {
        query.forceActiveFocus();
        query.selectAll();
    }

    Component.onCompleted: {
        if (visible)
            rebuild();
        else
            firstBuild.start();
    }
    // Préchargement unique une fois le shell inactif ; les changements de catalogue suivants sont temporisés et appliqués aussi en étant
    // masqué.
    Timer {
        id: firstBuild
        interval: 600
        onTriggered: if (browser.stale)
            browser.rebuild()
    }
    Timer {
        id: catalogTimer
        onTriggered: browser.rebuild()
    }
    Timer {
        id: bulkTimer
        interval: 80
        onTriggered: browser.bulk = false
    }
    Timer {
        id: tipTimer
        interval: 650
        onTriggered: {
            if (!browser.tipTarget || !browser.visible)
                return;
            tip.label.text = browser.tipText;
            const p = browser.tipTarget.mapToItem(browser, browser.tipTarget.width / 2, browser.tipTarget.height - 4);
            tip.x = Math.max(8, Math.min(browser.width - tip.width - 8, p.x - tip.width / 2));
            tip.y = Math.max(8, p.y);
            tip.shown = true;
        }
    }
    Connections {
        target: Bridge
        function onCatalogChanged() {
            browser.stale = true;
            catalogTimer.interval = browser.visible ? 60 : 1500;
            catalogTimer.restart();
        }
    }
    Connections {
        target: Bridge
        function onNavigationRequested(mode) {
            if (mode.startsWith("workspace:"))
                browser.selectBoard(mode.split(":")[1]);
        }
    }

    // ---- Ouverture : zoom + fondu avec léger dépassement, les régions entrent en décalé. S'arrête une fois masqué. ----
    WorkspaceOpenAnim {
        id: openAnim
        browser: browser
        grid: grid
    }
    NumberAnimation {
        id: revealAnim
        target: grid
        property: "reveal"
        from: 0
        to: 1
        duration: 900
    }
    onVisibleChanged: {
        if (visible) {
            if (stale)
                rebuild();
            catalogTimer.interval = 60;
            openAnim.restart();
            forceActiveFocus();
        } else {
            context.close();
            openAnim.stop();
            revealAnim.stop();
            tipTimer.stop();
            tip.shown = false;
            query.focus = false;
            grid.focus = false;
            pPanel = 1;
            pSide = 1;
            pHead = 1;
            pBody = 1;
            grid.reveal = 1;
        }
    }
    Keys.onPressed: event => {
        if (query.activeFocus || editor.open)
            return;
        if (event.key === Qt.Key_Down || event.key === Qt.Key_Right || event.key === Qt.Key_Tab) {
            grid.forceActiveFocus();
            if (browser.cur < 0)
                browser.cur = 0;
            event.accepted = true;
        } else if (event.text.length > 0 && !(event.modifiers & (Qt.ControlModifier | Qt.AltModifier)) && event.text.charCodeAt(0) >= 32) {
            query.forceActiveFocus();
            query.text += event.text;
            query.cursorPosition = query.text.length;
            event.accepted = true;
        }
    }
    Shortcut {
        sequence: "Ctrl+F"
        enabled: browser.visible && !editor.open
        onActivated: browser.focusSearch()
    }


    // Menu du clic droit : surcouche simple dans le panneau, entièrement impérative. Cause de « ne marche qu'une fois » : Menu.qml met
    // `expanded=false` lui-même quand un élément est choisi, ce qui détruisait une liaison `expanded:context.opened`, et le menu
    // restait à hauteur 0 / opacité 0. Ne jamais lier `expanded` ; openMenu/closeMenu gèrent tout l'état.
    WorkspaceContextMenu {
        id: context
        browser: browser
        editor: editor
        nameField: nameField
        categoryField: categoryField
    }


    Item {
        id: panel
        anchors.fill: parent
        transformOrigin: Item.Center
        opacity: browser.fx ? Math.max(0, Math.min(1, browser.pPanel * 1.5)) : browser.pPanel
        scale: browser.fx ? .94 + .06 * browser.pPanel : 1
        RowLayout {
            anchors.fill: parent
            spacing: Appearance.spacing.large
            WorkspaceSidebar {
                id: side
                browser: browser
            }
            ColumnLayout {
                Layout.fillWidth: true
                Layout.fillHeight: true
                spacing: Appearance.spacing.larger
                WorkspaceHeader {
                    browser: browser
                }
                WorkspaceSearchRow {
                    id: searchRow
                    browser: browser
                    grid: grid
                }
                // Les catégories passent à la ligne au lieu d'être rognées ; la rangée se replie sur une ligne par défaut.
                WorkspaceChips {
                    id: chipsBox
                    browser: browser
                }
                WorkspaceTileList {
                    id: grid
                    browser: browser
                    context: context
                    query: browser.query
                    rows: rows
                }
                RowLayout {
                    Layout.fillWidth: true
                    opacity: browser.pBody
                    StyledText {
                        text: qsTr("Tes fichiers restent à leur emplacement d’origine")
                        font.pointSize: Appearance.font.size.small
                        color: Colours.palette.m3outline
                        Layout.fillWidth: true
                    }
                    IconTextButton {
                        text: qsTr("Tableau de bord")
                        icon: "dashboard"
                        type: IconTextButton.Text
                        onClicked: Bridge.navigate("dashboard")
                    }
                }
            }
        }
    }
    // Une seule info-bulle partagée au lieu d'une Popup par tuile.
    WorkspaceTip {
        id: tip
        browser: browser
    }
    Dialog {
        id: editor
        title: qsTr("Personnaliser cet accès")
        cardWidth: 430
        StyledTextField {
            id: nameField
            Layout.fillWidth: true
            placeholderText: qsTr("Nom")
        }
        StyledTextField {
            id: categoryField
            Layout.fillWidth: true
            placeholderText: qsTr("Catégorie")
        }
        actions: [
            Item {
                Layout.fillWidth: true
            },
            TextButton {
                type: TextButton.Text
                text: qsTr("Annuler")
                onClicked: editor.close()
            },
            TextButton {
                type: TextButton.Filled
                text: qsTr("Enregistrer")
                onClicked: {
                    Bridge.action("editEntry", {
                        id: browser.selectedId,
                        name: nameField.text,
                        category: categoryField.text
                    });
                    editor.close();
                }
            }
        ]
    }
}
