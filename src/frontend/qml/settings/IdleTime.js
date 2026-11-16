.pragma library

// Délai d'inactivité : minutes + secondes saisies -> total borné, puis retour en minutes/secondes.
var MIN_SECONDS = 30;
var MAX_SECONDS = 86400;
var DEFAULT_SECONDS = 300;

function normalize(minutes, seconds) {
    var total = (parseInt(minutes) || 0) * 60 + (parseInt(seconds) || 0);
    total = Math.max(MIN_SECONDS, Math.min(MAX_SECONDS, total));
    return { total: total, minutes: Math.floor(total / 60), seconds: total % 60 };
}
