import QtQuick
import QtQuick.Window
import qs.services

// Une carte = une fenêtre sans cadre, transparente, qui ne prend jamais le focus. La couche bureau (desktop_layer.py) la
// range sous les fenêtres ; le verre et les coins arrondis sont posés par DWM sur le HWND (desktop_glass.py).
Window {
    id: win

    required property string instanceId
    // Relu uniquement quand le pont signale CETTE instance (instanceChanged) ; une donnée d'une autre carte ne touche pas celle-ci.
    property var inst: Bridge.winjets.instance(instanceId)
    readonly property bool glassOn: Bridge.glassEnabled && !Bridge.reducedEffects
    readonly property real glassAmount: Bridge.glassOpacity
    readonly property bool editing: Bridge.winjets.editMode
    // Faux pendant un geste : la géométrie vient alors de la souris, pas de l'instantané.
    property bool gesture: false
    property rect start: Qt.rect(0, 0, 0, 0)

    flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowDoesNotAcceptFocus | Qt.NoDropShadowWindowHint
    color: "transparent"
    visible: !!inst && inst.valid === true && inst.hidden !== true
    transientParent: null
    title: inst && inst.packageName ? inst.packageName : "Winjet"

    function syncGeometry(): void {
        if (gesture || !inst || !inst.valid)
            return;
        x = inst.x;
        y = inst.y;
        width = inst.w;
        height = inst.h;
    }

    function shape(): void {
        if (visible)
            Bridge.winjets.desktopLayer.shape(win, WinjetTheme.radius, glassOn, glassAmount);
    }

    onInstChanged: syncGeometry()
    onVisibleChanged: if (visible) {
        syncGeometry();
        Qt.callLater(() => {
            Bridge.winjets.desktopLayer.register(win);
            shape();
        });
    }
    onWidthChanged: Qt.callLater(shape)
    onHeightChanged: Qt.callLater(shape)

    onGlassOnChanged: Qt.callLater(shape)
    onGlassAmountChanged: Qt.callLater(shape)

    Connections {
        target: Bridge.winjets

        function onInstanceChanged(id: string): void {
            if (id === win.instanceId)
                win.inst = Bridge.winjets.instance(id);
        }
    }

    // Geste : déplacement ou redimensionnement par une poignée, avec aimantation côté pont (coordonnées globales logiques).
    function beginGesture(): void {
        start = Qt.rect(x, y, width, height);
        gesture = true;
    }

    function applyGesture(handle: string, dx: real, dy: real): void {
        if (!gesture)
            return;
        if (handle === "move") {
            const r = Bridge.winjets.snapMove(instanceId, start.x + dx, start.y + dy, start.width, start.height);
            x = r.x;
            y = r.y;
            return;
        }
        let left = start.x, top = start.y, right = start.x + start.width, bottom = start.y + start.height;
        if (handle.indexOf("w") >= 0)
            left += dx;
        if (handle.indexOf("e") >= 0)
            right += dx;
        if (handle.indexOf("n") >= 0)
            top += dy;
        if (handle.indexOf("s") >= 0)
            bottom += dy;
        const r = Bridge.winjets.snapResize(instanceId, handle, left, top, Math.max(1, right - left), Math.max(1, bottom - top));
        x = r.x;
        y = r.y;
        width = r.w;
        height = r.h;
    }

    function endGesture(): void {
        if (!gesture)
            return;
        gesture = false;
        Bridge.winjets.commitGeometry(instanceId, x, y, width, height);
    }

    WinjetCard {
        anchors.fill: parent
        inst: win.inst
        editing: win.editing
        active: win.visible
        onPressedAt: handle => win.beginGesture()
        onDragged: (handle, dx, dy) => win.applyGesture(handle, dx, dy)
        onReleased: handle => win.endGesture()
    }
}
