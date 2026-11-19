.pragma library
// Pilule : calculs purs, sans état (niveau d'une barre du spectre, horloge, fraction de lecture, suivi du panneau du haut).

// Niveau 0-1 de la barre i parmi count : moyenne des bandes regroupées, racine pour relever les niveaux faibles.
function level(values, i, count) {
    if (!values || values.length === 0 || count <= 0)
        return 0;
    var a = Math.floor(i * values.length / count);
    var b = Math.max(a + 1, Math.floor((i + 1) * values.length / count));
    var t = 0;
    for (var k = a; k < b; k++)
        t += Number(values[k]) || 0;
    return Math.min(1, Math.sqrt(Math.max(0, t / (b - a))));
}

// Niveau 0-1 de la barre i parmi count sur les `span` premières bandes, réparties en gammes (bornes logarithmiques : les
// graves restent étroits, les médiums larges), gain puis courbe pow(v, curve) pour relever les niveaux faibles.
function spread(values, i, count, span, gain, curve) {
    if (!values || values.length === 0 || count <= 0 || span <= 0)
        return 0;
    var n = Math.min(span, values.length);
    var a = i === 0 ? 0 : Math.round(Math.pow(n, i / count));
    var b = Math.max(a + 1, Math.round(Math.pow(n, (i + 1) / count)));
    var t = 0;
    for (var k = a; k < b && k < n; k++)
        t += Number(values[k]) || 0;
    var v = Math.min(1, Math.max(0, t / (b - a)) * gain);
    return Math.pow(v, curve);
}

// 83 -> "1:23", 3725 -> "1:02:05" ; durée inconnue ou négative -> "--:--".
function clock(seconds) {
    var s = Number(seconds);
    if (!isFinite(s) || s < 0)
        return "--:--";
    s = Math.floor(s);
    var h = Math.floor(s / 3600);
    var m = Math.floor((s % 3600) / 60);
    var r = String(s % 60);
    if (r.length < 2)
        r = "0" + r;
    if (h > 0)
        return h + ":" + (m < 10 ? "0" : "") + m + ":" + r;
    return m + ":" + r;
}

// Avancement 0-1 ; 0 tant que la durée est inconnue.
function fraction(position, length) {
    var p = Number(position), l = Number(length);
    if (!isFinite(p) || !isFinite(l) || l <= 0)
        return 0;
    return Math.max(0, Math.min(1, p / l));
}

// Position verticale de la pilule : au repos, ou juste sous le bord bas du panneau du haut (écart gap) quand il descend.
// panelBottom <= 0 : aucun panneau visible.
function followY(restY, panelBottom, gap) {
    if (!(panelBottom > 0))
        return restY;
    return Math.max(restY, panelBottom + gap);
}

// Volume 0-100 après `notches` crans de molette (positif monte), borné et arrondi.
function stepVolume(current, notches, step) {
    var c = Number(current), n = Number(notches);
    if (!isFinite(c))
        c = 0;
    if (!isFinite(n))
        n = 0;
    return Math.max(0, Math.min(100, Math.round(c + n * step)));
}
