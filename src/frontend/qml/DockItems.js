.pragma library
// Dock : catalogue des boutons personnalisables et calculs purs sur les deux listes ordonnées (barre, tiroir).
// Les libellés sont du texte source français : l'appelant les traduit avec qsTr(label(id)).

// kind : launcher (place fixe à gauche, barre ou masqué), tool, toggle (bascule rapide), shortcut (ouvre Windows).
var ITEMS = {
    start: {icon: "apps", label: "Menu démarrer", kind: "launcher"},
    recycle: {icon: "delete", label: "Corbeille", kind: "launcher"},
    files: {icon: "folder", label: "Fichiers", kind: "launcher"},
    search: {icon: "search", label: "Recherche", kind: "launcher"},
    autoTile: {icon: "grid_view", label: "Organisation automatique", kind: "tool"},
    running: {icon: "apps", label: "Applications ouvertes", kind: "tool"},
    volume: {icon: "volume_up", label: "Volume et luminosité", kind: "tool"},
    network: {icon: "wifi", label: "Réseau", kind: "tool"},
    settings: {icon: "settings", label: "Paramètres", kind: "tool"},
    power: {icon: "power_settings_new", label: "Alimentation", kind: "tool"},
    wifi: {icon: "wifi", label: "Wi-Fi", kind: "toggle"},
    bluetooth: {icon: "bluetooth", label: "Bluetooth", kind: "toggle"},
    airplane: {icon: "flight", label: "Mode avion", kind: "toggle"},
    micMute: {icon: "mic_off", label: "Couper le micro", kind: "toggle"},
    keepAwake: {icon: "coffee", label: "Garder éveillé", kind: "toggle"},
    nightLight: {icon: "nightlight", label: "Éclairage nocturne", kind: "shortcut"},
    focus: {icon: "do_not_disturb_on", label: "Ne pas déranger", kind: "shortcut"}
};

var ORDER = ["start", "recycle", "files", "search", "autoTile", "running", "volume", "network", "settings", "power",
    "wifi", "bluetooth", "airplane", "micMute", "keepAwake", "nightLight", "focus"];

var DEFAULT_BAR = ["start", "recycle", "files", "search", "volume", "network", "settings", "power"];
var DEFAULT_DRAWER = ["autoTile", "running", "wifi", "bluetooth", "micMute", "keepAwake"];

function known(id) {
    return Object.prototype.hasOwnProperty.call(ITEMS, id);
}

function isLauncher(id) {
    return known(id) && ITEMS[id].kind === "launcher";
}

function icon(id) {
    return known(id) ? ITEMS[id].icon : "";
}

function label(id) {
    return known(id) ? ITEMS[id].label : "";
}

function kind(id) {
    return known(id) ? ITEMS[id].kind : "";
}

function defaults() {
    return {bar: DEFAULT_BAR.slice(), drawer: DEFAULT_DRAWER.slice()};
}

// Listes saines : ids connus, sans doublon (la barre prime), lanceurs hors tiroir, `settings` toujours présent.
// Une liste absente (undefined/null) retombe sur les valeurs par défaut.
function normalize(bar, drawer) {
    var d = defaults();
    var b = Array.isArray(bar) ? bar : (bar && bar.length !== undefined ? Array.prototype.slice.call(bar) : d.bar);
    var t = Array.isArray(drawer) ? drawer : (drawer && drawer.length !== undefined ? Array.prototype.slice.call(drawer) : d.drawer);
    var seen = {};
    var outBar = [];
    var outDrawer = [];
    b.forEach(function (id) {
        if (known(id) && !seen[id]) {
            seen[id] = true;
            outBar.push(id);
        }
    });
    t.forEach(function (id) {
        if (known(id) && !seen[id]) {
            seen[id] = true;
            if (isLauncher(id))
                outBar.push(id);
            else
                outDrawer.push(id);
        }
    });
    if (!seen.settings)
        outBar.push("settings");
    return {bar: outBar, drawer: outDrawer};
}

// "bar", "drawer" ou "hidden".
function placement(bar, drawer, id) {
    var l = normalize(bar, drawer);
    if (l.bar.indexOf(id) >= 0)
        return "bar";
    if (l.drawer.indexOf(id) >= 0)
        return "drawer";
    return "hidden";
}

function canPlace(id, where) {
    if (!known(id))
        return false;
    if (where === "drawer")
        return !isLauncher(id);
    if (where === "hidden")
        return id !== "settings";
    return where === "bar";
}

// Déplace id vers where (bar, drawer, hidden) en fin de liste ; une demande impossible rend les listes inchangées.
function setPlacement(bar, drawer, id, where) {
    var l = normalize(bar, drawer);
    if (!canPlace(id, where) || placement(l.bar, l.drawer, id) === where)
        return l;
    l.bar = l.bar.filter(function (x) { return x !== id; });
    l.drawer = l.drawer.filter(function (x) { return x !== id; });
    if (where === "bar")
        l.bar.push(id);
    else if (where === "drawer")
        l.drawer.push(id);
    return l;
}

// Voisin d'id dans sa liste à la distance delta (-1 ou +1) ; les lanceurs gardent leur place fixe et sont ignorés.
function neighbour(list, id, delta) {
    var i = list.indexOf(id) + delta;
    while (i >= 0 && i < list.length && isLauncher(list[i]))
        i += delta;
    return i >= 0 && i < list.length ? i : -1;
}

function canMove(bar, drawer, id, delta) {
    if (isLauncher(id))
        return false;
    var l = normalize(bar, drawer);
    var list = l.bar.indexOf(id) >= 0 ? l.bar : l.drawer;
    return list.indexOf(id) >= 0 && neighbour(list, id, delta) >= 0;
}

// Échange id avec son voisin de même liste (delta -1 = vers le début, +1 = vers la fin).
function move(bar, drawer, id, delta) {
    var l = normalize(bar, drawer);
    if (!canMove(l.bar, l.drawer, id, delta))
        return l;
    var list = l.bar.indexOf(id) >= 0 ? l.bar : l.drawer;
    var from = list.indexOf(id);
    var to = neighbour(list, id, delta);
    list[from] = list[to];
    list[to] = id;
    return l;
}

// Ids des lignes de réglage, regroupés : barre, tiroir, puis masqués (lanceurs d'abord, dans l'ordre du catalogue).
function rows(bar, drawer) {
    var l = normalize(bar, drawer);
    var hidden = ORDER.filter(function (id) { return l.bar.indexOf(id) < 0 && l.drawer.indexOf(id) < 0; });
    return l.bar.concat(l.drawer, hidden);
}

// Ids de la barre hors lanceurs, dans l'ordre : groupe de droite du dock.
function barTools(bar, drawer) {
    return normalize(bar, drawer).bar.filter(function (id) { return !isLauncher(id); });
}
