import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.config
import qs.services
import "settings"

// "Personnaliser": left column (search + pages), right side (the selected page). Modules live in settings/.
// Entry point loaded by Main.qml; go(i) / openSection(name) are the jump API.
Item {
    id: page

    objectName: "shellPreferences"

    readonly property bool compact: width < 720
    readonly property real navWidth: compact ? 76 : Math.min(320, Math.max(260, width * 0.3))
    property int current: 0

    implicitHeight: Math.min(680, Bridge.screenHeight - 100)

    function go(index: int): void {
        current = Math.max(0, Math.min(SettingsIndex.pages.length - 1, index));
        slot.index = current;
    }

    // "social" and "bookmarks" jump to those sections of the Applications page.
    function openSection(name: string): void {
        go(SettingsIndex.pageIndex("apps"));
        slot.jumpTo(name);
    }

    function openCustomEditor(): void {
        customEditor.openEditor();
    }

    // Created on first use (rare, owns two native windows).
    function openDetectionGuide(): void {
        if (!detectionGuide)
            detectionGuide = detectionComp.createObject(page);
        detectionGuide.visible = true;
    }

    function openShortcutDialog(id: string, label: string): void {
        shortcutDialog.openFor(id, label);
    }

    property var detectionGuide: null

    CustomThemeEditor {
        id: customEditor
    }

    Component {
        id: detectionComp

        DetectionGuide {}
    }

    SettingsShortcutDialog {
        id: shortcutDialog

        onGotoShortcut: id => {
            page.go(SettingsIndex.pageIndex("shortcuts"));
            slot.jumpTo("sc:" + id);
        }
    }

    Connections {
        target: Bridge

        function onShellEvent(event: string): void {
            if (!Bridge.preview)
                return;
            if (event === "verifyRecorder")
                shortcutDialog.openFor("panel", qsTr("Ouvrir le panneau"));
            else if (event === "verifyRecorderClose")
                shortcutDialog.finish();
        }
    }

    Shortcut {
        sequence: "Ctrl+F"
        enabled: page.visible
        onActivated: nav.searchField.forceActiveFocus()
    }

    SettingsNav {
        id: nav

        z: 2
        anchors.left: parent.left
        anchors.top: parent.top
        anchors.bottom: parent.bottom
        width: page.compact && wide ? Math.max(page.navWidth, 260) : page.navWidth
        current: page.current
        compact: page.compact
        onPageChosen: index => page.go(index)
        onDashboardRequested: Bridge.navigate("dashboard")
        onResultChosen: entry => {
            page.go(entry.page);
            slot.jumpTo(entry.anchor ?? entry.id);
        }

        Behavior on width {
            enabled: !Appearance.reducedEffects

            Anim {
                duration: Appearance.anim.durations.small
            }
        }
    }

    SettingsPageSlot {
        id: slot

        anchors.left: parent.left
        anchors.leftMargin: page.navWidth + Appearance.spacing.normal
        anchors.right: parent.right
        anchors.top: parent.top
        anchors.bottom: parent.bottom
        host: page
    }

    IconButton {
        objectName: "prefsClose"
        anchors.top: parent.top
        anchors.right: parent.right
        anchors.margins: Appearance.padding.small
        z: 3
        icon: "close"
        type: IconButton.Text
        onClicked: Bridge.closePanel()
    }
}
