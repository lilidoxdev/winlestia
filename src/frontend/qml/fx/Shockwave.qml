import QtQuick
import QtQuick.Window
import qs.services
import qs.config

// Onde de choc unique, déclenchée par fire(screen, x, y). Fenêtre créée à la première utilisation, masquée au repos (aucun rendu, aucun minuteur).
// Le shader (shaders/shockwave.frag.qsb) ne montre des pixels capturés que sur l'anneau en mouvement ; le reste laisse voir le bureau réel.
Item {
    id: root

    property bool enabled: true
    // Fenêtre du shell : l'onde se place juste en dessous (jamais au-dessus du panneau).
    property var shell: null
    // Capture synchrone sur le fil graphique : au-delà de ce budget (ms), l'onde est coupée pour le reste de la session.
    readonly property int captureBudgetMs: 45
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
    property real centerX: 0
    property real centerY: 0
    property real farDistance: 1
    property real lifeSeconds: 0.5
    property real wave: 0
    property bool active: false
    property double startedAt: 0

    function fire(screen, x, y): void {
        if (!allowed || !screen)
            return;
        if (active && Date.now() - startedAt < refireGuardMs)
            return;
        stop();
        const t0 = Date.now();
        const captured = ShockShot.capture(screen.name);
        // La première capture (échauffement du pilote) n'est pas comptée ; ensuite, des dépassements consécutifs seulement.
        if (!warmedUp)
            warmedUp = true;
        else if (Date.now() - t0 > captureBudgetMs)
            overruns++;
        else
            overruns = 0;
        if (overruns >= maxOverruns)
            overBudget = true;
        if (!captured || overBudget) {
            ShockShot.release();
            return;
        }
        const w = screen.width;
        const h = screen.height;
        shotScreen = screen;
        centerX = Math.max(0, Math.min(1, x / w));
        centerY = Math.max(0, Math.min(1, y / h));
        farDistance = Math.max(1, Math.hypot(Math.max(x, w - x), Math.max(y, h - y)));
        lifeSeconds = Math.min(maxLifeSeconds, farDistance / waveSpeed + decaySeconds);
        startedAt = Date.now();
        active = true;
        safety.restart();
        loader.active = true;
    }

    function stop(): void {
        safety.stop();
        anim.stop();
        active = false;
        wave = 0;
        if (loader.item)
            loader.item.end();
        loader.active = false;
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
        onLoaded: if (root.active)
            item.begin()

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
                    anim.start();
                }
            }
            function end(): void {
                visible = false;
                image.source = "";
            }

            screen: root.shotScreen
            x: root.shotScreen ? root.shotScreen.virtualX : 0
            y: root.shotScreen ? root.shotScreen.virtualY : 0
            width: root.shotScreen ? root.shotScreen.width : 1
            height: root.shotScreen ? root.shotScreen.height : 1
            title: "Panneaux · Onde de choc"
            color: "transparent"
            visible: false
            flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint | Qt.WindowTransparentForInput | Qt.WindowDoesNotAcceptFocus

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
                property vector4d tint: Qt.vector4d(Colours.palette.m3primary.r, Colours.palette.m3primary.g, Colours.palette.m3primary.b, 1)
                property vector2d size: Qt.vector2d(width, height)
                property vector2d center: Qt.vector2d(root.centerX, root.centerY)
                property real time: root.wave
                property real speed: root.waveSpeed
                property real amplitude: root.waveAmplitude
                property real far: root.farDistance
                property real boost: root.brightness
                property real tintMix: root.tintAmount
                property real fade: Math.max(0, Math.min(1, (root.lifeSeconds - root.wave) / root.fadeSeconds))
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
