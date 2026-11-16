// Pilule : capsule en haut de l'écran, présente seulement quand quelque chose se passe (musique, volume, luminosité, micro).
// Elle passe d'un état à l'autre en animant taille, rayon et contenu : repos, média compact, média déployé, changement de piste,
// niveau. Fenêtre sans focus, masquée au repos (aucun clic bloqué), ni minuteur ni animation tant qu'il ne se passe rien.
pragma ComponentBehavior: Bound

import QtQuick
import QtQuick.Effects
import QtQuick.Window
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "PillMath.js" as PillMath
import "lyrics"

Window {
    id: root
    objectName: "livePill"
    title: "Panneaux · Pilule"
    transientParent: null
    color: "transparent"
    flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint | Qt.WindowDoesNotAcceptFocus

    readonly property int slimHeight: 40
    readonly property int discSize: 44
    readonly property int discRing: 2
    readonly property int discHole: 6
    readonly property int spinPeriodMs: 9000
    readonly property int spinStopMs: 900
    readonly property real hoverScale: 1.06
    readonly property int histBars: 7
    readonly property int histBarWidth: 3
    readonly property int histGap: 2
    readonly property int histHeight: 14
    // Bandes lues par l'histogramme compact : le bas et le milieu du spectre (le haut est presque muet), réparties en gammes
    // logarithmiques, avec un gain puis la courbe pow(v, histCurve) qui relève les niveaux faibles.
    readonly property int histSpan: 28
    readonly property real histGain: 1.6
    readonly property real histCurve: 0.6
    readonly property int closeSize: 28
    // Rayon du seul coin libre du bloc (bas gauche) ; les raccords concaves (Config.border.rounding) sont tracés par le cadre
    // (surface_geometry.py, pill_path) : la fenêtre est exactement le bloc, sans fond propre.
    readonly property real bodyRadius: Appearance.rounding.large
    readonly property real fillet: Config.border.rounding
    readonly property real tabVisibleMin: 0.5
    readonly property int levelWidth: 250
    readonly property int expandedWidth: 400
    readonly property int coverLarge: 88
    readonly property int lyricsHeight: 112
    readonly property int eqBarMin: 3
    readonly property int bandBars: 48
    readonly property int bandGap: 3
    readonly property int bandHeight: 64
    readonly property int bandBaseline: 3
    readonly property real bandTipAlpha: .55
    readonly property real bandFootAlpha: .04
    readonly property real tintAlpha: .16
    readonly property real tintReach: .62
    readonly property real glowOpacity: .4
    readonly property int controlSize: 36
    readonly property int playSize: 44
    readonly property int seekHitHeight: 16
    readonly property int progressHeight: 4
    readonly property int progressHoverHeight: 6
    readonly property int thumbSize: 12
    readonly property int slideDistance: 10
    readonly property int contentDelayMs: 80
    readonly property int wheelNotch: 120
    readonly property int wheelStep: 5
    readonly property real disabledAlpha: .38
    readonly property int mediaPageMinHeight: 95
    readonly property int levelBarHeight: 6
    readonly property int levelValueWidth: 40
    readonly property int coverIconGrade: 200
    readonly property real coverIconScale: 0.4
    readonly property real controlIconNudge: 0.05
    readonly property int pausedHoldMs: 4000
    readonly property int levelMs: 1500

    readonly property bool reduced: Appearance.reducedEffects
    readonly property var player: Players.active
    readonly property bool playing: player?.isPlaying ?? false
    readonly property bool suppressed: Bridge.quiet
    // Fenêtre du panneau du haut (tableau de bord, menus) : sa hauteur animée pilote la position de la pilule.
    readonly property var panel: Bridge.window
    // Le panneau est centré : il ne gêne la pilule du coin que sur les écrans assez étroits pour que leurs largeurs se chevauchent.
    // Bord droit réel du contenu du panneau (décalé vers la gauche par dashboardShift quand le rail est lié), comparé à la
    // position CIBLE de la pilule : le résultat ne bascule donc pas pendant l'animation de largeur.
    readonly property real panelRight: Bridge.screenX + Bridge.screenWidth / 2 + (panel?.panelWidth ?? 0) / 2 - (panel?.dashboardShift ?? 0)
    // Bord gauche cible du bloc, raccord concave compris.
    readonly property real targetX: Bridge.screenX + Bridge.screenWidth - frameInset - Math.round(targetWidth) - fillet
    // Sur un écran étroit, la languette se rétracte tant que le panneau du haut (collé au même bord) la gênerait.
    readonly property bool panelOverlaps: (panel?.visible ?? false) && panelRight + Appearance.spacing.normal > targetX
    // La page Média du tableau de bord ouverte fait doublon avec la pilule : elle s'efface.
    readonly property bool mediaPageShown: (panel?.visible ?? false) && panel.pageMode === "dashboard" && panel.dashboardTab === 1
        && panel.panelHeight >= mediaPageMinHeight

    property bool lingering: false
    property bool expandedWanted: false
    property string levelKind: "volume"
    property real levelValue: 0
    property bool levelMuted: false

    readonly property bool mediaWanted: (player?.available ?? false) && (playing || lingering || expandedWanted)
    // Priorité : niveau > déployé > disque > repos.
    readonly property string stateName: levelTimer.running ? "level" : mediaWanted && expandedWanted ? "expanded" : mediaWanted ? "compact"
        : "idle"
    property string shown: "idle"
    property real contentOpacity: 1

    readonly property string lyricsPhase: Bridge.lyrics?.state ?? "idle"
    // Zone de paroles de la carte : repliée sans paroles (off, none, idle).
    readonly property bool lyricsShown: lyricsPhase === "synced" || lyricsPhase === "plain" || lyricsPhase === "loading"
        || lyricsPhase === "instrumental"
    // Languette compacte : le disque, un espace, puis l'histogramme, dans la silhouette collée au cadre.
    readonly property real compactWidthWanted: discSize + Appearance.padding.normal * 2
    readonly property real compactHeightWanted: Appearance.padding.normal + discSize + Appearance.spacing.small + histHeight
        + Appearance.padding.normal
    readonly property real expandedHeight: Appearance.padding.large + coverLarge + Appearance.spacing.small + seekHitHeight + timeMetrics.height
        + (lyricsShown ? Appearance.spacing.small + lyricsHeight : 0) + Appearance.padding.larger
    // Largeur voulue (0 au repos) ; au repos la languette garde sa dernière largeur pendant qu'elle se rétracte.
    readonly property real wantedWidth: stateName === "expanded" ? expandedWidth : stateName === "level" ? levelWidth : stateName
        === "compact" ? compactWidthWanted : 0
    property real heldWidth: compactWidthWanted
    onWantedWidthChanged: {
        if (wantedWidth > 0)
            heldWidth = wantedWidth;
    }
    readonly property real targetWidth: wantedWidth > 0 ? wantedWidth : heldWidth
    // La languette sort du cadre (hauteur depuis 0) quand il y a quelque chose à montrer, et rentre sinon.
    readonly property bool tabWanted: !suppressed && !mediaPageShown && !panelOverlaps && stateName !== "idle"
    readonly property real targetHeight: !tabWanted ? 0 : stateName === "expanded" ? expandedHeight : stateName === "compact"
        ? compactHeightWanted : slimHeight
    // Lu par le pointeur (bridge_pointer.py) : la Pilule déployée est celle qu'on est en train de viser.
    readonly property bool expanded: stateName === "expanded"
    property real inlineValue: 0
    // Le spectre est aussi demandé tant que la page Média est ouverte : la couronne autour de sa pochette lit le même flux.
    readonly property bool wantsSpectrum: !suppressed && playing && (mediaPageShown || stateName === "compact" || stateName === "expanded")

    // La fenêtre épouse la taille animée du bloc : aucune zone morte autour, aucun clic avalé.
    width: Math.max(1, Math.round(pill.width))
    height: Math.max(1, Math.round(pill.height))
    // Bloc dans le coin haut droit : collé au bord intérieur haut ET au bord intérieur droit du cadre (ou de l'écran sans cadre),
    // il couvre le coin intérieur arrondi du cadre. Ancré par son coin haut droit : il s'étend vers la gauche et le bas.
    readonly property real frameInset: (Bridge.settings.screenFrame ?? true) ? Math.max(0, Bridge.frameThickness) : 0
    x: Math.round(Bridge.screenX + Bridge.screenWidth - frameInset - width)
    y: Math.round(Bridge.screenY + frameInset)
    visible: !suppressed && (tabWanted || pill.height > tabVisibleMin)

    function levelIcon() {
        if (levelKind === "brightness")
            return "brightness_medium";
        if (levelKind === "mic")
            return levelMuted ? "mic_off" : "mic";
        return levelMuted ? "no_sound" : levelValue <= 0 ? "volume_mute" : levelValue < 50 ? "volume_down" : "volume_up";
    }
    function fadeTime() {
        return reduced ? 0 : Appearance.anim.durations.expressiveFastEffects;
    }
    // Masquée, en silence ou sans lecteur : la carte se referme, elle ne revient jamais ouverte toute seule.
    function resetHover() {
        expandedWanted = false;
    }
    onSuppressedChanged: {
        if (suppressed)
            resetHover();
    }
    onMediaPageShownChanged: {
        if (mediaPageShown)
            resetHover();
    }
    onPlayerChanged: {
        if (!(player?.available ?? false)) {
            resetHover();
            lingering = false;
            holdTimer.stop();
        } else if (!playing && lingering) {
            holdTimer.restart();
        }
    }
    // Carte refermée sur une lecture en pause : la languette reste pausedHoldMs puis rentre dans le cadre.
    onExpandedWantedChanged: {
        if (!expandedWanted && !playing && (player?.available ?? false)) {
            lingering = true;
            holdTimer.restart();
        }
    }
    // Molette sur la pilule : volume du lecteur, affiché par le même retour de niveau que les touches de volume.
    function nudgeVolume(notches) {
        // Déployée, la carte reste ouverte et affiche le volume en ligne ; sinon, le retour de niveau habituel.
        const inline = expanded;
        const base = inline ? (inlineTimer.running ? inlineValue : (player?.volume ?? 0)) : levelTimer.running && levelKind === "volume"
            ? levelValue : (player?.volume ?? 0);
        const next = PillMath.stepVolume(base, notches, wheelStep);
        if (inline) {
            inlineValue = next;
            inlineTimer.restart();
        } else {
            levelKind = "volume";
            levelValue = next;
            levelMuted = false;
            levelTimer.restart();
        }
        Bridge.action("mediaVolume", {
            "value": next
        });
    }
    onStateNameChanged: {
        if (stateName === "idle")
            resetHover();
        if (shown === stateName)
            return;
        if (shown === "idle") {
            shown = stateName;
            contentOpacity = 0;
            fadeIn.restart();
        } else {
            swap.restart();
        }
    }
    onPlayingChanged: {
        if (playing) {
            lingering = true;
            holdTimer.stop();
        } else if (lingering) {
            holdTimer.restart();
        }
    }
    onWantsSpectrumChanged: Bridge.setPillSpectrum(wantsSpectrum)
    Component.onCompleted: {
        Bridge.setPillSpectrum(wantsSpectrum);
        lingering = playing;
        Bridge.mediaRef(true);
    }
    Component.onDestruction: {
        Bridge.setPillSpectrum(false);
        Bridge.mediaRef(false);
    }

    // Le contenu sortant s'efface avant que le suivant n'apparaisse ; la taille, elle, anime tout de suite vers la cible.
    TextMetrics {
        id: timeMetrics
        text: "0"
        font.family: Appearance.font.family.sans
        font.pointSize: Appearance.font.size.small
    }
    SequentialAnimation {
        id: swap
        NumberAnimation {
            target: root
            property: "contentOpacity"
            to: 0
            duration: root.fadeTime()
            easing.type: Easing.BezierSpline
            easing.bezierCurve: Appearance.anim.curves.standardAccel
        }
        ScriptAction {
            script: root.shown = root.stateName
        }
        PauseAnimation {
            duration: root.reduced ? 0 : root.contentDelayMs
        }
        NumberAnimation {
            target: root
            property: "contentOpacity"
            to: 1
            duration: root.fadeTime()
            easing.type: Easing.BezierSpline
            easing.bezierCurve: Appearance.anim.curves.standardDecel
        }
    }
    NumberAnimation {
        id: fadeIn
        target: root
        property: "contentOpacity"
        to: 1
        duration: root.fadeTime()
        easing.type: Easing.BezierSpline
        easing.bezierCurve: Appearance.anim.curves.standardDecel
    }

    Timer {
        id: holdTimer
        interval: root.pausedHoldMs
        onTriggered: root.lingering = false
    }
    Timer {
        id: levelTimer
        interval: root.levelMs
    }
    Timer {
        id: inlineTimer
        interval: root.levelMs
    }
    // Position du lecteur : mise à jour seulement quand la pilule déployée est visible et que la musique joue.
    Timer {
        running: root.stateName === "expanded" && root.playing
        interval: Config.dashboard.mediaUpdateInterval
        triggeredOnStart: true
        repeat: true
        onTriggered: root.player?.positionChanged()
    }

    Connections {
        target: Bridge
        function onShellEvent(event) {
            if (!event.startsWith("osd:") || !(Bridge.settings.osd ?? true) || !(Bridge.settings.pill ?? true))
                return;
            var p = event.split(":");
            if (p[1] !== "volume" && p[1] !== "brightness" && p[1] !== "mic")
                return;
            root.levelKind = p[1];
            root.levelValue = Math.max(0, Math.min(100, Number(p[2])));
            root.levelMuted = p[3] === "1";
            levelTimer.restart();
        }
    }

    // Pas de fond ici : ScreenFrame trace le bloc dans la même silhouette que le cadre (surface_geometry.py, pill_path),
    // donc un seul remplissage et un seul contour, sans couture.
    Item {
        id: pill
        anchors.top: parent.top
        anchors.right: parent.right
        width: root.targetWidth
        height: root.targetHeight

        Behavior on width {
            enabled: !root.reduced
            Anim {
                duration: Appearance.anim.durations.expressiveDefaultSpatial
                easing.bezierCurve: Appearance.anim.curves.expressiveDefaultSpatial
            }
        }
        // Courbe standard : une carte qui grandit quand les paroles arrivent ne rebondit pas.
        Behavior on height {
            enabled: !root.reduced
            Anim {
                duration: Appearance.anim.durations.expressiveDefaultSpatial
                easing.bezierCurve: Appearance.anim.curves.standard
            }
        }

        HoverHandler {
            id: hover
            enabled: root.stateName === "compact"
        }
        property real wheelAcc: 0
        WheelHandler {
            enabled: root.mediaWanted && (root.player?.available ?? false)
            acceptedDevices: PointerDevice.Mouse | PointerDevice.TouchPad
            onWheel: event => {
                pill.wheelAcc += event.angleDelta.y;
                const notches = Math.trunc(pill.wheelAcc / root.wheelNotch);
                if (notches === 0)
                    return;
                pill.wheelAcc -= notches * root.wheelNotch;
                root.nudgeVolume(notches);
            }
        }
        MouseArea {
            anchors.fill: parent
            enabled: root.stateName === "compact"
            cursorShape: Qt.PointingHandCursor
            onClicked: root.expandedWanted = true
        }

        // Contenu rogné par le seul coin libre du bloc, en bas à gauche (les autres coins sont repoussés hors de la fenêtre).
        StyledClippingRect {
            x: 0
            y: -root.bodyRadius
            width: pill.width + root.bodyRadius
            height: pill.height + root.bodyRadius
            radius: root.bodyRadius
        Item {
            id: content
            y: root.bodyRadius
            width: pill.width
            height: pill.height

        // Média compact : un disque (pochette en rotation lente tant que la musique joue) et, dessous, un petit histogramme.
        // Rien d'autre : ni texte ni carte. Le clic sur le disque ouvre la carte.
        Item {
            id: compactBox
            width: root.compactWidthWanted
            height: root.compactHeightWanted
            anchors.top: parent.top
            anchors.horizontalCenter: parent.horizontalCenter
            opacity: root.shown === "compact" ? root.contentOpacity : 0
            visible: opacity > 0
            Disc {
                id: disc
                anchors.horizontalCenter: parent.horizontalCenter
                y: Appearance.padding.normal
                art: root.player?.trackArtUrl ?? ""
                spinning: root.playing && root.shown === "compact" && root.visible && !root.reduced
                scale: hover.hovered && !root.reduced ? root.hoverScale : 1
                Behavior on scale {
                    enabled: !root.reduced
                    Anim {
                        duration: Appearance.anim.durations.small
                    }
                }
            }
            Equaliser {
                anchors.horizontalCenter: parent.horizontalCenter
                anchors.top: disc.bottom
                anchors.topMargin: Appearance.spacing.small
                width: root.histBars * root.histBarWidth + (root.histBars - 1) * root.histGap
                height: root.histHeight
                count: root.histBars
                barGap: root.histGap
                barMin: root.eqBarMin
                span: root.histSpan
                gain: root.histGain
                curve: root.histCurve
                spread: true
                animated: !root.reduced
                live: root.playing && root.shown === "compact" && root.visible
                fill: true
                bottomAligned: true
            }
        }

        // Volume, luminosité, micro : barre de niveau horizontale.
        Item {
            width: root.levelWidth
            height: root.slimHeight
            anchors.top: parent.top
            anchors.horizontalCenter: parent.horizontalCenter
            opacity: root.shown === "level" ? root.contentOpacity : 0
            visible: opacity > 0
            MaterialIcon {
                id: levelGlyph
                anchors.left: parent.left
                anchors.leftMargin: Appearance.padding.large
                anchors.verticalCenter: parent.verticalCenter
                text: root.levelIcon()
                color: Colours.palette.m3primary
                font.pointSize: Appearance.font.size.large
            }
            StyledText {
                id: levelText
                anchors.right: parent.right
                anchors.rightMargin: Appearance.padding.large
                anchors.verticalCenter: parent.verticalCenter
                width: root.levelValueWidth
                horizontalAlignment: Text.AlignRight
                text: Math.round(root.levelMuted ? 0 : root.levelValue) + "%"
                color: Colours.palette.m3onSurface
            }
            StyledRect {
                anchors.left: levelGlyph.right
                anchors.leftMargin: Appearance.spacing.small
                anchors.right: levelText.left
                anchors.rightMargin: Appearance.spacing.small
                anchors.verticalCenter: parent.verticalCenter
                height: root.levelBarHeight
                radius: Appearance.rounding.full
                color: Colours.layer(Colours.palette.m3surfaceContainerHigh, 2)
                StyledRect {
                    anchors.left: parent.left
                    anchors.top: parent.top
                    anchors.bottom: parent.bottom
                    radius: Appearance.rounding.full
                    color: Colours.palette.m3primary
                    width: parent.width * (root.levelMuted ? 0 : root.levelValue / 100)
                    Behavior on width {
                        enabled: !root.reduced
                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }
                }
            }
        }

        // Média déployé : voile de couleur en tête, pochette et son halo, titre, artiste, commandes, progression, paroles ;
        // le spectre court le long du bord bas, découpé par les coins arrondis de la carte.
        Item {
            id: card
            width: root.expandedWidth
            // Suit la hauteur animée de la carte : le bord bas (spectre) grandit avec elle quand les paroles arrivent.
            height: Math.max(pill.height, 1)
            anchors.top: parent.top
            anchors.horizontalCenter: parent.horizontalCenter
            opacity: root.shown === "expanded" ? root.contentOpacity : 0
            // Présente dès l'ouverture demandée (opacité nulle) : les paroles sont réclamées sans attendre la fin du fondu.
            visible: opacity > 0 || root.expandedWanted
            readonly property real inner: width - Appearance.padding.large * 2
            readonly property real position: seek.dragging ? seek.dragFraction * (root.player?.length ?? 0) : (root.player?.position ?? 0)

            Rectangle {
                width: parent.width
                height: parent.height * root.tintReach
                gradient: Gradient {
                    orientation: Gradient.Vertical
                    GradientStop {
                        position: 0
                        color: Qt.alpha(Colours.palette.m3primary, root.tintAlpha)
                    }
                    GradientStop {
                        position: 1
                        color: Qt.alpha(Colours.palette.m3primary, 0)
                    }
                }
            }
            Equaliser {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.bottom: parent.bottom
                height: root.bandHeight
                count: root.bandBars
                barGap: root.bandGap
                barMin: root.bandBaseline
                animated: !root.reduced
                live: root.playing && root.shown === "expanded"
                fill: true
                bottomAligned: true
                faded: true
            }
            Cover {
                id: bigCover
                size: root.coverLarge
                x: Appearance.padding.large
                y: Appearance.padding.large
                circle: false
                glow: true
                art: root.player?.trackArtUrl ?? ""
                // Cliquer la pochette referme la carte : elle devient de nouveau le disque.
                MouseArea {
                    anchors.fill: parent
                    cursorShape: Qt.PointingHandCursor
                    onClicked: root.expandedWanted = false
                }
            }
            Control {
                id: closeButton
                anchors.top: parent.top
                anchors.topMargin: Appearance.spacing.smaller
                anchors.right: parent.right
                anchors.rightMargin: Appearance.spacing.smaller
                implicitWidth: root.closeSize
                icon: "close"
                canUse: true
                function onClicked(): void {
                    root.expandedWanted = false;
                }
            }
            Column {
                anchors.left: bigCover.right
                anchors.leftMargin: Appearance.spacing.normal
                anchors.right: closeButton.left
                anchors.rightMargin: Appearance.spacing.smaller
                anchors.top: bigCover.top
                spacing: 0
                SwapText {
                    width: parent.width
                    elide: Text.ElideRight
                    wanted: root.player ? (root.player.trackTitle || qsTr("Unknown title")) : ""
                    color: Colours.palette.m3onSurface
                    font.pointSize: Appearance.font.size.large
                    font.weight: Font.Bold
                }
                SwapText {
                    width: parent.width
                    elide: Text.ElideRight
                    wanted: (root.player?.trackArtist ?? "") || qsTr("Unknown artist")
                    color: Colours.palette.m3onSurfaceVariant
                    font.pointSize: Appearance.font.size.small
                }
            }
            Row {
                anchors.left: bigCover.right
                anchors.leftMargin: Appearance.spacing.small
                anchors.bottom: bigCover.bottom
                spacing: Appearance.spacing.smaller
                Control {
                    anchors.verticalCenter: parent.verticalCenter
                    icon: "skip_previous"
                    canUse: root.player?.canGoPrevious ?? false
                    function onClicked(): void {
                        root.player?.previous();
                    }
                }
                Control {
                    anchors.verticalCenter: parent.verticalCenter
                    emphasis: true
                    icon: root.playing ? "pause" : "play_arrow"
                    canUse: root.player?.canTogglePlaying ?? false
                    function onClicked(): void {
                        root.player?.togglePlaying();
                    }
                }
                Control {
                    anchors.verticalCenter: parent.verticalCenter
                    icon: "skip_next"
                    canUse: root.player?.canGoNext ?? false
                    function onClicked(): void {
                        root.player?.next();
                    }
                }
            }
            // Progression : piste fine qui s'épaissit au survol, poignée, clic ou glissé pour se déplacer si le lecteur le permet.
            Item {
                id: seek
                x: Appearance.padding.large
                width: card.inner
                anchors.top: bigCover.bottom
                anchors.topMargin: Appearance.spacing.small
                height: root.seekHitHeight
                readonly property bool canSeek: (root.player?.canSeek ?? false) && (root.player?.lengthKnown ?? false)
                readonly property bool engaged: dragging || seekArea.containsMouse
                property bool dragging: false
                property real dragFraction: 0
                readonly property real shownFraction: dragging ? dragFraction : PillMath.fraction(root.player?.position ?? 0,
                    root.player?.lengthKnown ? root.player.length : 0)
                StyledRect {
                    anchors.verticalCenter: parent.verticalCenter
                    width: parent.width
                    height: seek.engaged ? root.progressHoverHeight : root.progressHeight
                    radius: Appearance.rounding.full
                    color: Colours.palette.m3surfaceContainerHighest
                    Behavior on height {
                        enabled: !root.reduced
                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }
                    StyledRect {
                        anchors.left: parent.left
                        anchors.top: parent.top
                        anchors.bottom: parent.bottom
                        radius: Appearance.rounding.full
                        color: Colours.palette.m3primary
                        width: parent.width * seek.shownFraction
                        Behavior on width {
                            enabled: !root.reduced && !seek.dragging
                            Anim {
                                duration: Appearance.anim.durations.small
                            }
                        }
                    }
                }
                Rectangle {
                    width: root.thumbSize
                    height: root.thumbSize
                    radius: Appearance.rounding.full
                    anchors.verticalCenter: parent.verticalCenter
                    x: Math.max(0, Math.min(seek.width - width, seek.width * seek.shownFraction - width / 2))
                    color: Colours.palette.m3onSurface
                    opacity: seek.engaged && seek.canSeek ? 1 : 0
                    Behavior on opacity {
                        enabled: !root.reduced
                        Anim {
                            duration: Appearance.anim.durations.small
                        }
                    }
                }
                MouseArea {
                    id: seekArea
                    anchors.fill: parent
                    enabled: seek.canSeek
                    hoverEnabled: true
                    cursorShape: Qt.PointingHandCursor
                    function fractionAt(x: real): real {
                        return Math.max(0, Math.min(1, x / width));
                    }
                    onPressed: mouse => {
                        seek.dragFraction = fractionAt(mouse.x);
                        seek.dragging = true;
                    }
                    onPositionChanged: mouse => {
                        if (pressed)
                            seek.dragFraction = fractionAt(mouse.x);
                    }
                    onReleased: {
                        if (seek.dragging && root.player)
                            root.player.position = seek.dragFraction * root.player.length;
                        seek.dragging = false;
                    }
                    onCanceled: seek.dragging = false
                }
            }
            StyledText {
                id: elapsed
                x: Appearance.padding.large
                anchors.top: seek.bottom
                text: PillMath.clock(card.position)
                color: Colours.palette.m3onSurfaceVariant
                font.pointSize: Appearance.font.size.small
            }
            // Volume en ligne (molette) : icône et pourcentage entre les deux temps, la carte reste ouverte.
            Row {
                anchors.horizontalCenter: parent.horizontalCenter
                anchors.top: seek.bottom
                spacing: Appearance.spacing.smaller
                opacity: inlineTimer.running ? 1 : 0
                visible: opacity > 0
                Behavior on opacity {
                    enabled: !root.reduced
                    Anim {
                        duration: Appearance.anim.durations.small
                    }
                }
                MaterialIcon {
                    anchors.verticalCenter: parent.verticalCenter
                    text: root.inlineValue <= 0 ? "volume_mute" : root.inlineValue < 50 ? "volume_down" : "volume_up"
                    color: Colours.palette.m3primary
                    font.pointSize: Appearance.font.size.normal
                }
                StyledText {
                    anchors.verticalCenter: parent.verticalCenter
                    text: Math.round(root.inlineValue) + "%"
                    color: Colours.palette.m3primary
                    font.pointSize: Appearance.font.size.small
                }
            }
            StyledText {
                anchors.right: parent.right
                anchors.rightMargin: Appearance.padding.large
                anchors.top: seek.bottom
                text: root.player?.lengthKnown ? PillMath.clock(root.player.length) : PillMath.clock(-1)
                color: Colours.palette.m3onSurfaceVariant
                font.pointSize: Appearance.font.size.small
            }
            // Paroles : zone repliée quand il n'y en a pas ; ligne en cours ancrée au centre, voisines estompées.
            LyricsView {
                anchors.left: parent.left
                anchors.leftMargin: Appearance.padding.large
                anchors.right: parent.right
                anchors.rightMargin: Appearance.padding.large
                anchors.top: elapsed.bottom
                anchors.topMargin: Appearance.spacing.small
                height: root.lyricsHeight
                opacity: root.lyricsShown ? 1 : 0
                active: root.visible && (root.shown === "expanded" || root.expandedWanted)
                fontSize: Appearance.font.size.normal
                Behavior on opacity {
                    enabled: !root.reduced
                    Anim {
                        duration: Appearance.anim.durations.normal
                    }
                }
            }
        }
        }
        }
    }

    // Disque façon vinyle : pochette ronde qui tourne, anneau fin et petit axe central. L'animation ne tourne que si `spinning` ;
    // à l'arrêt, une courte inertie (même vitesse au départ) finit le mouvement, et la reprise repart de l'angle atteint.
    component Disc: Item {
        id: vinyl
        property url art
        property bool spinning: false
        property real angle: 0
        readonly property real inertiaDegrees: 180 * root.spinStopMs / root.spinPeriodMs
        implicitWidth: root.discSize
        implicitHeight: root.discSize
        width: root.discSize
        height: root.discSize
        onSpinningChanged: {
            if (spinning) {
                stopAnim.stop();
                angle = angle % 360;
                spinAnim.from = angle;
                spinAnim.to = angle + 360;
                spinAnim.restart();
            } else {
                spinAnim.stop();
                if (root.visible && !root.reduced) {
                    stopAnim.to = angle + inertiaDegrees;
                    stopAnim.restart();
                }
            }
        }
        NumberAnimation {
            id: spinAnim
            target: vinyl
            property: "angle"
            duration: root.spinPeriodMs
            loops: Animation.Infinite
            easing.type: Easing.Linear
        }
        NumberAnimation {
            id: stopAnim
            target: vinyl
            property: "angle"
            duration: root.spinStopMs
            easing.type: Easing.OutQuad
        }
        Cover {
            size: root.discSize
            anchors.fill: parent
            circle: true
            art: vinyl.art
            rotation: vinyl.angle
        }
        Rectangle {
            anchors.fill: parent
            radius: Appearance.rounding.full
            color: "transparent"
            border.width: root.discRing
            border.color: Colours.palette.m3outlineVariant
        }
        Rectangle {
            anchors.centerIn: parent
            width: root.discHole
            height: root.discHole
            radius: Appearance.rounding.full
            color: Colours.palette.m3surfaceContainer
            border.width: 1
            border.color: Colours.palette.m3outlineVariant
        }
    }

    // Pochette : cercle (compact) ou carré arrondi (déployé), icône de repli tant qu'aucune image n'est disponible.
    // Au changement de piste, la nouvelle image apparaît en fondu par-dessus l'ancienne.
    // Le rognage (ClippingRectangle) vit dans un enfant : la couche du halo ne remplace plus son masque (c'était ce qui
    // laissait la pochette carrée).
    component Cover: Item {
        id: cover
        required property int size
        property bool circle: true
        property url art
        // Halo doux de la couleur d'accent derrière la pochette.
        property bool glow: false
        implicitWidth: size
        implicitHeight: size
        layer.enabled: glow
        layer.effect: MultiEffect {
            shadowEnabled: true
            shadowColor: Colours.palette.m3primary
            shadowOpacity: root.glowOpacity
            shadowBlur: 1
            shadowVerticalOffset: Appearance.spacing.smaller / 2
        }
        onArtChanged: {
            previousImage.source = String(art) === "" ? "" : currentImage.source;
            currentImage.source = art;
        }
        Component.onCompleted: currentImage.source = art
        StyledClippingRect {
            anchors.fill: parent
            radius: cover.circle ? cover.size / 2 : Appearance.rounding.normal
            color: Colours.tPalette.m3surfaceContainerHigh
            MaterialIcon {
                anchors.centerIn: parent
                grade: root.coverIconGrade
                text: "art_track"
                color: Colours.palette.m3onSurfaceVariant
                font.pointSize: (parent.width * root.coverIconScale) || 1
            }
            Image {
                id: previousImage
                anchors.fill: parent
                asynchronous: true
                fillMode: Image.PreserveAspectCrop
                sourceSize.width: width
                sourceSize.height: height
            }
            Image {
                id: currentImage
                anchors.fill: parent
                asynchronous: true
                fillMode: Image.PreserveAspectCrop
                sourceSize.width: width
                sourceSize.height: height
                opacity: status === Image.Ready ? 1 : 0
                Behavior on opacity {
                    enabled: !root.reduced
                    Anim {
                        duration: Appearance.anim.durations.expressiveSlowEffects
                    }
                }
            }
        }
    }

    // Égaliseur : `count` barres arrondies m3primary regroupées depuis les 48 bandes ; barres plates quand rien ne joue.
    // fill : les barres se partagent toute la largeur (écart barGap) ; bottomAligned : posées sur le bas au lieu d'être centrées.
    component Equaliser: Item {
        id: eq
        property int count: 6
        property bool live: false
        property bool fill: false
        property bool bottomAligned: false
        property int barWidth: 3
        property int barGap: 3
        property int barMin: 2
        property bool animated: true
        property bool faded: false
        // span : nombre de bandes lues depuis le bas du spectre ; gain : amplification douce du niveau.
        property int span: 48
        property real gain: 1
        // spread : gammes logarithmiques, gain puis pow(v, curve) (PillMath.spread) au lieu de la moyenne simple.
        property bool spread: false
        property real curve: 0.5
        readonly property var values: live ? (Bridge.spectrum ?? []).slice(0, span) : []
        readonly property real cell: fill ? Math.max(1, (width - (count - 1) * barGap) / count) : barWidth
        readonly property real step: fill ? cell + barGap : barWidth * 2
        implicitWidth: count * barWidth + (count - 1) * barWidth
        Repeater {
            model: eq.count
            delegate: StyledRect {
                required property int index
                x: index * eq.step
                width: eq.cell
                height: Math.max(eq.barMin, eq.spread ? PillMath.spread(eq.values, index, eq.count, eq.span, eq.gain, eq.curve) * eq.height
                    : Math.min(1, PillMath.level(eq.values, index, eq.count) * eq.gain) * eq.height)
                anchors.verticalCenter: eq.bottomAligned ? undefined : eq.verticalCenter
                anchors.bottom: eq.bottomAligned ? eq.bottom : undefined
                radius: Appearance.rounding.full
                color: eq.faded ? "transparent" : Colours.palette.m3primary
                // faded : la pointe est lumineuse et le pied se fond dans la carte.
                gradient: eq.faded ? fade : null
                property Gradient fade: Gradient {
                    orientation: Gradient.Vertical
                    GradientStop {
                        position: 0
                        color: Qt.alpha(Colours.palette.m3primary, root.bandTipAlpha)
                    }
                    GradientStop {
                        position: 1
                        color: Qt.alpha(Colours.palette.m3primary, root.bandFootAlpha)
                    }
                }
                Behavior on height {
                    enabled: eq.animated
                    Anim {
                        duration: Appearance.anim.durations.expressiveFastEffects
                        easing.bezierCurve: Appearance.anim.curves.expressiveFastEffects
                    }
                }
            }
        }
    }

    // Texte qui change de valeur en glissant : l'ancien sort vers la gauche en s'effaçant, le nouveau arrive de la droite.
    component SwapText: StyledText {
        id: swapText
        property string wanted
        property real shift: 0
        transform: Translate {
            x: swapText.shift
        }
        onWantedChanged: {
            if (text === "" || root.reduced || !swapText.visible || !root.visible)
                text = wanted;
            else
                swapAnim.restart();
        }
        Component.onCompleted: text = wanted
        SequentialAnimation {
            id: swapAnim
            ParallelAnimation {
                Anim {
                    target: swapText
                    property: "opacity"
                    to: 0
                    duration: Appearance.anim.durations.small
                    easing.bezierCurve: Appearance.anim.curves.standardAccel
                }
                Anim {
                    target: swapText
                    property: "shift"
                    to: -root.slideDistance
                    duration: Appearance.anim.durations.small
                    easing.bezierCurve: Appearance.anim.curves.standardAccel
                }
            }
            ScriptAction {
                script: {
                    swapText.text = swapText.wanted;
                    swapText.shift = root.slideDistance;
                }
            }
            ParallelAnimation {
                Anim {
                    target: swapText
                    property: "opacity"
                    to: 1
                    duration: Appearance.anim.durations.normal
                    easing.bezierCurve: Appearance.anim.curves.standardDecel
                }
                Anim {
                    target: swapText
                    property: "shift"
                    to: 0
                    duration: Appearance.anim.durations.normal
                    easing.bezierCurve: Appearance.anim.curves.standardDecel
                }
            }
        }
    }

    // Commande ronde : couche d'état au survol et à l'appui ; la lecture/pause est pleine (m3primary), les autres sont discrètes.
    component Control: StyledRect {
        id: control
        required property string icon
        required property bool canUse
        property bool emphasis: false
        function onClicked(): void {
        }
        implicitWidth: emphasis ? root.playSize : root.controlSize
        implicitHeight: implicitWidth
        radius: Appearance.rounding.full
        color: emphasis ? Qt.alpha(Colours.palette.m3primary, canUse ? 1 : root.disabledAlpha) : "transparent"
        StateLayer {
            color: control.emphasis ? Colours.palette.m3onPrimary : Colours.palette.m3onSurface
            disabled: !control.canUse
            radius: Appearance.rounding.full
            function onClicked(): void {
                control.onClicked();
            }
        }
        MaterialIcon {
            id: ctrlIcon
            anchors.centerIn: parent
            anchors.verticalCenterOffset: font.pointSize * root.controlIconNudge
            animate: true
            text: control.icon
            color: control.emphasis ? Colours.palette.m3onPrimary : control.canUse ? Colours.palette.m3onSurface : Colours.palette.m3outline
            font.pointSize: control.emphasis ? Appearance.font.size.extraLarge : Appearance.font.size.large
        }
    }
}
