import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

// Right side: big page title, then the page itself. Changing page is interruptible (fade out, swap, fade in);
// a search jump scrolls to the row and pulses it.
Item {
    id: slot

    property int index: 0
    // Object given to pages (openCustomEditor, openDetectionGuide, openShortcutDialog).
    property var host
    property string pendingAnchor
    readonly property var meta: SettingsIndex.pages[index]
    readonly property alias flick: scroll
    readonly property Item page: loader.item

    function component(i: int): Component {
        return [appearance, iface, docks, shortcuts, media, apps, files, general][i];
    }

    function find(root: Item, id: string): Item {
        const kids = root ? root.children : [];
        for (let i = 0; i < kids.length; i++) {
            const c = kids[i];
            if (c.rowId === id || c.sectionId === id)
                return c;
            const r = find(c, id);
            if (r)
                return r;
        }
        return null;
    }

    // Row or section with this id on the current page, or null.
    function findRow(id: string): Item {
        return find(loader.item, id);
    }

    function jumpTo(id: string): void {
        pendingAnchor = id;
        if (!swap.running)
            Qt.callLater(slot.applyAnchor);
    }

    function applyAnchor(): void {
        if (pendingAnchor === "" || !loader.item)
            return;
        const id = pendingAnchor;
        const it = findRow(id);
        if (!it)
            return;
        // A row hidden by its condition is not reachable: fall back to nothing rather than scroll to a ghost.
        if (it.height <= 0) {
            Qt.callLater(slot.applyAnchor);
            return;
        }
        pendingAnchor = "";
        const y = it.mapToItem(content, 0, 0).y;
        scroll.contentY = Math.max(0, Math.min(y - Appearance.padding.large * 3, scroll.contentHeight - scroll.height));
        if (it.pulse)
            it.pulse();
    }

    function load(): void {
        loader.sourceComponent = component(index);
        scroll.contentY = 0;
    }

    onIndexChanged: {
        if (Appearance.reducedEffects || !loader.item) {
            swap.stop();
            load();
            content.opacity = 1;
            content.shift = 0;
            Qt.callLater(slot.applyAnchor);
        } else {
            swap.restart();
        }
    }
    Component.onCompleted: load()

    Connections {
        target: loader

        function onLoaded(): void {
            loader.item.host = slot.host;
        }
    }

    Component {
        id: appearance
        PageAppearance {}
    }
    Component {
        id: iface
        PageInterface {}
    }
    Component {
        id: docks
        PageDocks {}
    }
    Component {
        id: shortcuts
        PageShortcuts {}
    }
    Component {
        id: media
        PageMedia {}
    }
    Component {
        id: apps
        PageApps {}
    }
    Component {
        id: files
        PageFiles {}
    }
    Component {
        id: general
        PageGeneral {}
    }

    SequentialAnimation {
        id: swap

        NumberAnimation {
            target: content
            property: "opacity"
            to: 0
            duration: Appearance.anim.durations.small / 2
            easing.type: Easing.OutCubic
        }
        ScriptAction {
            script: {
                slot.load();
                content.shift = 14;
            }
        }
        ParallelAnimation {
            NumberAnimation {
                target: content
                property: "opacity"
                to: 1
                duration: Appearance.anim.durations.small
                easing.type: Easing.OutCubic
            }
            NumberAnimation {
                target: content
                property: "shift"
                to: 0
                duration: Appearance.anim.durations.small
                easing.type: Easing.OutCubic
            }
        }
        ScriptAction {
            script: slot.applyAnchor()
        }
    }

    Flickable {
        id: scroll

        anchors.fill: parent
        contentWidth: width
        contentHeight: content.implicitHeight + Appearance.padding.large * 2
        clip: true
        boundsBehavior: Flickable.StopAtBounds
        interactive: contentHeight > height

        C.ScrollBar.vertical: StyledScrollBar {
            flickable: scroll
        }

        ColumnLayout {
            id: content

            property real shift: 0

            x: Appearance.padding.large
            y: Appearance.padding.large + shift
            width: Math.min(scroll.width - Appearance.padding.large * 2 - 8, 820)
            spacing: Appearance.spacing.larger

            ColumnLayout {
                Layout.fillWidth: true
                Layout.leftMargin: Appearance.padding.larger
                spacing: 2

                StyledText {
                    objectName: "settingsPageTitle"
                    Layout.fillWidth: true
                    elide: Text.ElideRight
                    text: slot.meta.title
                    font.pointSize: Appearance.font.size.title
                }

                StyledText {
                    Layout.fillWidth: true
                    wrapMode: Text.WordWrap
                    text: slot.meta.subtitle
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3outline
                }
            }

            Loader {
                id: loader

                Layout.fillWidth: true
                Layout.preferredHeight: item ? item.implicitHeight : 0
            }
        }
    }
}
