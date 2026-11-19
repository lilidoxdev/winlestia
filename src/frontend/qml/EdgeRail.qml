// Rail de bord caché générique (adaptateur de fenêtre Windows, même recette que Dock.qml) : `side` vaut "left" ou "right".
// Fenêtre de taille fixe ; seules la silhouette de bosse (bumpWidth/bumpHeight, lue par surface_geometry) et le contenu s'animent.
// Les instances fournissent contentW/contentHeight (+ chevronW/panelW optionnels pour un tiroir) et placent leur contenu avec edgeX/slide.
import QtQuick
import QtQuick.Window
import qs.components
import qs.config
import qs.services

Window {
    id: rail
    // Une Window transitoire est différée tant que son tableau de bord propriétaire est masqué sous Windows.
    transientParent: null
    color: "transparent"
    flags: (Bridge.preview ? Qt.Window
        : Qt.Tool) | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint | Qt.WindowDoesNotAcceptFocus
    property string side: "right"
    readonly property bool isLeft: side === "left"
    property real contentW: 54
    property real contentHeight: 0
    property real chevronW: 0
    property real panelW: 0
    property bool shown: Bridge.preview
    property bool expanded: false
    property real reveal: expanded ? 1 : 0
    Behavior on reveal {
        enabled: !Appearance.reducedEffects
        Anim {
            duration: Appearance.anim.durations.expressiveDefaultSpatial
            easing.bezierCurve: Appearance.anim.curves.emphasizedDecel
        }
    }
    readonly property real railW: contentW + 2 * Appearance.padding.large + chevronW
    readonly property real fillet: 26
    readonly property real baseHeight: Math.min(Math.ceil(contentHeight) + 2 * (fillet + Appearance.padding.large), Math.max(200,
        Bridge.screenHeight - 80))
    readonly property real fullHeight: panelW > 0 ? Math.max(baseHeight, Math.min(Bridge.screenHeight - 160, 520)) : baseHeight
    readonly property real bumpWidth: railW + panelW * reveal
    // Largeur vive que ce rail réclame au bord de son écran : 0 replié ou masqué, rail + tiroir + marge du cadre + écart une fois déplié.
    // Elle suit `reveal` : les voisins (le tableau de bord) s'écartent avec le rail au lieu d'être recouverts.
    readonly property real reservedWidth: reveal * (railW + panelW + inset + Appearance.padding.large)
    readonly property real bumpHeight: baseHeight + (fullHeight - baseHeight) * reveal
    property real offset: shown ? 0 : railW
    // Largeur de bosse envoyée en dernier au fond natif (-1 sans verre natif) ;
    // le contenu suit la plus étroite des deux pour ne jamais dépasser de l'une ou l'autre.
    property real nativeBump: -1
    readonly property real contentBump: nativeBump < 0 ? bumpWidth : Math.max(railW, Math.min(nativeBump, bumpWidth))
    // Bord intérieur de la bosse affichée en coordonnées fenêtre, et glissement signé du contenu pendant le masquage.
    readonly property real edgeX: isLeft ? contentBump : width - contentBump
    readonly property real slide: isLeft ? -offset : offset
    onOffsetChanged: Bridge.updateSurfaces()
    onBumpWidthChanged: Bridge.updateSurfaces()
    // Le cadre d'écran est une autre fenêtre toujours au premier plan ; on remonte le rail au-dessus à chaque sortie.
    onShownChanged: {
        if (!shown)
            expanded = false;
        if (shown && !Bridge.preview)
            Qt.callLater(rail.raise);
    }
    onVisibleChanged: if (visible && !Bridge.preview)
        Qt.callLater(rail.raise)
    readonly property real inset: Bridge.screenFullscreen ? 0 : Math.max(0, Config.border.thickness)
    // La silhouette (surface_geometry.rail_path) est dessinée par la surface d'écran partagée : pas de fond propre ici.
    width: railW + panelW
    height: fullHeight
    x: isLeft ? Bridge.screenX + inset : Bridge.screenX + Bridge.screenWidth - width - inset
    y: Bridge.screenY + (Bridge.screenHeight - height) / 2
    property bool allowed: true
    visible: allowed && (shown || offset < bumpWidth - 1) && !Bridge.screenFullscreen
    Behavior on offset {
        Anim {
            duration: Appearance.anim.durations.expressiveFastSpatial
            easing.bezierCurve: Appearance.anim.curves.emphasizedDecel
        }
    }
}
