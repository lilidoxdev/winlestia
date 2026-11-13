import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import ".."

// Bloc `media` : carte « en cours de lecture » (pochette, titre, artiste, source, progression, commandes) sur Players.active.
// Props : showArt, showProgress, showControls (vrais par défaut). La position n'est notifiée que par événements : un minuteur
// d'une seconde l'interpole, uniquement pendant la lecture et tant que la carte est visible et non suspendue.
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
    readonly property bool live: visible && (!host || host.live)
    readonly property bool editing: !!host && host.editing

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

    // Échelle effective : plafonnée par la hauteur disponible (le contenu ne déborde jamais de la carte).
    readonly property real fs: Math.max(0.3, Math.min(sc, height / 160))

    implicitWidth: 260 * sc
    implicitHeight: 104 * sc

    Timer {
        running: block.live && block.playing && block.showProgress && block.lengthKnown
        interval: 1000
        repeat: true
        onTriggered: block.tick++
    }

    // Tuile secondaryContainer toujours tracée (rectangle simple, sans masque) : note de musique m3primary tant que la pochette
    // manque ; la pochette arrondie (rayon de l'apparence) se superpose dès qu'elle est prête.
    Rectangle {
        id: cover

        visible: block.showArt && block.hasPlayer
        anchors.left: parent.left
        anchors.verticalCenter: parent.verticalCenter
        width: Math.min(block.height, block.width * 0.4)
        height: width
        radius: Appearance.rounding.normal * block.fs
        color: Colours.palette.m3secondaryContainer

        MaterialIcon {
            anchors.centerIn: parent
            visible: art.status !== Image.Ready
            text: "music_note"
            color: Colours.palette.m3onSecondaryContainer
            font.pointSize: Math.max(8, cover.width * 0.28)
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
        visible: block.hasPlayer
        anchors.left: cover.visible ? cover.right : parent.left
        anchors.leftMargin: cover.visible ? 12 * block.fs : 0
        anchors.right: parent.right
        anchors.verticalCenter: parent.verticalCenter
        spacing: 2 * block.fs

        StyledText {
            Layout.fillWidth: true
            visible: text !== ""
            text: block.hasPlayer ? block.player.identity : ""
            color: Colours.palette.m3outline
            font.pointSize: Appearance.font.size.small * block.fs
            elide: Text.ElideRight
        }
        StyledText {
            Layout.fillWidth: true
            text: (block.hasPlayer ? block.player.trackTitle : "") || qsTr("Titre inconnu")
            color: Colours.palette.m3onSurface
            font.pointSize: Appearance.font.size.larger * block.fs
            font.weight: Font.DemiBold
            elide: Text.ElideRight
        }
        StyledText {
            Layout.fillWidth: true
            text: (block.hasPlayer ? block.player.trackArtist : "") || qsTr("Artiste inconnu")
            color: Colours.palette.m3onSurfaceVariant
            font.pointSize: Appearance.font.size.small * block.fs
            elide: Text.ElideRight
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.topMargin: 4 * block.fs
            visible: block.showProgress
            spacing: 6 * block.fs

            StyledText {
                text: block.clock(block.position)
                color: Colours.palette.m3onSurfaceVariant
                font.pointSize: Appearance.font.size.small * block.fs
            }
            Rectangle {
                Layout.fillWidth: true
                Layout.preferredHeight: Math.max(2, 4 * block.fs)
                radius: height / 2
                color: Colours.palette.m3outlineVariant

                Rectangle {
                    width: parent.width * block.progress
                    height: parent.height
                    radius: parent.radius
                    color: Colours.palette.m3primary
                }
            }
            StyledText {
                text: block.lengthKnown ? block.clock(block.length) : "--:--"
                color: Colours.palette.m3onSurfaceVariant
                font.pointSize: Appearance.font.size.small * block.fs
            }
        }

        RowLayout {
            Layout.alignment: Qt.AlignHCenter
            Layout.topMargin: 2 * block.fs
            visible: block.showControls
            spacing: 8 * block.fs

            IconButton {
                readonly property real side: 30 * block.fs

                type: IconButton.Text
                icon: "skip_previous"
                font.pointSize: Appearance.font.size.large * block.fs
                disabled: !(block.hasPlayer && block.player.canGoPrevious)
                implicitWidth: side
                implicitHeight: side
                onClicked: block.act("media.prev")
            }
            IconButton {
                readonly property real side: 36 * block.fs

                type: IconButton.Filled
                icon: block.playing ? "pause" : "play_arrow"
                font.pointSize: Appearance.font.size.large * block.fs
                disabled: !(block.hasPlayer && block.player.canTogglePlaying)
                implicitWidth: side
                implicitHeight: side
                onClicked: block.act("media.playPause")
            }
            IconButton {
                readonly property real side: 30 * block.fs

                type: IconButton.Text
                icon: "skip_next"
                font.pointSize: Appearance.font.size.large * block.fs
                disabled: !(block.hasPlayer && block.player.canGoNext)
                implicitWidth: side
                implicitHeight: side
                onClicked: block.act("media.next")
            }
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
            color: Colours.palette.m3onSurfaceVariant
            font.pointSize: Appearance.font.size.extraLarge * block.fs
        }
        StyledText {
            Layout.alignment: Qt.AlignHCenter
            objectName: "mediaEmpty"
            text: qsTr("Aucune lecture")
            color: Colours.palette.m3onSurfaceVariant
            font.pointSize: Appearance.font.size.normal * block.fs
        }
    }
}
