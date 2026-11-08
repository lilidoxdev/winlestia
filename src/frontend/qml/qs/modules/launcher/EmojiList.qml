pragma ComponentBehavior: Bound

import qs.components
import qs.components.controls
import qs.services
import qs.config
import QtQuick

GridView {
    id: root

    required property StyledTextField search
    required property var visibilities

    readonly property int columns: Math.max(1, Math.floor(width / 60))
    // Le premier mot est la commande « emoji » elle-même ; le reste filtre les mots-clés français/anglais embarqués.
    readonly property string term: search.text.split(" ").slice(1).join(" ")

    function incrementCurrentIndex(): void {
        moveCurrentIndexDown();
    }
    function decrementCurrentIndex(): void {
        moveCurrentIndexUp();
    }

    model: Bridge.emojiChoices(term)
    onModelChanged: currentIndex = 0
    cellWidth: width / columns
    cellHeight: 60
    clip: true
    currentIndex: 0
    highlightFollowsCurrentItem: true
    highlightMoveDuration: 0
    keyNavigationEnabled: false

    highlight: StyledRect {
        radius: Appearance.rounding.normal
        color: Colours.palette.m3onSurface
        opacity: 0.08
    }

    StyledScrollBar.vertical: StyledScrollBar {
        flickable: root
    }

    delegate: Item {
        id: cell

        required property var modelData
        required property int index

        width: root.cellWidth
        height: root.cellHeight

        StateLayer {
            radius: Appearance.rounding.normal

            function onClicked(): void {
                Apps.launch(cell.modelData);
                root.visibilities.launcher = false;
            }
        }

        StyledText {
            anchors.centerIn: parent
            text: cell.modelData.name
            font.family: "Segoe UI Emoji"
            font.pointSize: Appearance.font.size.extraLarge
            renderType: Text.NativeRendering
        }
    }
}
