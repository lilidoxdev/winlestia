pragma ComponentBehavior: Bound

import "items"
import qs.components.controls
import qs.config
import QtQuick

PathView {
    id: root

    required property StyledTextField search
    required property var visibilities
    required property real availableWidth

    readonly property int itemWidth: Config.launcher.sizes.wallpaperWidth * 0.8 + Appearance.padding.normal * 2
    readonly property int numItems: {
        const fit = Math.floor(availableWidth / itemWidth);
        const visible = Math.min(fit, Config.launcher.maxWallpapers, scriptModel.length);
        if (visible === 2)
            return 1;
        if (visible > 1 && visible % 2 === 0)
            return visible - 1;
        return Math.max(0, visible);
    }

    model: scriptModel.values
    onModelChanged: currentIndex = 0

    QtObject {
        id: scriptModel

        readonly property string term: root.search.text.split(" ").slice(1).join(" ")
        // Scanned only now: this item exists only while the wallpaper mode is open.
        property int revision: 0
        readonly property var values: { revision; return Bridge.wallpaperChoices(term) }
        readonly property int length: values.length
    }

    Component.onCompleted: Bridge.refreshWallpapers()
    Connections {
        target: Bridge
        function onWallpapersReady() { scriptModel.revision++ }
    }

    implicitWidth: Math.min(numItems, count) * itemWidth
    pathItemCount: numItems
    cacheItemCount: 4

    snapMode: PathView.SnapToItem
    preferredHighlightBegin: 0.5
    preferredHighlightEnd: 0.5
    highlightRangeMode: PathView.StrictlyEnforceRange

    delegate: WallpaperItem {
        visibilities: root.visibilities
    }

    path: Path {
        startY: root.height / 2

        PathAttribute {
            name: "z"
            value: 0
        }
        PathLine {
            x: root.width / 2
            relativeY: 0
        }
        PathAttribute {
            name: "z"
            value: 1
        }
        PathLine {
            x: root.width
            relativeY: 0
        }
    }

    CustomMouseArea {
        function onWheel(event: WheelEvent): void {
            if (event.angleDelta.y > 0)
                root.decrementCurrentIndex();
            else if (event.angleDelta.y < 0)
                root.incrementCurrentIndex();
        }

        anchors.fill: parent
    }
}
