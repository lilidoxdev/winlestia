import QtQuick
import qs.components
import qs.components.controls
import qs.services
import qs.config

// Média : titre / artiste (ou invite), précédent / lecture / suivant.
LockCard {
    id: root

    readonly property var player: Players.active

    StyledText {
        id: title

        width: parent.width
        horizontalAlignment: Text.AlignHCenter
        color: Colours.palette.m3primary
        elide: Text.ElideRight
        text: root.player ? (root.player.trackTitle || qsTr("Titre inconnu")) : qsTr("Rien en lecture")
        font.pointSize: Appearance.font.size.large
        font.weight: Font.DemiBold
    }

    StyledText {
        id: artist

        anchors.top: title.bottom
        width: parent.width
        horizontalAlignment: Text.AlignHCenter
        elide: Text.ElideRight
        text: root.player ? (root.player.trackArtist || qsTr("Artiste inconnu")) : qsTr("Lancez un peu de musique !")
        color: Colours.palette.m3onSurfaceVariant
    }

    Row {
        anchors.top: artist.bottom
        anchors.topMargin: Appearance.spacing.normal
        anchors.horizontalCenter: parent.horizontalCenter
        spacing: Appearance.spacing.large

        IconButton {
            icon: "skip_previous"
            type: IconButton.Text
            disabled: !(root.player?.canGoPrevious ?? false)
            onClicked: root.player?.previous()
        }
        IconButton {
            icon: root.player?.isPlaying ? "pause" : "play_arrow"
            type: IconButton.Filled
            disabled: !(root.player?.canTogglePlaying ?? false)
            onClicked: root.player?.togglePlaying()
        }
        IconButton {
            icon: "skip_next"
            type: IconButton.Text
            disabled: !(root.player?.canGoNext ?? false)
            onClicked: root.player?.next()
        }
    }
}
