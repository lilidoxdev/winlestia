pragma Singleton

import Quickshell

Singleton {
    // Literally just here to shorten accessing stuff :woe:
    // Also kinda so I can keep accessing it with `Appearance.xxx` instead of `Config.appearance.xxx`
    readonly property var rounding: Config.appearance.rounding
    readonly property var spacing: Config.appearance.spacing
    readonly property var padding: Config.appearance.padding
    readonly property var font: Config.appearance.font
    readonly property var anim: Config.appearance.anim
    readonly property var transparency: Config.appearance.transparency
}
