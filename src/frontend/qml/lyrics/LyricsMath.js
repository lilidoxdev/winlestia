.pragma library
// Paroles synchronisées : calculs purs, sans état. lines = [{ time: secondes, text }] triées par temps.

// Indice de la ligne en cours à `position` (dernière ligne dont le temps est <= position) ; -1 avant la première ligne.
function activeIndex(lines, position) {
    if (!lines || lines.length === 0)
        return -1;
    var p = Number(position);
    if (!isFinite(p))
        return -1;
    var lo = 0, hi = lines.length - 1, found = -1;
    while (lo <= hi) {
        var mid = (lo + hi) >> 1;
        if (Number(lines[mid].time) <= p) {
            found = mid;
            lo = mid + 1;
        } else {
            hi = mid - 1;
        }
    }
    return found;
}

// Temps (s) de la ligne suivant `index` ; -1 s'il n'y en a pas. Avant la première ligne (index -1), c'est la première.
function nextTime(lines, index) {
    if (!lines || index + 1 < 0 || index + 1 >= lines.length)
        return -1;
    return Number(lines[index + 1].time);
}

// Avancement 0-1 dans la ligne `index` ; 0 hors bornes, 1 pour la dernière ligne (aucune fin connue).
function progressInLine(lines, index, position) {
    if (!lines || index < 0 || index >= lines.length)
        return 0;
    var p = Number(position);
    if (!isFinite(p))
        return 0;
    var start = Number(lines[index].time);
    var end = nextTime(lines, index);
    if (end < 0)
        return 1;
    if (end <= start)
        return 1;
    return Math.max(0, Math.min(1, (p - start) / (end - start)));
}
