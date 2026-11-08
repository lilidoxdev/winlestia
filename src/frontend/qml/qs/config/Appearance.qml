pragma Singleton

import Quickshell

Singleton {
    // Sert uniquement à raccourcir l'accès aux valeurs
    // Et à garder l'accès via `Appearance.xxx` plutôt que `Config.appearance.xxx`
    readonly property var rounding: Config.appearance.rounding
    readonly property var spacing: Config.appearance.spacing
    readonly property var padding: Config.appearance.padding
    readonly property var font: Config.appearance.font
    readonly property var anim: Config.appearance.anim
    readonly property bool reducedEffects: Bridge.reducedEffects || Bridge.quiet
    readonly property var transparency: Config.appearance.transparency
}
