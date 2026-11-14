// Mélange de deux couleurs « #rgb », « #rrggbb » ou « #aarrggbb » (convention Qt). Fonctions pures, sans état.
function parse(text) {
    var s = String(text);
    if (s.charAt(0) !== "#")
        return null;
    s = s.slice(1);
    if (s.length === 3)
        s = s.charAt(0) + s.charAt(0) + s.charAt(1) + s.charAt(1) + s.charAt(2) + s.charAt(2);
    if (s.length === 6)
        s = "ff" + s;
    if (!/^[0-9a-fA-F]{8}$/.test(s))
        return null;
    return [parseInt(s.substr(2, 2), 16), parseInt(s.substr(4, 2), 16), parseInt(s.substr(6, 2), 16), parseInt(s.substr(0, 2), 16)];
}

function hex(n) {
    var v = Math.max(0, Math.min(255, Math.round(n))).toString(16);
    return v.length < 2 ? "0" + v : v;
}

// t = 0 donne « from », t = 1 donne « to ». Une couleur illisible (nom, format inconnu) bascule directement sur « to ».
function mix(from, to, t) {
    if (t >= 1)
        return to;
    var a = parse(from), b = parse(to);
    if (!a || !b)
        return to;
    if (t <= 0)
        return from;
    var out = [];
    for (var i = 0; i < 4; i++)
        out.push(a[i] + (b[i] - a[i]) * t);
    return "#" + hex(out[3]) + hex(out[0]) + hex(out[1]) + hex(out[2]);
}

// Vrai si les deux palettes ont les mêmes rôles aux mêmes valeurs (Bridge.changed notifie aussi sans changement de couleur).
function same(a, b) {
    var k, n = 0;
    a = a || {};
    b = b || {};
    for (k in a) {
        n++;
        if (b[k] === undefined || String(a[k]) !== String(b[k]))
            return false;
    }
    for (k in b)
        n--;
    return n === 0;
}

// Palette affichée à l'instant t : sert de point de départ quand une nouvelle palette arrive en cours de transition.
function snapshot(from, to, t) {
    var out = {}, k;
    for (k in to)
        out[k] = String(mix(from[k] !== undefined ? from[k] : to[k], to[k], t));
    for (k in from)
        if (out[k] === undefined)
            out[k] = String(from[k]);
    return out;
}
