pragma Singleton
import QtQuick
import qs.config
import qs.services

// Valeurs partagées par les cartes Winjets : rayon, gouttière, voile et rôles de palette. Aucune couleur littérale :
// tout vient de Colours (le thème, le mode clair/sombre et le verre suivent le shell).
QtObject {
    // Même arrondi que le tableau de bord (Config.border.rounding).
    // Liquid glass v2 : 28 px dès 2x2 cellules (>= 336 px de côté), 22 px en dessous.
    readonly property real radiusLarge: 28
    readonly property real radiusSmall: 22
    readonly property real radius: radiusLarge
    function radiusFor(w: real, h: real): real {
        return (w >= 336 && h >= 336) ? radiusLarge : radiusSmall;
    }
    readonly property real gutter: 16
    readonly property real pad: 16

    // Mouvement événementiel (ms) : survol/pression, changement de texte, valeurs animées.
    readonly property int motionFast: 160
    readonly property int motionMedium: 220
    readonly property int motionSlow: 450

    // Verre : teinte fumée dense (0.58 sombre, 0.62 clair) sur le flou DWM ; opaque en effets réduits.
    readonly property real glassAlpha: Bridge.dark ? 0.58 : 0.62
    readonly property real hoverFillBoost: 0.04
    readonly property color glassFill: solid ? Qt.alpha(Colours.palette.m3surface, 1) : Qt.alpha(Colours.palette.m3surface, glassAlpha)
    function glassFillAt(opacity, hovered: bool): color {
        if (solid)
            return Qt.alpha(Colours.palette.m3surface, 1);
        const custom = opacity !== null && opacity !== undefined && !isNaN(Number(opacity));
        const base = custom ? Math.max(0, Math.min(100, Number(opacity))) / 100 : glassAlpha;
        return Qt.alpha(Colours.palette.m3surface, Math.min(1, base + (hovered ? hoverFillBoost : 0)));
    }
    // Liseré diagonal (haut-gauche -> milieu -> bas-droite) ; `boost` = 1.4 au survol.
    readonly property color rimTop: Qt.alpha(Colours.palette.m3onSurface, 0.55)
    readonly property color rimMid: Qt.alpha(Colours.palette.m3onSurface, 0.10)
    readonly property color rimBottom: Qt.alpha(Colours.palette.m3onSurface, 0.28)
    readonly property color rimInner: Qt.alpha(Colours.palette.m3onSurface, 0.06)
    readonly property real rimBoost: 1.4
    // Reflet spéculaire (haut 35 %) et voile de corps (haut clair, bas sombre).
    readonly property color highlight: Qt.alpha(Colours.palette.m3onSurface, 0.05)
    readonly property color bodyTop: Qt.alpha(Colours.palette.m3onSurface, 0.07)
    readonly property color bodyBottom: Qt.alpha(Colours.palette.m3shadow, 0.06)

    // Surface identique à celle du tableau de bord (dashboard/Background.qml) : m3surface, dont l'alpha suit déjà le verre
    // (Colours.glassLayer) ; opaque sans verre. Pas de liseré : les panneaux n'en ont pas.
    // « Effets réduits » : le verre natif est retiré (WinjetWindow.glassOn), la carte doit alors être pleine, pas transparente sans flou.
    readonly property bool solid: Bridge.reducedEffects
    readonly property color veil: solid ? Qt.alpha(Colours.palette.m3surface, 1) : Colours.palette.m3surface
    // Transparence (0..100 %) réellement appliquée par les panneaux : 1 - alpha de m3surface (verre auto/personnalisé, 0 sans verre).
    readonly property int defaultTransparency: Math.round((1 - veil.a) * 100)
    // Fond d'une carte : opacity null/absent = voile global ; sinon m3surface à l'alpha demandé (0..100 %), le texte reste opaque.
    function fill(opacity): color {
        if (solid || opacity === null || opacity === undefined || isNaN(Number(opacity)))
            return veil;
        return Qt.alpha(Colours.palette.m3surface, Math.max(0, Math.min(100, Number(opacity))) / 100);
    }

    // Liseré réservé à l'habillage d'édition (les cartes n'en ont pas).
    readonly property color border: Bridge.dark ? Qt.alpha(Colours.palette.m3onSurface, 0.3) : Qt.alpha(Colours.palette.m3outline, 0.4)

    readonly property color textPrimary: Colours.palette.m3onSurface
    readonly property color textSecondary: Colours.palette.m3onSurfaceVariant
    readonly property color accent: Colours.palette.m3primary
    readonly property color track: Qt.alpha(Colours.palette.m3onSurfaceVariant, 0.22)

    // Rôles du format (§5) -> palette. Un rôle inconnu retombe sur l'accent.
    function role(name: string): color {
        switch (name) {
        case "primary":
            return Colours.palette.m3primary;
        case "secondary":
            return Colours.palette.m3secondary;
        case "tertiary":
            return Colours.palette.m3tertiary;
        case "onSurface":
            return Colours.palette.m3onSurface;
        case "onSurfaceVariant":
            return Colours.palette.m3onSurfaceVariant;
        case "positive":
            return Colours.palette.m3tertiary;
        case "negative":
            return Colours.palette.m3error;
        default:
            return Colours.palette.m3primary;
        }
    }
}
