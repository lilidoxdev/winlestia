// Pilule : capsule en haut de l'écran, présente seulement quand quelque chose se passe (musique, volume, luminosité, micro).
// Elle passe d'un état à l'autre en animant taille, rayon et contenu : repos, média compact, média déployé, changement de piste,
// niveau. Fenêtre sans focus, masquée au repos (aucun clic bloqué), ni minuteur ni animation tant qu'il ne se passe rien.
pragma ComponentBehavior: Bound

import QtQuick
import QtQuick.Window
import qs.components
import qs.components.controls
import qs.services
import qs.config

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
    readonly property int flashWidth: 360
    readonly property int levelWidth: 250
    readonly property int expandedWidth: 360
    readonly property int expandedHeight: 156
    readonly property int coverLarge: 72
    readonly property int eqCompactBars: 6
    readonly property int eqExpandedBars: 20
    readonly property int eqBarWidth: 3
    readonly property int eqBarMin: 2
    readonly property int eqCompactHeight: 18
    readonly property int eqExpandedHeight: 24
    readonly property int progressHeight: 4
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

    readonly property real targetWidth: stateName === "expanded" ? expandedWidth : stateName === "flash" ? flashWidth : stateName
        === "level" ? levelWidth : stateName === "compact" ? compactWidth : slimHeight
    readonly property real targetHeight: stateName === "expanded" ? expandedHeight : slimHeight
    readonly property real targetRadius: stateName === "expanded" ? Appearance.rounding.large : targetHeight / 2
    readonly property bool wantsSpectrum: !suppressed && playing && (stateName === "compact" || stateName === "flash" || stateName
        === "expanded")

    // La fenêtre épouse la taille animée de la capsule : aucune zone morte autour, aucun clic avalé.
    width: Math.max(1, pill.width)
    height: Math.max(1, pill.height)
    x: Bridge.screenX + (Bridge.screenWidth - width) / 2
    // Avec le cadre d'écran, la pilule est suspendue à son bord haut ; sans cadre, petite marge depuis le haut de l'écran.
    y: Bridge.screenY + ((Bridge.settings.screenFrame ?? true) ? Math.max(0, Bridge.frameThickness) : Appearance.spacing.normal)
    visible: !suppressed && (stateName !== "idle" || pill.opacity > 0.01)

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
        previousKey = trackKey;
        lingering = playing;
        Bridge.mediaRef(true);
    }
    Component.onDestruction: {
        Bridge.setPillSpectrum(false);
        Bridge.mediaRef(false);
    }

    // Le contenu sortant s'efface avant que le suivant n'apparaisse ; la taille, elle, anime tout de suite vers la cible.
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
        opacity: root.stateName === "idle" ? 0 : 1

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
        MouseArea {
            anchors.fill: parent
            enabled: root.stateName === "compact" || root.stateName === "flash"
            cursorShape: Qt.PointingHandCursor
            onClicked: {
                leaveTimer.stop();
                root.expandedWanted = true;
            }
        }

        // Média compact : pochette ronde à gauche, mini égaliseur à droite.
        Item {
            width: root.compactWidth
            height: root.slimHeight
            anchors.centerIn: parent
            opacity: root.shown === "compact" ? root.contentOpacity : 0
            visible: opacity > 0
            Cover {
                size: root.coverSmall
                anchors.left: parent.left
                anchors.leftMargin: (root.slimHeight - root.coverSmall) / 2
                anchors.verticalCenter: parent.verticalCenter
                circle: true
                art: root.player?.trackArtUrl ?? ""
            }
            Equaliser {
                anchors.right: parent.right
                anchors.rightMargin: Appearance.padding.larger + Appearance.padding.small
                anchors.verticalCenter: parent.verticalCenter
                count: root.eqCompactBars
                barWidth: root.eqBarWidth
                barMin: root.eqBarMin
                animated: !root.reduced
                height: root.eqCompactHeight
                live: root.playing && root.shown === "compact"
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

        // Média déployé : carte avec pochette, titre, artiste, commandes, progression et spectre.
        Item {
            width: root.expandedWidth
            height: root.expandedHeight
            anchors.centerIn: parent
            opacity: root.shown === "expanded" ? root.contentOpacity : 0
            visible: opacity > 0
            Cover {
                id: bigCover
                size: root.coverLarge
                x: Appearance.padding.large
                y: Appearance.padding.large
                circle: false
                art: root.player?.trackArtUrl ?? ""
            }
            Column {
                anchors.left: bigCover.right
                anchors.leftMargin: Appearance.spacing.normal
                anchors.right: parent.right
                anchors.rightMargin: Appearance.padding.large
                anchors.top: bigCover.top
                spacing: 0
                StyledText {
                    width: parent.width
                    elide: Text.ElideRight
                    text: root.player ? (root.player.trackTitle || qsTr("Unknown title")) : ""
                    color: Colours.palette.m3primary
                    font.pointSize: Appearance.font.size.normal
                }
                StyledText {
                    width: parent.width
                    elide: Text.ElideRight
                    text: (root.player?.trackArtist ?? "") || qsTr("Unknown artist")
                    color: Colours.palette.m3secondary
                }
            }
            Row {
                anchors.left: bigCover.right
                anchors.leftMargin: Appearance.spacing.small
                anchors.bottom: bigCover.bottom
                spacing: Appearance.spacing.small
                Control {
                    icon: "skip_previous"
                    canUse: root.player?.canGoPrevious ?? false
                    function onClicked(): void {
                        root.player?.previous();
                    }
                }
                Control {
                    icon: root.playing ? "pause" : "play_arrow"
                    canUse: root.player?.canTogglePlaying ?? false
                    function onClicked(): void {
                        root.player?.togglePlaying();
                    }
                }
                Control {
                    icon: "skip_next"
                    canUse: root.player?.canGoNext ?? false
                    function onClicked(): void {
                        root.player?.next();
                    }
                }
            }
            StyledRect {
                id: progress
                x: Appearance.padding.large
                width: parent.width - Appearance.padding.large * 2
                anchors.top: bigCover.bottom
                anchors.topMargin: Appearance.spacing.smaller
                height: root.progressHeight
                radius: Appearance.rounding.full
                color: Colours.layer(Colours.palette.m3surfaceContainerHigh, 2)
                StyledRect {
                    anchors.left: parent.left
                    anchors.top: parent.top
                    anchors.bottom: parent.bottom
                    radius: Appearance.rounding.full
                    color: Colours.palette.m3primary
                    width: parent.width * Math.max(0, Math.min(1, root.player?.lengthKnown && root.player?.length ? root.player.position
                        / root.player.length : 0))
                }
            }
            Equaliser {
                x: Appearance.padding.large
                width: parent.width - Appearance.padding.large * 2
                anchors.bottom: parent.bottom
                anchors.bottomMargin: Appearance.padding.large
                count: root.eqExpandedBars
                barWidth: root.eqBarWidth
                barMin: root.eqBarMin
                animated: !root.reduced
                height: root.eqExpandedHeight
                live: root.playing && root.shown === "expanded"
                stretch: true
            }
        }
    }

    // Pochette : cercle (compact) ou carré arrondi (déployé), icône de repli tant qu'aucune image n'est disponible.
    component Cover: StyledClippingRect {
        required property int size
        property bool circle: true
        property url art
        implicitWidth: size
        implicitHeight: size
        radius: circle ? Appearance.rounding.full : Appearance.rounding.normal
        color: Colours.tPalette.m3surfaceContainerHigh
        MaterialIcon {
            anchors.centerIn: parent
            grade: root.coverIconGrade
            text: "art_track"
            color: Colours.palette.m3onSurfaceVariant
            font.pointSize: (parent.width * root.coverIconScale) || 1
        }
        Image {
            anchors.fill: parent
            source: parent.art
            asynchronous: true
            fillMode: Image.PreserveAspectCrop
            sourceSize.width: width
            sourceSize.height: height
        }
    }

    // Égaliseur : `count` barres m3primary regroupées depuis les 48 bandes ; barres plates quand rien ne joue.
    component Equaliser: Item {
        id: eq
        property int count: 6
        property bool live: false
        property bool stretch: false
        property int barWidth: 3
        property int barMin: 2
        property bool animated: true
        readonly property var values: live ? Bridge.spectrum : []
        readonly property real gap: stretch ? (width - count * eq.barWidth) / Math.max(1, count - 1) : eq.barWidth
        implicitWidth: count * eq.barWidth + (count - 1) * eq.barWidth
        function level(i) {
            const s = values;
            if (!s || s.length === 0)
                return 0;
            const a = Math.floor(i * s.length / count);
            const b = Math.max(a + 1, Math.floor((i + 1) * s.length / count));
            let t = 0;
            for (let k = a; k < b; k++)
                t += s[k] || 0;
            return Math.min(1, Math.sqrt(t / (b - a)));
        }
        Repeater {
            model: eq.count
            delegate: StyledRect {
                required property int index
                x: index * (eq.barWidth + eq.gap)
                width: eq.barWidth
                height: Math.max(eq.barMin, eq.level(index) * eq.height)
                anchors.verticalCenter: eq.verticalCenter
                radius: Appearance.rounding.full
                color: Colours.palette.m3primary
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

    component Control: StyledRect {
        id: control
        required property string icon
        required property bool canUse
        function onClicked(): void {
        }
        implicitWidth: ctrlIcon.implicitHeight + Appearance.padding.small
        implicitHeight: implicitWidth
        radius: Appearance.rounding.full
        StateLayer {
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
            color: control.canUse ? Colours.palette.m3onSurface : Colours.palette.m3outline
            font.pointSize: Appearance.font.size.large
        }
    }
}
