.pragma library
// Vue d'ensemble : disposition « lignes justifiées » et navigation géométrique. Fonctions pures, sans état.

// aspects : rapport largeur/hauteur de chaque vignette, dans l'ordre. area : {w, h} disponible. Chaque ligne reçoit la hauteur
// de vignette commune h ; une carte occupe h + labelH en hauteur. On retient le nombre de lignes qui maximise h, plafonné par
// maxH (hauteur) et maxW (largeur) pour qu'une fenêtre seule ne remplisse pas l'écran. Retourne [{x, y, w, h, row}].
function layout(aspects, area, gap, labelH, maxW, maxH) {
    var n = aspects.length;
    if (n === 0 || area.w <= 0 || area.h <= 0)
        return [];
    var total = 0, i;
    for (i = 0; i < n; i++)
        total += aspects[i];
    var best = null;
    for (var rows = 1; rows <= n; rows++) {
        var groups = [[]], sum = 0, target = total / rows;
        for (i = 0; i < n; i++) {
            var cur = groups[groups.length - 1];
            if (cur.length > 0 && groups.length < rows && sum + aspects[i] / 2 > target * groups.length) {
                groups.push([]);
                cur = groups[groups.length - 1];
            }
            cur.push(i);
            sum += aspects[i];
        }
        var h = (area.h - (groups.length - 1) * gap) / groups.length - labelH;
        for (var g = 0; g < groups.length; g++) {
            var rowSum = 0;
            for (var k = 0; k < groups[g].length; k++)
                rowSum += aspects[groups[g][k]];
            h = Math.min(h, (area.w - (groups[g].length - 1) * gap) / rowSum);
        }
        if (best === null || h > best.h)
            best = {h: h, groups: groups};
    }
    var scale = Math.min(best.h, maxH);
    for (i = 0; i < n; i++)
        scale = Math.min(scale, maxW / aspects[i]);
    scale = Math.max(1, scale);
    var out = new Array(n), rowsH = best.groups.length * (scale + labelH) + (best.groups.length - 1) * gap;
    var y = Math.max(0, (area.h - rowsH) / 2);
    for (var r = 0; r < best.groups.length; r++) {
        var members = best.groups[r], width = (members.length - 1) * gap;
        for (var m = 0; m < members.length; m++)
            width += scale * aspects[members[m]];
        var x = Math.max(0, (area.w - width) / 2);
        for (var q = 0; q < members.length; q++) {
            var w = scale * aspects[members[q]];
            out[members[q]] = {x: x, y: y, w: w, h: scale, row: r};
            x += w + gap;
        }
        y += scale + labelH + gap;
    }
    return out;
}

// Voisin dans la direction (dx, dy) : le centre le plus proche dont l'écart sur l'axe demandé est positif, l'écart transversal
// comptant double. Retourne l'index courant s'il n'y a personne.
function neighbour(rects, current, dx, dy) {
    if (current < 0 || current >= rects.length)
        return rects.length > 0 ? 0 : -1;
    var c = rects[current], cx = c.x + c.w / 2, cy = c.y + c.h / 2, best = -1, bestScore = 0;
    for (var i = 0; i < rects.length; i++) {
        if (i === current)
            continue;
        var ax = rects[i].x + rects[i].w / 2 - cx, ay = rects[i].y + rects[i].h / 2 - cy;
        var along = dx !== 0 ? ax * dx : ay * dy, across = dx !== 0 ? Math.abs(ay) : Math.abs(ax);
        if (along <= 1)
            continue;
        var score = along + across * 2;
        if (best < 0 || score < bestScore) {
            best = i;
            bestScore = score;
        }
    }
    return best < 0 ? current : best;
}

// Filtre par titre ou nom d'application, sans casse.
function matches(item, text) {
    var needle = String(text || "").toLowerCase();
    return needle === "" || String(item.title || "").toLowerCase().indexOf(needle) >= 0
        || String(item.app || "").toLowerCase().indexOf(needle) >= 0;
}

// --- Mouvement (ouverture, fermeture, activation) : fonctions pures ---

function clamp01(v) {
    return v < 0 ? 0 : v > 1 ? 1 : v;
}

// Progression de t dans la fenêtre [from, to], bornée à 0..1.
function ramp(t, from, to) {
    return to <= from ? (t >= to ? 1 : 0) : clamp01((t - from) / (to - from));
}

function lerp(a, b, t) {
    return a + (b - a) * t;
}

function lerpRect(a, b, t) {
    return {x: lerp(a.x, b.x, t), y: lerp(a.y, b.y, t), w: lerp(a.w, b.w, t), h: lerp(a.h, b.h, t)};
}

// Intersection avec {w, h} (origine 0,0) ; rectangle centré de la taille d'origine si l'intersection est vide.
function clampRect(r, bounds) {
    var x0 = Math.max(0, r.x), y0 = Math.max(0, r.y), x1 = Math.min(bounds.w, r.x + r.w), y1 = Math.min(bounds.h, r.y + r.h);
    if (x1 - x0 < 1 || y1 - y0 < 1) {
        var w = Math.max(1, Math.min(r.w, bounds.w)), h = Math.max(1, Math.min(r.h, bounds.h));
        return {x: (bounds.w - w) / 2, y: (bounds.h - h) / 2, w: w, h: h};
    }
    return {x: x0, y: y0, w: x1 - x0, h: y1 - y0};
}

// Rectangle d'une fenêtre du backend (x, y, w, h en pixels PHYSIQUES de l'écran principal, origine 0,0) converti en pixels logiques
// locaux à la vue : on divise par ratio (devicePixelRatio de la fenêtre, comme overview_native.place qui le multiplie), puis on borne
// à l'écran, après soustraction de origin ({x, y} logique de l'écran de la vue) (fenêtres agrandies : bordures négatives).
function toLocal(item, ratio, bounds, origin) {
    var k = ratio > 0 ? ratio : 1, ox = origin ? origin.x : 0, oy = origin ? origin.y : 0;
    return clampRect({x: item.x / k - ox, y: item.y / k - oy, w: Math.max(1, item.w / k), h: Math.max(1, item.h / k)}, bounds);
}

// Rectangle de départ/arrivée d'un « survol » : la vraie place de la fenêtre (rest) et celle d'où elle part (src). Une fenêtre réduite
// part d'une petite vignette en bas au centre de l'écran (le dock), à l'échelle du rectangle restauré, et revient vers rest.
function realRects(item, ratio, bounds, dockSize, origin) {
    var rest = toLocal(item, ratio, bounds, origin);
    if (!item.minimized)
        return {src: rest, rest: rest};
    var s = Math.min(1, dockSize / Math.max(rest.w, rest.h)), w = Math.max(1, rest.w * s), h = Math.max(1, rest.h * s);
    return {src: {x: (bounds.w - w) / 2, y: bounds.h - h, w: w, h: h}, rest: rest};
}
