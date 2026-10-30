pragma ComponentBehavior: Bound

import "items"
import "services"
import qs.components
import qs.components.controls
import qs.components.containers
import qs.services
import qs.config
import Quickshell
import QtQuick

StyledListView {
    id: root

    required property StyledTextField search
    required property PersistentProperties visibilities

    // The first word is the "file" command itself; the rest is the name searched in the Windows index.
    readonly property string term: search.text.split(" ").slice(1).join(" ")

    model: Bridge.fileResults
    onModelChanged: currentIndex = 0
    onTermChanged: Bridge.fileQuery(term)
    Component.onCompleted: Bridge.fileQuery(term)
    Component.onDestruction: Bridge.fileQuery("")

    spacing: Appearance.spacing.small
    orientation: Qt.Vertical
    implicitHeight: (Config.launcher.sizes.itemHeight + spacing) * Math.min(Config.launcher.maxShown, count) - spacing

    preferredHighlightBegin: 0
    preferredHighlightEnd: height
    highlightRangeMode: ListView.ApplyRange

    highlightFollowsCurrentItem: false
    highlight: StyledRect {
        radius: Appearance.rounding.normal
        color: Colours.palette.m3onSurface
        opacity: 0.08

        y: root.currentItem?.y ?? 0
        implicitWidth: root.width
        implicitHeight: root.currentItem?.implicitHeight ?? 0
    }

    StyledScrollBar.vertical: StyledScrollBar {
        flickable: root
    }

    delegate: appItem

    Component {
        id: appItem

        AppItem {
            visibilities: root.visibilities
        }
    }
}
