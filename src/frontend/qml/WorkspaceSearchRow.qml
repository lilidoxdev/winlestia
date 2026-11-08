// Ligne de recherche de l'espace (champ + bascules).
// Extrait de WorkspaceBrowser.qml (pur découpage) : les ids du parent sont reçus en propriétés requises.
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.services
import qs.config

RowLayout {
    required property var browser
    required property var grid
    readonly property alias queryField: query
    Layout.fillWidth: true
    spacing: Appearance.spacing.small
    opacity: browser.pHead
    transform: Translate {
        y: browser.fx ? (1 - browser.pHead) * -10 : 0
    }
    StyledRect {
        id: searchBox
        Layout.fillWidth: true
        implicitHeight: 50
        radius: Appearance.rounding.full
        color: query.activeFocus ? Colours.palette.m3surfaceContainerHighest : Colours.palette.m3surfaceContainerHigh
        Rectangle {
            anchors.fill: parent
            radius: parent.radius
            color: "transparent"
            border.width: 2
            border.color: Colours.palette.m3primary
            opacity: query.activeFocus ? 1 : 0
            Behavior on opacity {
                enabled: !Appearance.reducedEffects
                Anim {
                    duration: Appearance.anim.durations.small
                }
            }
        }
        RowLayout {
            anchors.fill: parent
            anchors.leftMargin: Appearance.padding.large + Appearance.padding.small
            anchors.rightMargin: Appearance.padding.larger
            MaterialIcon {
                text: qsTr("search")
                color: query.activeFocus ? Colours.palette.m3primary : Colours.palette.m3outline
                scale: query.activeFocus && browser.fx ? 1.15 : 1
                Behavior on scale {
                    WorkspaceSpring {}
                }
            }
            StyledTextField {
                id: query
                objectName: "workspaceQuery"
                Layout.fillWidth: true
                placeholderText: qsTr("Rechercher dans %1  (Ctrl+F)").arg(browser.title.toLocaleLowerCase())
                Keys.onReturnPressed: {
                    if (browser.entries.length)
                        Bridge.action("launch", {
                            id: browser.entries[0].id
                        });
                }
                // First Esc clears the text (overrides Main's Escape shortcut only when there is text); next Esc closes via Main.
                Keys.onShortcutOverride: event => {
                    if (event.key === Qt.Key_Escape && query.text.length > 0)
                        event.accepted = true;
                }
                Keys.onEscapePressed: {
                    if (query.text.length > 0)
                        query.clear();
                }
                Keys.onDownPressed: {
                    grid.forceActiveFocus();
                    if (browser.cur < 0)
                        browser.cur = 0;
                }
            }
            IconButton {
                icon: "close"
                type: IconButton.Text
                visible: query.text.length > 0
                onClicked: query.clear()
            }
        }
    }
    IconButton {
        icon: "star"
        type: IconButton.Tonal
        toggle: true
        checked: browser.favoritesOnly
        onClicked: {
            browser.markBulk();
            browser.favoritesOnly = !browser.favoritesOnly;
        }
    }
    IconButton {
        objectName: "workspaceFlat"
        icon: "sort_by_alpha"
        type: browser.flatMode ? IconButton.Tonal : IconButton.Text
        toggle: true
        checked: browser.flatMode
        onClicked: {
            browser.markBulk();
            browser.flatMode = !browser.flatMode;
        }
    }
    IconButton {
        icon: browser.listMode ? "grid_view" : "view_list"
        type: IconButton.Text
        onClicked: browser.listMode = !browser.listMode
    }
}
