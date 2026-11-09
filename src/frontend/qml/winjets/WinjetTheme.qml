pragma Singleton
import QtQuick
import qs.services

// Valeurs partagées par les cartes Winjets : rayon, gouttière, voile et rôles de palette. Aucune couleur littérale :
// tout vient de Colours (le thème, le mode clair/sombre et le verre suivent le shell).
QtObject {
    readonly property real radius: 20
    readonly property real gutter: 16
    readonly property real pad: 16

    // Voile de la carte posée sur le verre. 0.78 minimum : le texte secondaire garde >= 4.5:1 même sur un fond clair ou noir.
    readonly property real veilAlpha: Bridge.glassEnabled && !Bridge.reducedEffects ? Math.max(0.78, Bridge.glassTintOpacity) : 0.96
    readonly property color veil: Qt.alpha(Colours.surfaceBase, veilAlpha)
    // Liseré 1 px, plus marqué en sombre (image 3 de la référence).
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
