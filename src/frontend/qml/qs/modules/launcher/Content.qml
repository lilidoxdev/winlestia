pragma ComponentBehavior: Bound

import "services"
import qs.components
import qs.components.controls
import qs.services
import qs.config
import Quickshell
import QtQuick

Item {
    id: root

    required property PersistentProperties visibilities
    required property var panels
    required property real maxHeight

    readonly property int padding: Appearance.padding.large
    readonly property int rounding: Appearance.rounding.large
    readonly property bool wallpaperMode: list.showWallpapers

    implicitWidth: listWrapper.width + padding * 2
    implicitHeight: searchWrapper.height + listWrapper.height + padding * 2

    Item {
        id: listWrapper

        implicitWidth: list.width
        implicitHeight: list.height + root.padding

        anchors.horizontalCenter: parent.horizontalCenter
        anchors.bottom: searchWrapper.top
        anchors.bottomMargin: root.padding

        ContentList {
            id: list

            content: root
            visibilities: root.visibilities
            panels: root.panels
            maxHeight: root.maxHeight - searchWrapper.implicitHeight - root.padding * 3
            search: search
            padding: root.padding
            rounding: root.rounding
        }
    }

    StyledRect {
        id: searchWrapper

        color: Colours.layer(Colours.palette.m3surfaceContainer, 2)
        radius: Appearance.rounding.full

        anchors.left: parent.left
        anchors.right: parent.right
        anchors.bottom: parent.bottom
        anchors.margins: root.padding

        implicitHeight: Math.max(searchIcon.implicitHeight, search.implicitHeight, clearIcon.implicitHeight)

        MaterialIcon {
            id: searchIcon

            anchors.verticalCenter: parent.verticalCenter
            anchors.left: parent.left
            anchors.leftMargin: root.padding

            text: qsTr("search")
            color: Colours.palette.m3onSurfaceVariant
        }

        StyledTextField {
            id: search
            objectName: "paletteQuery"
            onTextChanged: Bridge.searchPalette(text)

            anchors.left: searchIcon.right
            anchors.right: clearIcon.left
            anchors.leftMargin: Appearance.spacing.small
            anchors.rightMargin: Appearance.spacing.small

            topPadding: Appearance.padding.larger
            bottomPadding: Appearance.padding.larger

            placeholderText: qsTr("Type \"%1\" for commands").arg(Config.launcher.actionPrefix)

            // Chemin d'activation commun à Entrée et à Ctrl+1…9 : lance l'élément courant de la liste affichée, quel que soit son type.
            function activateCurrent(): bool {
                const currentItem = list.currentList?.currentItem;
                if (!currentItem)
                    return false;
                Apps.launch(currentItem.modelData);
                return true;
            }

            onAccepted: activateCurrent()
            Keys.onUpPressed: list.currentList?.decrementCurrentIndex()
            Keys.onDownPressed: list.currentList?.incrementCurrentIndex()
            Keys.onLeftPressed: event => {
                if (list.showEmoji && list.currentList)
                    list.currentList.moveCurrentIndexLeft();
                else
                    event.accepted = false;
            }
            Keys.onRightPressed: event => {
                if (list.showEmoji && list.currentList)
                    list.currentList.moveCurrentIndexRight();
                else
                    event.accepted = false;
            }

            Keys.onEscapePressed: root.visibilities.launcher = false

            Keys.onPressed: event => {
                // Ctrl+1…Ctrl+9 : active le n-ième élément actuellement visible de la liste courante (comme Entrée dessus).
                // Les listes sans notion de rangée (carrousel de fonds d'écran : pas d'indexAt) ne réagissent pas.
                if ((event.modifiers & Qt.ControlModifier) && event.key >= Qt.Key_1 && event.key <= Qt.Key_9) {
                    const cur = list.currentList;
                    let done = false;
                    if (cur && typeof cur.indexAt === "function") {
                        const left = cur.contentX + 1, top = cur.contentY + 1;
                        let first = cur.indexAt(left, top);
                        if (first < 0)
                            first = cur.indexAt(left, top + (cur.spacing ?? 0));  // le point tombait dans l'espacement entre deux rangées
                        if (first < 0 && cur.contentY <= cur.originY)
                            first = 0;
                        const index = first < 0 ? -1 : first + (event.key - Qt.Key_1);
                        const target = index >= 0 && index < cur.count ? cur.itemAtIndex(index) : null;
                        if (target && target.y < cur.contentY + cur.height && target.y + target.height > cur.contentY) {
                            cur.currentIndex = index;
                            done = search.activateCurrent();
                        }
                    }
                    event.accepted = done;
                    return;
                }

                // Tab / Maj+Tab déplacent la sélection, même sans les raccourcis vim (Qt envoie Backtab pour Maj+Tab).
                if (event.key === Qt.Key_Tab) {
                    if (event.modifiers & Qt.ShiftModifier)
                        list.currentList?.decrementCurrentIndex();
                    else
                        list.currentList?.incrementCurrentIndex();
                    event.accepted = true;
                    return;
                } else if (event.key === Qt.Key_Backtab) {
                    list.currentList?.decrementCurrentIndex();
                    event.accepted = true;
                    return;
                }

                if (!Config.launcher.vimKeybinds)
                    return;

                if (event.modifiers & Qt.ControlModifier) {
                    if (event.key === Qt.Key_J) {
                        list.currentList?.incrementCurrentIndex();
                        event.accepted = true;
                    } else if (event.key === Qt.Key_K) {
                        list.currentList?.decrementCurrentIndex();
                        event.accepted = true;
                    }
                }
            }

            Component.onCompleted: forceActiveFocus()

            Connections {
                target: root.visibilities

                function onLauncherChanged(): void {
                    if (!root.visibilities.launcher)
                        search.text = "";
                }

                function onSessionChanged(): void {
                    if (!root.visibilities.session)
                        search.forceActiveFocus();
                }
            }
        }

        MaterialIcon {
            id: clearIcon

            anchors.verticalCenter: parent.verticalCenter
            anchors.right: parent.right
            anchors.rightMargin: root.padding

            width: search.text ? implicitWidth : implicitWidth / 2
            opacity: {
                if (!search.text)
                    return 0;
                if (mouse.pressed)
                    return 0.7;
                if (mouse.containsMouse)
                    return 0.8;
                return 1;
            }

            text: qsTr("close")
            color: Colours.palette.m3onSurfaceVariant

            MouseArea {
                id: mouse

                anchors.fill: parent
                hoverEnabled: true
                cursorShape: search.text ? Qt.PointingHandCursor : undefined

                onClicked: search.text = ""
            }

            Behavior on width {
                Anim {
                    duration: Appearance.anim.durations.small
                }
            }

            Behavior on opacity {
                Anim {
                    duration: Appearance.anim.durations.small
                }
            }
        }
    }
}
