pragma Singleton
import QtQuick
import qs.config
import qs.services

// Valeurs partagées par les cartes Winjets : rayon, gouttière, voile et rôles de palette. Aucune couleur littérale :
// tout vient de Colours (le thème, le mode clair/sombre et le verre suivent le shell).
QtObject {
    // Même arrondi que le tableau de bord (Config.border.rounding).
    readonly property real radius: Config.border.rounding
    readonly property real gutter: 16
    readonly property real pad: 16

    // Surface identique à celle du tableau de bord (dashboard/Background.qml) : m3surface, dont l'alpha suit déjà le verre
    // (Colours.glassLayer) ; opaque sans verre. Pas de liseré : les panneaux n'en ont pas.
    readonly property color veil: Colours.palette.m3surface
    // Transparence (0..100 %) réellement appliquée par les panneaux : 1 - alpha de m3surface (verre auto/personnalisé, 0 sans verre).
    readonly property int defaultTransparency: Math.round((1 - veil.a) * 100)
    // Fond d'une carte : opacity null/absent = voile global ; sinon m3surface à l'alpha demandé (0..100 %), le texte reste opaque.
    function fill(opacity): color {
        if (opacity === null || opacity === undefined || isNaN(Number(opacity)))
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
