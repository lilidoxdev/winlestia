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
    readonly property int coverSmall: 30
    readonly property int compactWidth: 128
    readonly property int compactMaxWidth: 320
    readonly property int compactEqWidth: 33
    readonly property int flashWidth: 360
    readonly property int levelWidth: 250
    readonly property int expandedWidth: 400
    readonly property int coverLarge: 88
    readonly property int lyricsHeight: 112
    readonly property int eqCompactBars: 6
    readonly property int eqBarWidth: 3
    readonly property int eqBarMin: 2
    readonly property int eqCompactHeight: 18
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
    readonly property real hiddenScale: .92
    readonly property real disabledAlpha: .38
    readonly property int mediaPageMinHeight: 95
    readonly property int levelBarHeight: 6
    readonly property int levelValueWidth: 40
    readonly property real glassOpacity: .94
    readonly property int coverIconGrade: 200
    readonly property real coverIconScale: 0.4
    readonly property real controlIconNudge: 0.05
    readonly property int hoverIntentMs: 220
    readonly property int hoverLeaveMs: 450
    readonly property int pausedHoldMs: 5000
    readonly property int flashMs: 2600
    readonly property int levelMs: 1500

    readonly property bool reduced: Appearance.reducedEffects
    readonly property var player: Players.active
    readonly property bool playing: player?.isPlaying ?? false
    readonly property bool suppressed: Bridge.quiet
    // Fenêtre du panneau du haut (tableau de bord, menus) : sa hauteur animée pilote la position de la pilule.
    readonly property var panel: Bridge.window
    readonly property real panelBottom: (panel?.visible ?? false) ? panel.y + panel.panelHeight : 0
    // La page Média du tableau de bord ouverte fait doublon avec la pilule : elle s'efface.
    readonly property bool mediaPageShown: (panel?.visible ?? false) && panel.pageMode === "dashboard" && panel.dashboardTab === 1
        && panel.panelHeight >= mediaPageMinHeight
    readonly property string trackKey: (player?.trackTitle ?? "") + "\u0000" + (player?.trackArtist ?? "")

    property bool lingering: false
    property bool expandedWanted: false
    property string levelKind: "volume"
    property real levelValue: 0
    property bool levelMuted: false
    property string previousKey: ""

    readonly property bool mediaWanted: (player?.available ?? false) && (playing || lingering || expandedWanted)
    // Priorité : niveau > déployé > changement de piste > compact > repos.
    readonly property string stateName: levelTimer.running ? "level" : mediaWanted && expandedWanted ? "expanded" : mediaWanted
        && flashTimer.running ? "flash" : mediaWanted ? "compact" : "idle"
    property string shown: "idle"
    property real contentOpacity: 1

    readonly property string lyricsPhase: Bridge.lyrics?.state ?? "idle"
    // Zone de paroles de la carte : repliée sans paroles (off, none, idle).
    readonly property bool lyricsShown: lyricsPhase === "synced" || lyricsPhase === "plain" || lyricsPhase === "loading"
        || lyricsPhase === "instrumental"
    readonly property string fallbackLine: ((player?.trackTitle ?? "") + ((player?.trackArtist ?? "") !== "" ? " · " + player.trackArtist
        : ""))
    // Compact : largeur fixe avec des paroles synchronisées (la pilule ne bouge pas à chaque ligne), sinon ajustée au titre.
    readonly property real compactWidthWanted: lyricsPhase === "synced" ? compactMaxWidth : fallbackLine === "" ? compactWidth
        : Math.max(compactWidth, Math.min(compactMaxWidth, slimHeight + compactEqWidth + Appearance.padding.larger + Appearance.spacing.small * 2
        + Math.ceil(compactMetrics.advanceWidth)))
    readonly property real expandedHeight: Appearance.padding.large + coverLarge + Appearance.spacing.small + seekHitHeight + timeMetrics.height
        + (lyricsShown ? Appearance.spacing.small + lyricsHeight : 0) + Appearance.padding.larger
    readonly property real targetWidth: stateName === "expanded" ? expandedWidth : stateName === "flash" ? flashWidth : stateName
        === "level" ? levelWidth : stateName === "compact" ? compactWidthWanted : slimHeight
    readonly property real targetHeight: stateName === "expanded" ? expandedHeight : slimHeight
    readonly property real targetRadius: stateName === "expanded" ? Appearance.rounding.large : targetHeight / 2
    // Lu par le pointeur (bridge_pointer.py) : la Pilule déployée est celle qu'on est en train de viser.
    readonly property bool expanded: stateName === "expanded"
    property real inlineValue: 0
    // Le spectre est aussi demandé tant que la page Média est ouverte : la couronne autour de sa pochette lit le même flux.
    readonly property bool wantsSpectrum: !suppressed && playing && (mediaPageShown || stateName === "compact" || stateName === "flash"
        || stateName === "expanded")

    // La fenêtre épouse la taille animée de la capsule : aucune zone morte autour, aucun clic avalé.
    width: Math.max(1, pill.width)
    height: Math.max(1, pill.height)
    x: Bridge.screenX + (Bridge.screenWidth - width) / 2
    // Avec le cadre d'écran, la pilule est suspendue à son bord haut ; sans cadre, petite marge depuis le haut de l'écran.
    readonly property real restY: Bridge.screenY + ((Bridge.settings.screenFrame ?? true) ? Math.max(0, Bridge.frameThickness)
        : Appearance.spacing.normal)
    // Le panneau du haut descend : la pilule le suit, juste en dessous, dans le même mouvement (aucun minuteur propre).
    y: Math.round(PillMath.followY(restY, panelBottom, Appearance.spacing.normal))
    visible: !suppressed && ((stateName !== "idle" && !mediaPageShown) || pill.opacity > 0.01)

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
    // Masquée ou en silence : plus de survol en attente, elle ne doit jamais revenir déployée sans survol.
    function resetHover() {
        enterTimer.stop();
        leaveTimer.stop();
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
    onTrackKeyChanged: {
        if (previousKey !== "" && (player?.trackTitle ?? "") !== "" && mediaWanted && !expandedWanted)
            flashTimer.restart();
        previousKey = trackKey;
    }
    onWantsSpectrumChanged: Bridge.setPillSpectrum(wantsSpectrum)
    Component.onCompleted: {
        Bridge.setPillSpectrum(wantsSpectrum);
        previousKey = trackKey;
        lingering = playing;
        Bridge.mediaRef(true);
    }
    Component.onDestruction: {
        Bridge.setPillSpectrum(false);
        Bridge.mediaRef(false);
    }

    // Le contenu sortant s'efface avant que le suivant n'apparaisse ; la taille, elle, anime tout de suite vers la cible.
    TextMetrics {
        id: compactMetrics
        text: root.fallbackLine
        font.family: Appearance.font.family.sans
        font.pointSize: Appearance.font.size.normal
        font.weight: Font.DemiBold
    }
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
        id: flashTimer
        interval: root.flashMs
    }
    Timer {
        id: levelTimer
        interval: root.levelMs
    }
    Timer {
        id: inlineTimer
        interval: root.levelMs
    }
    Timer {
        id: enterTimer
        interval: root.hoverIntentMs
        onTriggered: root.expandedWanted = true
    }
    Timer {
        id: leaveTimer
        interval: root.hoverLeaveMs
        onTriggered: root.expandedWanted = false
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

    StyledClippingRect {
        id: pill
        anchors.top: parent.top
        anchors.horizontalCenter: parent.horizontalCenter
        width: root.targetWidth
        height: root.targetHeight
        radius: Math.max(0, root.targetRadius)
        color: Qt.alpha(Colours.palette.m3surfaceContainer, Bridge.glassEnabled ? root.glassOpacity : 1)
        border.width: 1
        border.color: Colours.palette.m3outlineVariant
        opacity: root.stateName === "idle" || root.mediaPageShown ? 0 : 1
        scale: root.mediaPageShown ? root.hiddenScale : 1

        Behavior on scale {
            enabled: !root.reduced
            Anim {
                duration: Appearance.anim.durations.expressiveEffects
                easing.bezierCurve: Appearance.anim.curves.expressiveEffects
            }
        }
        Behavior on width {
            enabled: !root.reduced
            Anim {
                duration: Appearance.anim.durations.expressiveDefaultSpatial
                easing.bezierCurve: Appearance.anim.curves.expressiveDefaultSpatial
            }
        }
        Behavior on height {
            enabled: !root.reduced
            Anim {
                duration: Appearance.anim.durations.expressiveDefaultSpatial
                easing.bezierCurve: Appearance.anim.curves.expressiveDefaultSpatial
            }
        }
        Behavior on radius {
            enabled: !root.reduced
            Anim {
                duration: Appearance.anim.durations.expressiveDefaultSpatial
                easing.bezierCurve: Appearance.anim.curves.expressiveDefaultSpatial
            }
        }
        Behavior on opacity {
            enabled: !root.reduced
            Anim {
                duration: Appearance.anim.durations.expressiveEffects
                easing.bezierCurve: Appearance.anim.curves.expressiveEffects
            }
        }

        HoverHandler {
            id: hover
            enabled: root.mediaWanted
            onHoveredChanged: {
                if (hovered) {
                    leaveTimer.stop();
                    enterTimer.restart();
                } else {
                    enterTimer.stop();
                    leaveTimer.restart();
                }
            }
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
            enabled: root.stateName === "compact" || root.stateName === "flash"
            cursorShape: Qt.PointingHandCursor
            onClicked: {
                leaveTimer.stop();
                root.expandedWanted = true;
            }
        }

        // Média compact : pochette ronde, ligne de paroles en cours (repli titre · artiste) qui glisse, mini égaliseur.
        Item {
            width: pill.width
            height: root.slimHeight
            anchors.centerIn: parent
            opacity: root.shown === "compact" ? root.contentOpacity : 0
            visible: opacity > 0
            Cover {
                id: compactCover
                size: root.coverSmall
                anchors.left: parent.left
                anchors.leftMargin: (root.slimHeight - root.coverSmall) / 2
                anchors.verticalCenter: parent.verticalCenter
                circle: true
                art: root.player?.trackArtUrl ?? ""
            }
            Equaliser {
                id: compactEq
                anchors.right: parent.right
                anchors.rightMargin: Appearance.padding.larger
                anchors.verticalCenter: parent.verticalCenter
                count: root.eqCompactBars
                barWidth: root.eqBarWidth
                barMin: root.eqBarMin
                animated: !root.reduced
                height: root.eqCompactHeight
                live: root.playing && root.shown === "compact"
            }
            LyricsView {
                anchors.left: compactCover.right
                anchors.leftMargin: Appearance.spacing.small
                anchors.right: compactEq.left
                anchors.rightMargin: Appearance.spacing.small
                anchors.verticalCenter: parent.verticalCenter
                compact: true
                active: root.visible && root.shown === "compact"
                fallbackText: root.fallbackLine
            }
        }

        // Changement de piste : titre — artiste, élidé.
        Item {
            width: root.flashWidth
            height: root.slimHeight
            anchors.centerIn: parent
            opacity: root.shown === "flash" ? root.contentOpacity : 0
            visible: opacity > 0
            Cover {
                id: flashCover
                size: root.coverSmall
                anchors.left: parent.left
                anchors.leftMargin: (root.slimHeight - root.coverSmall) / 2
                anchors.verticalCenter: parent.verticalCenter
                circle: true
                art: root.player?.trackArtUrl ?? ""
            }
            StyledText {
                anchors.left: flashCover.right
                anchors.leftMargin: Appearance.spacing.small
                anchors.right: parent.right
                anchors.rightMargin: Appearance.padding.large
                anchors.verticalCenter: parent.verticalCenter
                elide: Text.ElideRight
                text: ((root.player?.trackTitle ?? "") + ((root.player?.trackArtist ?? "") !== "" ? " — " + root.player.trackArtist : ""))
                color: Colours.palette.m3onSurface
                font.pointSize: Appearance.font.size.normal
            }
        }

        // Volume, luminosité, micro : barre de niveau horizontale.
        Item {
            width: root.levelWidth
            height: root.slimHeight
            anchors.centerIn: parent
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
            height: root.expandedHeight
            anchors.top: parent.top
            anchors.horizontalCenter: parent.horizontalCenter
            opacity: root.shown === "expanded" ? root.contentOpacity : 0
            visible: opacity > 0
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
            }
            Column {
                anchors.left: bigCover.right
                anchors.leftMargin: Appearance.spacing.normal
                anchors.right: parent.right
                anchors.rightMargin: Appearance.padding.large
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
                active: root.visible && root.shown === "expanded"
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

    // Pochette : cercle (compact) ou carré arrondi (déployé), icône de repli tant qu'aucune image n'est disponible.
    // Au changement de piste, la nouvelle image apparaît en fondu par-dessus l'ancienne.
    component Cover: StyledClippingRect {
        id: cover
        required property int size
        property bool circle: true
        property url art
        // Halo doux de la couleur d'accent derrière la pochette.
        property bool glow: false
        implicitWidth: size
        implicitHeight: size
        radius: circle ? Appearance.rounding.full : Appearance.rounding.normal
        color: Colours.tPalette.m3surfaceContainerHigh
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
        readonly property var values: live ? Bridge.spectrum : []
        readonly property real cell: fill ? Math.max(1, (width - (count - 1) * barGap) / count) : barWidth
        readonly property real step: fill ? cell + barGap : barWidth * 2
        implicitWidth: count * barWidth + (count - 1) * barWidth
        Repeater {
            model: eq.count
            delegate: StyledRect {
                required property int index
                x: index * eq.step
                width: eq.cell
                height: Math.max(eq.barMin, PillMath.level(eq.values, index, eq.count) * eq.height)
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
