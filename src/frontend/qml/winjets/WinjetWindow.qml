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
    // Suspendu par la couche bureau (plein écran, recouvert, verrouillé) : reçoit ctx.paused et coupe minuteurs et animations.
    property bool paused: false
    property rect start: Qt.rect(0, 0, 0, 0)
    // Vrai dès que le curseur a dépassé la zone morte : seulement alors le geste est écrit.
    property bool armed: false

    flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowDoesNotAcceptFocus | Qt.NoDropShadowWindowHint
    color: "transparent"
    visible: !!inst && inst.valid === true && inst.hidden !== true
    transientParent: null
    title: inst && inst.packageName ? inst.packageName : "Winjet"

    function syncGeometry(): void {
        if (gesture || !inst || !inst.valid)
            return;
        place(inst.x, inst.y, inst.w, inst.h);
    }

    // Une seule mise en place native (x, y, largeur, hauteur) : quatre affectations donnaient quatre déplacements de fenêtre.
    function place(px: real, py: real, pw: real, ph: real): void {
        const layer = Bridge.winjets.desktopLayer;
        if (layer && layer.place)
            layer.place(win, Math.round(px), Math.round(py), Math.round(pw), Math.round(ph));
        else {
            x = px;
            y = py;
            width = pw;
            height = ph;
        }
    }

    function shape(): void {
        if (visible && !gesture)
            Bridge.winjets.desktopLayer.shape(win, WinjetTheme.radiusFor(width, height), glassOn, glassAmount);
    }

    // Sortie du mode édition en plein geste (Échap, Terminé, clic sur la grille) : la poignée est détruite sans `released`.
    onEditingChanged: {
        if (!editing && gesture)
            endGesture();
    }
    onInstChanged: syncGeometry()
    onVisibleChanged: {
        refreshPause();
        if (visible) {
            syncGeometry();
            Qt.callLater(() => {
                Bridge.winjets.desktopLayer.register(win);
                shape();
                refreshPause();
            });
        }
    }
    onWidthChanged: Qt.callLater(shape)
    onHeightChanged: Qt.callLater(shape)

    onGlassOnChanged: Qt.callLater(shape)
    onGlassAmountChanged: Qt.callLater(shape)

    function refreshPause(): void {
        const layer = Bridge.winjets.desktopLayer;
        paused = !!layer && visible && layer.paused(win) === true;
    }

    Connections {
        target: Bridge.winjets.desktopLayer
        ignoreUnknownSignals: true

        function onPauseChanged(): void {
            win.refreshPause();
        }
    }

    Connections {
        target: Bridge.winjets

        function onInstanceChanged(id: string): void {
            if (id === win.instanceId)
                win.inst = Bridge.winjets.instance(id);
        }
    }

    // Geste : déplacement ou redimensionnement par une poignée. Les deltas viennent du curseur global (pas de la fenêtre qui
    // bouge), l'aimantation est calculée par le pont sur des voisines figées au début ; une seule écriture à la fin.
    function beginGesture(): void {
        start = Qt.rect(x, y, width, height);
        armed = false;
        gesture = true;
        Bridge.winjets.beginGesture(instanceId);
    }

    function applyGesture(handle: string, dx: real, dy: real, snap: bool): void {
        if (!gesture)
            return;
        // Zone morte : un double-clic ou un clic tremblé ne déplace ni n'aimante rien.
        if (!armed) {
            if (Math.abs(dx) + Math.abs(dy) < 4)
                return;
            armed = true;
        }
        if (handle === "move") {
            const r = Bridge.winjets.snapMove(instanceId, start.x + dx, start.y + dy, start.width, start.height, snap);
            place(r.x, r.y, start.width, start.height);
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
        const r = Bridge.winjets.snapResize(instanceId, handle, left, top, Math.max(1, right - left), Math.max(1, bottom - top), snap);
        place(r.x, r.y, r.w, r.h);
    }

    function endGesture(): void {
        if (!gesture)
            return;
        gesture = false;
        if (armed) {
            Bridge.winjets.commitGeometry(instanceId, x, y, width, height);
            shape();
        } else {
            Bridge.winjets.endGesture();
        }
        armed = false;
    }

    WinjetCard {
        anchors.fill: parent
        inst: win.inst
        editing: win.editing
        active: win.visible
        paused: win.paused
        onPressedAt: handle => win.beginGesture()
        onDragged: (handle, dx, dy, snap) => win.applyGesture(handle, dx, dy, snap)
        onReleased: handle => win.endGesture()
    }
}
