pragma Singleton
import QtQuick
import qs.config
import "PaletteBlend.js" as Blend

QtObject {
    id: root
    readonly property real surfaceOpacity: Bridge.glassEnabled ? Bridge.glassTintOpacity : 1
    // Une base opaque, un alpha : le cadre, le tableau de bord et le dock lisent tous surfaceColor.
    readonly property color surfaceBase: pick("m3surface", Bridge.dark ? "#101518" : "#fff8f6")
    readonly property color surfaceColor: Qt.alpha(surfaceBase, surfaceOpacity)
    readonly property bool light: !Bridge.dark
    // Changement de palette (nouveau fond d’écran) : fondu de l’ancienne vers la nouvelle, un seul réel animé.
    // Au repos progress vaut 1, les rôles sont constants : aucune animation, aucun timer.
    readonly property int transitionDuration: Appearance.reducedEffects ? 0 : Appearance.anim.durations.large
    property real progress: 1
    property var fromPalette: ({})
    property var toPalette: ({})
    property Connections source: Connections {
        target: Bridge
        function onAdaptivePaletteChanged() {
            root.retarget(Bridge.adaptivePalette);
        }
    }
    property NumberAnimation fade: NumberAnimation {
        target: root
        property: "progress"
        from: 0
        to: 1
        duration: root.transitionDuration
        easing.type: Easing.BezierSpline
        easing.bezierCurve: Appearance.anim.curves.standard
    }
    Component.onCompleted: {
        fromPalette = toPalette = Bridge.adaptivePalette || ({});
    }
    // Le point de départ est la palette actuellement affichée, même en pleine transition.
    function retarget(next) {
        next = next || ({});
        if (Blend.same(next, toPalette))
            return;
        fade.stop();
        var shown = progress >= 1 ? toPalette : Blend.snapshot(fromPalette, toPalette, progress);
        // Premier vrai jeu de couleurs (le départ ne serait que les valeurs de secours) : application immédiate.
        if (transitionDuration <= 0 || Object.keys(toPalette).length === 0) {
            fromPalette = toPalette = next;
            progress = 1;
            return;
        }
        fromPalette = shown;
        toPalette = next;
        progress = 0;
        fade.start();
    }
    function pick(role, fallback) {
        var to = toPalette[role] ?? fallback;
        return progress >= 1 ? to : Blend.mix(fromPalette[role] ?? fallback, to, progress);
    }
    property QtObject palette: QtObject {
        property color m3background: glassLayer(pick("m3background", Bridge.dark ? "#101518" : "#fff8f6"), 1)
        property color m3surface: glassLayer(pick("m3surface", Bridge.dark ? "#101518" : "#fff8f6"), 1)
        property color m3onSurface: pick("m3onSurface", Bridge.dark ? "#e5ebef" : "#261916")
        property color m3surfaceContainer: glassLayer(pick("m3surfaceContainer", Bridge.dark ? "#182025" : "#fae8e3"), 0.65)
        property color m3surfaceContainerLow: glassLayer(pick("m3surfaceContainerLow", Bridge.dark ? "#151c21" : "#fff0eb"), 0.5)
        property color m3surfaceContainerHigh: glassLayer(pick("m3surfaceContainerHigh", Bridge.dark ? "#242d33" : "#f5e0d9"), 0.8)
        property color m3surfaceContainerHighest: glassLayer(pick("m3surfaceContainerHighest", Bridge.dark ? "#303b42" : "#eed9d2"), 1)
        property color m3primary: pick("m3primary", Bridge.dark ? "#afd4f0" : "#895143")
        property color m3secondary: pick("m3secondary", Bridge.dark ? "#c3c8d0" : "#775b52")
        property color m3tertiary: pick("m3tertiary", Bridge.dark ? "#c7cea0" : "#68613c")
        property color m3primaryContainer: glassLayer(pick("m3primaryContainer", Bridge.dark ? "#304b60" : "#ffdace"), 0.85)
        property color m3secondaryContainer: glassLayer(pick("m3secondaryContainer", Bridge.dark ? "#404851" : "#f5dcd3"), 0.85)
        property color m3onPrimary: pick("m3onPrimary", Bridge.dark ? "#12354a" : "#ffffff")
        property color m3onSecondary: pick("m3onSecondary", Bridge.dark ? "#253039" : "#ffffff")
        property color m3onPrimaryContainer: pick("m3onPrimaryContainer", Bridge.dark ? "#d1e7fa" : "#351006")
        property color m3onSecondaryContainer: pick("m3onSecondaryContainer", Bridge.dark ? "#e3e6ed" : "#2b1811")
        property color m3onSurfaceVariant: pick("m3onSurfaceVariant", Bridge.dark ? "#bdc7cf" : "#54433e")
        property color m3outline: pick("m3outline", Bridge.dark ? "#8897a2" : "#85726a")
        property color m3outlineVariant: pick("m3outlineVariant", Bridge.dark ? "#3c4a54" : "#d6c3bc")
        property color m3error: pick("m3error", Bridge.dark ? "#ffb4ab" : "#b3261e")
        property color m3onError: pick("m3onError", Bridge.dark ? "#690005" : "#ffffff")
        property color m3shadow: pick("m3shadow", Bridge.dark ? "#000000" : "#000000")
        property color m3scrim: pick("m3scrim", Bridge.dark ? "#000000" : "#000000")
        property color m3inverseSurface: pick("m3inverseSurface", Bridge.dark ? "#e0e3e6" : "#352f2c")
        property color m3inverseOnSurface: pick("m3inverseOnSurface", Bridge.dark ? "#2e3134" : "#fff0eb")
        property color m3inversePrimary: pick("m3inversePrimary", Bridge.dark ? "#40657f" : "#ffb5a0")
        property color m3onTertiary: pick("m3onTertiary", Bridge.dark ? "#263640" : "#f5ded5")
    }
    readonly property var tPalette: palette
    function glassLayer(color, strength) {
        return Bridge.glassEnabled ? Qt.alpha(color, Math.min(.06, Bridge.glassOpacity * .2 * strength)) : color;
    }
    // Écran de verrouillage : la teinte verre ne fait varier que l’alpha, on retrouve la couleur pleine.
    function solid(color) {
        return Qt.alpha(color, 1);
    }
    function layer(color, level) {
        return Qt.tint(color, Qt.alpha(palette.m3primary, Math.min(.18, level * .025)));
    }
    function onColour(color) {
        return palette.m3onSurface;
    }
}
