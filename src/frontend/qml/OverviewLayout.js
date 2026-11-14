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
