import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config
import ".."

// Bloc `media` : carte « en cours de lecture » sur Players.active. Pochette en fond flouté et assombri (`backdrop`), titre en gras,
// artiste à 0,65, barre de progression cliquable (déplace la lecture si le lecteur le permet) avec temps écoulé / restant, et
// pastille flottante de commandes (`pill`) au bord bas. Props : showArt, showProgress, showControls, backdrop, pill (vrais par défaut).
// La position n'est notifiée que par événements : un minuteur d'une seconde l'interpole, uniquement pendant la lecture et tant
// que la carte est visible et non suspendue. Mouvement : fondu de pochette au changement de piste, glyphe lecture/pause en 140 ms.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property var player: Players.active
    readonly property bool hasPlayer: player !== null && player !== undefined
    readonly property bool playing: hasPlayer && player.isPlaying
    readonly property bool showArt: flag("showArt")
    readonly property bool showProgress: flag("showProgress")
    readonly property bool showControls: flag("showControls")
    readonly property bool wantBackdrop: flag("backdrop")
    readonly property bool wantPill: flag("pill")
    readonly property bool live: visible && (!host || host.live)
    readonly property bool editing: !!host && host.editing
    readonly property bool motionOk: live && !Bridge.reducedEffects

    // Incrémenté chaque seconde pendant la lecture : force la relecture de `position` (extrapolée côté lecteur).
    property int tick: 0
    readonly property real position: {
        void tick;
        return hasPlayer ? Math.max(0, player.position) : 0;
    }
    readonly property real length: hasPlayer ? Math.max(0, player.length) : 0
    readonly property bool lengthKnown: hasPlayer && length > 0 && player.lengthKnown !== false
    readonly property real progress: lengthKnown ? Math.min(1, position / length) : 0
    readonly property string artUrl: hasPlayer ? player.trackArtUrl : ""
    readonly property bool canSeek: hasPlayer && lengthKnown && player.canSeek === true && player.positionSupported === true
    // Fond d'artwork actif : voulu, pochette prête. Sans pochette, le verre reste uni et la tuile d'icône reprend sa place.
    readonly property bool backdropOn: showArt && wantBackdrop && hasPlayer && bgArt.status === Image.Ready

    function flag(name: string): bool {
        const v = spec ? Bridge.winjets.resolve(spec[name], ctx) : undefined;
        return v === undefined || v === null ? true : v !== false;
    }

    function clock(seconds: real): string {
        if (!isFinite(seconds) || seconds < 0)
            return "--:--";
        const s = Math.floor(seconds);
        const h = Math.floor(s / 3600);
        const m = Math.floor((s % 3600) / 60);
        const r = (s % 60).toString().padStart(2, "0");
        return h > 0 ? `${h}:${m.toString().padStart(2, "0")}:${r}` : `${m}:${r}`;
    }

    function act(type: string): void {
        if (host && !editing)
            host.runAction({
                type: type
            });
    }

    function seekTo(ratio: real): void {
        if (!canSeek || editing)
            return;
        player.position = Math.max(0, Math.min(1, ratio)) * length;
        tick++;
    }

    // Échelle effective : plafonnée par la hauteur disponible (le contenu ne déborde jamais de la carte).
    readonly property real fs: Math.max(0.3, Math.min(sc, height / 160))

    implicitWidth: 260 * sc
    implicitHeight: 104 * sc

    // Changement de piste : la pochette précédente s'efface sous la nouvelle, le texte glisse de 4 px en fondu.
    property string lastUrl: ""
    property string prevUrl: ""
    property real artMix: 1
    readonly property string trackKey: hasPlayer ? player.trackTitle + "\u0001" + player.trackArtist : ""
    onArtUrlChanged: {
        prevUrl = lastUrl;
        lastUrl = artUrl;
        if (motionOk && prevUrl !== "")
            artFade.restart();
    }
    onTrackKeyChanged: if (motionOk && trackKey !== "")
        textSwap.restart()

    NumberAnimation {
        id: artFade

        target: block
        property: "artMix"
        from: 0
        to: 1
        duration: WinjetTheme.motionSlow || 450
        easing.type: Easing.OutCubic
    }
    ParallelAnimation {
        id: textSwap

        NumberAnimation {
            target: head
            property: "opacity"
            from: 0
            to: 1
            duration: WinjetTheme.motionMedium || 220
            easing.type: Easing.OutCubic
        }
        NumberAnimation {
            target: headShift
            property: "y"
            from: 4 * block.fs
            to: 0
            duration: WinjetTheme.motionMedium || 220
            easing.type: Easing.OutCubic
        }
    }

    Timer {
        running: block.live && block.playing && block.showProgress && block.lengthKnown
        interval: 1000
        repeat: true
        onTriggered: block.tick++
    }

    // Fond d'artwork : pochette très réduite puis agrandie (flou), assombrie par le voile de surface, pleine carte.
    Rectangle {
        id: backdrop

        readonly property point origin: {
            void block.width;
            void block.height;
            return block.host ? block.mapToItem(block.host, 0, 0) : Qt.point(0, 0);
        }

        visible: block.showArt && block.wantBackdrop && block.hasPlayer && block.artUrl !== ""
        z: -1
        x: -origin.x
        y: -origin.y
        width: block.host ? block.host.width : block.width
        height: block.host ? block.host.height : block.height
        radius: WinjetTheme.radiusFor(width, height)
        color: "transparent"
        opacity: block.backdropOn ? 1 : 0
        // Rectangle simple : un masque (ClippingRectangle, MultiEffect) fait disparaître l'image ; les coins du fond ne sont donc pas
        // arrondis.

        Behavior on opacity {
            enabled: block.motionOk
            NumberAnimation {
                duration: WinjetTheme.motionSlow || 450
                easing.type: Easing.OutCubic
            }
        }

        Image {
            anchors.fill: parent
            source: block.prevUrl
            opacity: 1 - block.artMix
            fillMode: Image.PreserveAspectCrop
            smooth: true
            asynchronous: true
            sourceSize.width: 40
            sourceSize.height: 40
        }
        Image {
            id: bgArt

            anchors.fill: parent
            source: block.artUrl
            opacity: block.artMix
            fillMode: Image.PreserveAspectCrop
            smooth: true
            asynchronous: true
            sourceSize.width: 40
            sourceSize.height: 40
        }
        Rectangle {
            anchors.fill: parent
            color: Qt.alpha(Colours.palette.m3surface, 0.55)
        }
        Rectangle {
            anchors.fill: parent
            gradient: Gradient {
                GradientStop {
                    position: 0.45
                    color: "transparent"
                }
                GradientStop {
                    position: 1
                    color: Qt.alpha(Colours.palette.m3surface, 0.6)
                }
            }
        }
    }

    ColumnLayout {
        visible: block.hasPlayer
        anchors.fill: parent
        spacing: 6 * block.fs

        // Titre : tuile de pochette quand le fond n'est pas actif, puis source, titre gras et artiste.
        RowLayout {
            id: head

            Layout.fillWidth: true
            spacing: 12 * block.fs
            transform: Translate {
                id: headShift
            }

            Rectangle {
                id: cover

                visible: block.showArt && !block.backdropOn
                Layout.preferredWidth: 56 * block.fs
                Layout.preferredHeight: 56 * block.fs
                Layout.alignment: Qt.AlignTop
                radius: 14 * block.fs
                color: Colours.palette.m3secondaryContainer

                MaterialIcon {
                    anchors.centerIn: parent
                    visible: art.status !== Image.Ready
                    text: "music_note"
                    color: Colours.palette.m3onSecondaryContainer
                    font.pointSize: Math.max(8, cover.width * 0.34)
                }
                StyledClippingRect {
                    anchors.fill: parent
                    radius: cover.radius
                    visible: art.status === Image.Ready

                    Image {
                        id: art

                        anchors.fill: parent
                        source: block.artUrl
                        fillMode: Image.PreserveAspectCrop
                        asynchronous: true
                        cache: false
                        sourceSize.width: Math.ceil(cover.width * 2)
                        sourceSize.height: Math.ceil(cover.height * 2)
                    }
                }
            }

            ColumnLayout {
                Layout.fillWidth: true
                spacing: 1 * block.fs

                StyledText {
                    Layout.fillWidth: true
                    visible: text !== "" && block.height > 150
                    text: block.hasPlayer ? block.player.identity : ""
                    color: WinjetTheme.accent
                    font.pointSize: (Math.max(9, Math.round(11 * block.fs))) * 0.75
                    font.weight: Font.DemiBold
                    font.capitalization: Font.AllUppercase
                    font.letterSpacing: 1.2
                    elide: Text.ElideRight
                }
                StyledText {
                    Layout.fillWidth: true
                    text: (block.hasPlayer ? block.player.trackTitle : "") || qsTr("Titre inconnu")
                    color: Colours.palette.m3onSurface
                    font.pointSize: (Math.max(12, Math.round(22 * block.fs))) * 0.75
                    font.weight: Font.Bold
                    elide: Text.ElideRight
                }
                StyledText {
                    Layout.fillWidth: true
                    text: (block.hasPlayer ? block.player.trackArtist : "") || qsTr("Artiste inconnu")
                    color: Qt.alpha(Colours.palette.m3onSurface, 0.65)
                    font.pointSize: (Math.max(10, Math.round(14 * block.fs))) * 0.75
                    elide: Text.ElideRight
                }
            }
        }

        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true
        }

        // Progression : temps écoulé, barre cliquable (déplace la lecture), temps restant.
        RowLayout {
            Layout.fillWidth: true
            visible: block.showProgress
            spacing: 8 * block.fs

            StyledText {
                text: block.clock(block.position)
                color: Qt.alpha(Colours.palette.m3onSurface, 0.6)
                font.pointSize: (Math.max(9, Math.round(12 * block.fs))) * 0.75
                font.features: {
                    "tnum": 1
                }
            }
            Item {
                Layout.fillWidth: true
                Layout.preferredHeight: 18 * block.fs

                Rectangle {
                    id: track

                    anchors.verticalCenter: parent.verticalCenter
                    width: parent.width
                    height: Math.max(3, 4 * block.fs * (seekArea.containsMouse && block.canSeek ? 1.5 : 1))
                    radius: height / 2
                    color: Qt.alpha(Colours.palette.m3onSurface, 0.2)

                    Behavior on height {
                        enabled: block.motionOk
                        NumberAnimation {
                            duration: WinjetTheme.motionFast || 160
                            easing.type: Easing.OutCubic
                        }
                    }

                    Rectangle {
                        width: parent.width * block.progress
                        height: parent.height
                        radius: parent.radius
                        color: WinjetTheme.accent

                        Behavior on width {
                            enabled: block.motionOk
                            NumberAnimation {
                                duration: WinjetTheme.motionSlow || 450
                                easing.type: Easing.OutCubic
                            }
                        }
                    }
                }
                MouseArea {
                    id: seekArea

                    anchors.fill: parent
                    enabled: block.canSeek && !block.editing
                    hoverEnabled: true
                    cursorShape: block.canSeek ? Qt.PointingHandCursor : Qt.ArrowCursor
                    onClicked: mouse => block.seekTo(mouse.x / width)
                }
            }
            StyledText {
                text: block.lengthKnown ? "-" + block.clock(Math.max(0, block.length - block.position)) : "--:--"
                color: Qt.alpha(Colours.palette.m3onSurface, 0.6)
                font.pointSize: (Math.max(9, Math.round(12 * block.fs))) * 0.75
                font.features: {
                    "tnum": 1
                }
            }
        }

        // Pastille flottante de commandes : verre propre avec liseré externe et filet interne.
        Rectangle {
            id: pill

            visible: block.showControls
            Layout.alignment: Qt.AlignHCenter
            Layout.preferredWidth: controls.implicitWidth + 20 * block.fs
            Layout.preferredHeight: 44 * block.fs
            radius: height / 2
            color: block.wantPill ? Qt.alpha(Colours.palette.m3surface, 0.5) : "transparent"
            border.width: block.wantPill ? 1 : 0
            border.color: Qt.alpha(Colours.palette.m3onSurface, 0.3)

            Rectangle {
                anchors.fill: parent
                anchors.margins: 1
                radius: height / 2
                color: "transparent"
                visible: block.wantPill
                border.width: 1
                border.color: Qt.alpha(Colours.palette.m3onSurface, 0.06)
            }

            RowLayout {
                id: controls

                anchors.centerIn: parent
                spacing: 6 * block.fs

                Ctl {
                    motion: block.motionOk
                    locked: block.editing
                    glyph: "skip_previous"
                    usable: block.hasPlayer && block.player.canGoPrevious
                    side: 34 * block.fs
                    onActivated: block.act("media.prev")
                }
                Ctl {
                    motion: block.motionOk
                    locked: block.editing
                    id: toggle

                    primary: true
                    usable: block.hasPlayer && block.player.canTogglePlaying
                    side: 38 * block.fs
                    onActivated: block.act("media.playPause")

                    // Les deux glyphes se croisent : échelle et opacité, 140 ms.
                    MaterialIcon {
                        anchors.centerIn: parent
                        text: "play_arrow"
                        color: Colours.palette.m3onPrimary
                        font.pointSize: Math.max(8, toggle.side * 0.5)
                        opacity: block.playing ? 0 : 1
                        scale: block.playing ? 0.6 : 1

                        Behavior on opacity {
                            enabled: block.motionOk
                            NumberAnimation {
                                duration: 140
                            }
                        }
                        Behavior on scale {
                            enabled: block.motionOk
                            NumberAnimation {
                                duration: 140
                                easing.type: Easing.OutCubic
                            }
                        }
                    }
                    MaterialIcon {
                        anchors.centerIn: parent
                        text: "pause"
                        color: Colours.palette.m3onPrimary
                        font.pointSize: Math.max(8, toggle.side * 0.5)
                        opacity: block.playing ? 1 : 0
                        scale: block.playing ? 1 : 0.6

                        Behavior on opacity {
                            enabled: block.motionOk
                            NumberAnimation {
                                duration: 140
                            }
                        }
                        Behavior on scale {
                            enabled: block.motionOk
                            NumberAnimation {
                                duration: 140
                                easing.type: Easing.OutCubic
                            }
                        }
                    }
                }
                Ctl {
                    motion: block.motionOk
                    locked: block.editing
                    glyph: "skip_next"
                    usable: block.hasPlayer && block.player.canGoNext
                    side: 34 * block.fs
                    onActivated: block.act("media.next")
                }
            }
        }
    }

    // Bouton rond de la pastille : survol et appui (échelle 0,92, 90 ms) ; `primary` = disque d'accent.
    component Ctl: Item {
        id: ctl

        property string glyph: ""
        property bool primary: false
        property bool usable: true
        property real side: 34
        property bool motion: false
        property bool locked: false

        signal activated

        Layout.preferredWidth: side
        Layout.preferredHeight: side
        scale: ctlArea.pressed ? 0.92 : 1
        opacity: usable ? 1 : 0.35

        Behavior on scale {
            enabled: ctl.motion
            NumberAnimation {
                duration: 90
                easing.type: Easing.OutCubic
            }
        }

        Rectangle {
            anchors.fill: parent
            radius: width / 2
            color: ctl.primary ? WinjetTheme.accent : Qt.alpha(Colours.palette.m3onSurface, ctlArea.containsMouse && ctl.usable ? 0.14 : 0)

            Behavior on color {
                enabled: ctl.motion
                ColorAnimation {
                    duration: WinjetTheme.motionFast || 160
                }
            }
        }
        MaterialIcon {
            anchors.centerIn: parent
            visible: ctl.glyph !== ""
            text: ctl.glyph
            color: Colours.palette.m3onSurface
            font.pointSize: Math.max(8, ctl.side * 0.5)
        }
        MouseArea {
            id: ctlArea

            anchors.fill: parent
            enabled: ctl.usable && !ctl.locked
            hoverEnabled: true
            cursorShape: Qt.PointingHandCursor
            onClicked: ctl.activated()
        }
    }

    // État vide : même hauteur que la carte, calme.
    ColumnLayout {
        visible: !block.hasPlayer
        anchors.centerIn: parent
        spacing: 4 * block.fs

        MaterialIcon {
            Layout.alignment: Qt.AlignHCenter
            text: "music_off"
            color: Qt.alpha(Colours.palette.m3onSurface, 0.6)
            font.pointSize: Appearance.font.size.extraLarge * block.fs
        }
        StyledText {
            Layout.alignment: Qt.AlignHCenter
            objectName: "mediaEmpty"
            text: qsTr("Aucune lecture")
            color: Qt.alpha(Colours.palette.m3onSurface, 0.6)
            font.pointSize: (Math.max(10, Math.round(14 * block.fs))) * 0.75
            font.weight: Font.DemiBold
        }
    }
}
