pragma ComponentBehavior: Bound

import qs.components
import qs.components.filedialog
import qs.config
import Quickshell
import Quickshell.Widgets
import QtQuick
import QtQuick.Layouts

Item {
    id: root

    required property PersistentProperties visibilities
    required property PersistentProperties state
    required property FileDialog facePicker
    readonly property real nonAnimWidth: view.implicitWidth + viewWrapper.anchors.margins * 2
    readonly property real nonAnimHeight: tabs.implicitHeight + tabs.anchors.topMargin + view.implicitHeight + viewWrapper.anchors.margins * 2

    implicitWidth: nonAnimWidth
    implicitHeight: nonAnimHeight

    Tabs {
        id: tabs

        anchors.top: parent.top
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.topMargin: Appearance.padding.normal
        anchors.margins: Appearance.padding.large

        nonAnimWidth: root.nonAnimWidth - anchors.margins * 2
        state: root.state
    }

    // Plain clipping, no layer: an OpacityMask layer recomposites its content and
    // turns the glass opaque behind it, which broke the single shared alpha.
    Item {
        id: viewWrapper

        anchors.top: tabs.bottom
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.bottom: parent.bottom
        anchors.margins: Appearance.padding.large

        clip: true

        Flickable {
            id: view

            readonly property int currentIndex: root.state.currentTab
            readonly property Item currentItem: row.children[currentIndex]

            anchors.fill: parent

            flickableDirection: Flickable.HorizontalFlick

            implicitWidth: currentItem.implicitWidth
            implicitHeight: currentItem.implicitHeight

            contentX: currentItem.x
            contentWidth: row.implicitWidth
            contentHeight: row.implicitHeight

            onContentXChanged: {
                if (!moving)
                    return;

                const x = contentX - currentItem.x;
                if (x > currentItem.implicitWidth / 2)
                    root.state.currentTab = Math.min(root.state.currentTab + 1, tabs.count - 1);
                else if (x < -currentItem.implicitWidth / 2)
                    root.state.currentTab = Math.max(root.state.currentTab - 1, 0);
            }

            onDragEnded: {
                const x = contentX - currentItem.x;
                if (x > currentItem.implicitWidth / 10)
                    root.state.currentTab = Math.min(root.state.currentTab + 1, tabs.count - 1);
                else if (x < -currentItem.implicitWidth / 10)
                    root.state.currentTab = Math.max(root.state.currentTab - 1, 0);
                else
                    contentX = Qt.binding(() => currentItem.x);
            }

            RowLayout {
                id: row

                Pane {
                    paneIndex:0
                    sourceComponent: Dash {
                        visibilities: root.visibilities
                        state: root.state
                        facePicker: root.facePicker
                    }
                }

                Pane {
                    paneIndex:1
                    sourceComponent: Media {
                        visibilities: root.visibilities
                    }
                }

                Pane {
                    paneIndex:2
                    sourceComponent: Performance {}
                }

                Pane {
                    paneIndex:3
                    sourceComponent: Workspaces {}
                }
            }

            Behavior on contentX {
                Anim {}
            }
        }
    }

    component Pane: Loader {
        Layout.alignment: Qt.AlignTop

        id: pane
        required property int paneIndex
        active: false
        function ensureLoaded() {if(paneIndex===view.currentIndex&&!active)active=true}
        Component.onCompleted: ensureLoaded()
        Connections {target:view;function onCurrentIndexChanged(){pane.ensureLoaded()}}

    }
}
