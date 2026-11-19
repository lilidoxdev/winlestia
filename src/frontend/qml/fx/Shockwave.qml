import QtQuick
import QtQuick.Window
import qs.services
import qs.config

// Onde de choc unique, déclenchée par fire(screen, x, y). Fenêtre créée une fois au préchauffage (warm()), masquée au repos (aucun rendu,
// aucun minuteur), puis réutilisée.
// Le shader (shaders/shockwave.frag.qsb) ne montre des pixels capturés que sur l'anneau en mouvement ; le reste laisse voir le bureau réel.
Item {
    id: root

    property bool enabled: true
    // Fenêtre du shell : l'onde se place juste en dessous (jamais au-dessus du panneau).
    property var shell: null
    // Capture synchrone sur le fil graphique : au-delà de ce budget (ms), l'onde est coupée pour le reste de la session.
    readonly property int captureBudgetMs: 60
    // Rayon maximal de l'onde (px logiques) : seul le carré de côté 2 * reach autour du point de départ est capturé.
    readonly property int reach: 520
    // Trace d'ouverture opt-in (open_trace.py) : sans elle, note() ne fait rien.
    readonly property bool tracing: Bridge.traceEnabled()
    function note(step: string): void {
        if (tracing)
            Bridge.trace(step);
    }
    readonly property int maxOverruns: 3
    // Marge de la minuterie de sécurité au-delà de la vie maximale de l'onde (ms).
    readonly property int safetyMarginMs: 300
    property bool warmedUp: false
    property int overruns: 0
    property bool overBudget: false
    readonly property bool allowed: enabled && !overBudget && !Appearance.reducedEffects
    readonly property real waveSpeed: 1700
    readonly property real waveAmplitude: 18
    readonly property real decaySeconds: 0.35
    readonly property real maxLifeSeconds: 0.9
    readonly property real fadeSeconds: 0.2
    readonly property real brightness: 0.16
    readonly property real tintAmount: 0.05
    readonly property int refireGuardMs: 150

    property var shotScreen: null
    // Région capturée, en pixels logiques locaux à l'écran ; centre de l'onde normalisé dans cette région.
    property int regionX: 0
    property int regionY: 0
    property int regionW: 1
    property int regionH: 1
    property real centerX: 0
    property real centerY: 0
    property real farDistance: 1
    property real lifeSeconds: 0.5
    property real wave: 0
    property bool active: false
    property double startedAt: 0

    function fire(screen, x, y): void {
        if (!allowed || !screen) {
            note("shock skipped reason=" + (!enabled || !screen ? "disabled" : overBudget ? "overBudget" : "reduced"));
            return;
        }
        if (active && Date.now() - startedAt < refireGuardMs) {
            note("shock skipped reason=refire");
            return;
        }
        // Fenêtre non préchauffée : jamais créée de façon synchrone pendant l'ouverture, l'onde est omise pour cette fois.
        if (!loader.item) {
            note("shock skipped reason=cold");
            return;
        }
        stop();
        const w = screen.width;
        const h = screen.height;
        const side = 2 * reach;
        const rw = Math.min(side, w);
        const rh = Math.min(side, h);
        const rx = Math.max(0, Math.min(w - rw, Math.round(x - reach)));
        const ry = Math.max(0, Math.min(h - rh, Math.round(y - reach)));
        const t0 = Date.now();
        const captured = ShockShot.capture(screen.name, rx, ry, rw, rh);
        // La première capture (échauffement du pilote) n'est pas comptée ; ensuite, des dépassements consécutifs seulement.
        if (!warmedUp)
            warmedUp = true;
        else if (Date.now() - t0 > captureBudgetMs)
            overruns++;
        else
            overruns = 0;
        if (overruns >= maxOverruns && !overBudget) {
            overBudget = true;
            note("shock overBudget tripped ms=" + (Date.now() - t0));
        }
        if (!captured || overBudget) {
            note("shock skipped reason=" + (!captured ? "capture-failed" : "overBudget"));
            ShockShot.release();
            return;
        }
        shotScreen = screen;
        regionX = rx;
        regionY = ry;
        regionW = rw;
        regionH = rh;
        centerX = Math.max(0, Math.min(1, (x - rx) / rw));
        centerY = Math.max(0, Math.min(1, (y - ry) / rh));
        farDistance = reach;
        lifeSeconds = Math.min(maxLifeSeconds, farDistance / waveSpeed + decaySeconds);
        startedAt = Date.now();
        active = true;
        safety.restart();
        loader.item.begin();
        note("shock fired");
    }

    // Crée la fenêtre de l'onde (masquée) et compile le shader à l'avance ; appelée une fois par Main.qml au préchauffage.
    function warm(): void {
        if (enabled)
            loader.active = true;
    }

    function stop(): void {
        if (active)
            note("shock end");
        safety.stop();
        anim.stop();
        active = false;
        wave = 0;
        if (loader.item)
            loader.item.end();
        ShockShot.release();
    }
    onAllowedChanged: if (!allowed && active)
        stop()
    Component.onDestruction: ShockShot.release()

    // Filet de sécurité : si l'image n'est jamais prête, la fenêtre et la capture ne restent pas en mémoire.
    Timer {
        id: safety
        repeat: false
        interval: Math.round(root.maxLifeSeconds * 1000 * Appearance.anim.durations.scale) + root.safetyMarginMs
        onTriggered: root.stop()
    }

    // Dès que le shell devient visible pendant une onde, elle se glisse juste sous lui (jamais au-dessus du panneau).
    Connections {
        target: root.shell
        enabled: root.active
        function onVisibleChanged() {
            if (root.shell && root.shell.visible && loader.item)
                ShockShot.placeUnder(loader.item, root.shell);
        }
    }

    Loader {
        id: loader
        active: false

        sourceComponent: Window {
            id: win

            function begin(): void {
                image.source = "image://shockshot/" + ShockShot.seq;
                visible = true;
                // Au premier plan sans activation ; le placement sous le shell n'a lieu que lorsque celui-ci est visible.
                ShockShot.placeTopmost(win);
                if (root.shell && root.shell.visible)
                    ShockShot.placeUnder(win, root.shell);
                launch();
            }
            function launch(): void {
                if (visible && image.status === Image.Ready && root.active && !anim.running) {
                    root.wave = 0;
                    root.note("shock shown");
                    anim.start();
                }
            }
            function end(): void {
                visible = false;
                image.source = "";
            }

            // Sans parent transitoire : le shell est masqué au déclenchement et Qt différerait sinon l'affichage de l'onde.
            transientParent: null
            onVisibleChanged: launch()
            screen: root.shotScreen
            x: (root.shotScreen ? root.shotScreen.virtualX : 0) + root.regionX
            y: (root.shotScreen ? root.shotScreen.virtualY : 0) + root.regionY
            width: root.regionW
            height: root.regionH
            title: "Panneaux · Onde de choc"
            color: "transparent"
            visible: false
            flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint |
                Qt.WindowTransparentForInput | Qt.WindowDoesNotAcceptFocus

            Image {
                id: image
                anchors.fill: parent
                cache: false
                asynchronous: false
                visible: false
                layer.enabled: false
                onStatusChanged: {
                    if (status === Image.Error)
                        Qt.callLater(root.stop);
                    else
                        win.launch();
                }
            }

            ShaderEffect {
                anchors.fill: parent
                visible: win.visible
                property variant source: image
                property vector4d tint: Qt.vector4d(
                    Colours.palette.m3primary.r, Colours.palette.m3primary.g, Colours.palette.m3primary.b, 1)
                property vector2d size: Qt.vector2d(width, height)
                property vector2d center: Qt.vector2d(root.centerX, root.centerY)
                property real time: root.wave
                property real speed: root.waveSpeed
                property real amplitude: root.waveAmplitude
                property real far: root.farDistance
                property real boost: root.brightness
                property real tintMix: root.tintAmount
                property real fade: Math.max(0, Math.min(1, (root.lifeSeconds - root.wave) / root.fadeSeconds))
                property real reach: root.reach
                fragmentShader: Qt.resolvedUrl("../../shaders/shockwave.frag.qsb")
            }
        }
    }

    NumberAnimation {
        id: anim
        target: root
        property: "wave"
        from: 0
        to: root.lifeSeconds
        duration: Math.round(root.lifeSeconds * 1000 * Appearance.anim.durations.scale)
        easing.type: Easing.Linear
        onFinished: root.stop()
    }
}
