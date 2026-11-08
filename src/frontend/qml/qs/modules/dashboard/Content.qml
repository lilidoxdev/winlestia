pragma ComponentBehavior: Bound

import qs.components
import qs.components.filedialog
import qs.components.controls
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
    readonly property real nonAnimHeight: tabs.implicitHeight + tabs.anchors.topMargin + view.implicitHeight + viewWrapper.anchors.margins
        * 2

    // Taille fixe du contenu du tiroir : maximum des volets, ne fait que croître (à la première mesure d'un volet).
    property real paneMaxWidth: 966
    property real paneMaxHeight: 450

    // Vrai pendant le glissement d'onglet ; Wrapper s'en sert pour n'activer sa couche hors écran qu'alors.
    readonly property bool animating: slide.running

    implicitWidth: nonAnimWidth
    implicitHeight: nonAnimHeight

    // Précharge les panneaux un par un (async) une fois le tableau de bord affiché ; en pause quand il est masqué.
    function preloadStep(): void {
        if (!root.visible)
            return;
        // Le panneau où se trouve l'utilisateur (ou vers lequel il bascule) passe toujours en premier.
        const cur = row.children[root.state.currentTab];
        if (cur && !cur.active)
            cur.active = true;
        for (let i = 0; i < row.children.length; i++) {
            const p = row.children[i];
            if (p.status === Loader.Loading)
                return;
            if (!p.active) {
                p.active = true;
                return;
            }
        }
    }

    onVisibleChanged: preloadStep()
    Component.onCompleted: Qt.callLater(preloadStep)

    Tabs {
        id: tabs

        anchors.top: parent.top
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.topMargin: Appearance.padding.normal
        anchors.margins: Appearance.padding.large
        anchors.rightMargin: Appearance.padding.large + settingsButton.width + Appearance.padding.small

        nonAnimWidth: root.nonAnimWidth - anchors.leftMargin - anchors.rightMargin
        state: root.state
    }

    // Les réglages restent toujours accessibles depuis le tableau de bord (Ctrl+Alt+Espace), même dock masqué.
    IconButton {
        id: settingsButton

        objectName: "dashboardSettingsButton"
        anchors.right: parent.right
        anchors.rightMargin: Appearance.padding.large
        anchors.verticalCenter: tabs.verticalCenter
        anchors.verticalCenterOffset: -Appearance.padding.small
        icon: "settings"
        type: IconButton.Text
        onClicked: Bridge.action("settings", {})
    }

    // Rognage simple, sans couche : une couche OpacityMask recompose son contenu et
    // rend le verre opaque derrière, ce qui cassait l'alpha unique partagé.
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
            readonly property Pane currentItem: row.children[currentIndex]
            // Le panneau sortant reste affiché jusqu'à la fin du glissement ; lastIndex retient l'ancienne valeur.
            property int previousIndex: -1
            property int lastIndex: currentIndex

            onCurrentIndexChanged: {
                previousIndex = lastIndex;
                lastIndex = currentIndex;
                Qt.callLater(() => {
                    if (!slide.running)
                        previousIndex = -1;
                });
            }

            anchors.fill: parent

            flickableDirection: Flickable.HorizontalFlick

            implicitWidth: root.paneMaxWidth
            implicitHeight: root.paneMaxHeight

            contentX: currentItem.cellX
            contentWidth: row.implicitWidth
            contentHeight: row.implicitHeight

            onContentXChanged: {
                if (!moving)
                    return;

                const x = contentX - currentItem.cellX;
                if (x > root.paneMaxWidth / 2)
                    root.state.currentTab = Math.min(root.state.currentTab + 1, tabs.count - 1);
                else if (x < -root.paneMaxWidth / 2)
                    root.state.currentTab = Math.max(root.state.currentTab - 1, 0);
            }

            onDragEnded: {
                const x = contentX - currentItem.cellX;
                if (x > root.paneMaxWidth / 10)
                    root.state.currentTab = Math.min(root.state.currentTab + 1, tabs.count - 1);
                else if (x < -root.paneMaxWidth / 10)
                    root.state.currentTab = Math.max(root.state.currentTab - 1, 0);
                else
                    contentX = Qt.binding(() => currentItem.cellX);
            }

            RowLayout {
                id: row

                Pane {
                    paneIndex: 0
                    sourceComponent: Dash {
                        visibilities: root.visibilities
                        state: root.state
                        facePicker: root.facePicker
                    }
                }

                Pane {
                    paneIndex: 1
                    sourceComponent: Media {
                        visibilities: root.visibilities
                    }
                }

                Pane {
                    paneIndex: 2
                    sourceComponent: Performance {}
                }

                Pane {
                    paneIndex: 3
                    sourceComponent: Workspaces {
                        tabActive: root.state.currentTab === 3
                    }
                }
            }

            Behavior on contentX {
                Anim {
                    id: slide

                    onRunningChanged: if (!running)
                        view.previousIndex = -1

                    duration: Appearance.reducedEffects ? 0 : Appearance.anim.durations.normal
                }
            }
        }
    }

    component Pane: Loader {
        id: pane

        required property int paneIndex
        // Mesuré une fois chargé, pour que l'emplacement non chargé (et le premier glissement) ait la bonne taille.
        property real cachedWidth: [823, 966, 835, 900][paneIndex] ?? 800
        property real cachedHeight: [429, 340, 270, 450][paneIndex] ?? 340
        readonly property bool current: paneIndex === view.currentIndex
        readonly property bool shown: current || slide.running || view.dragging || view.moving || paneIndex === view.previousIndex

        Layout.alignment: Qt.AlignTop

        // Chaque panneau occupe une cellule fixe (max) ; des marges égales le centrent sur les deux axes.
        Layout.preferredWidth: cachedWidth
        Layout.preferredHeight: cachedHeight
        Layout.leftMargin: Math.max(0, (root.paneMaxWidth - cachedWidth) / 2)
        Layout.rightMargin: Layout.leftMargin
        Layout.topMargin: (root.paneMaxHeight - cachedHeight) / 2
        Layout.bottomMargin: Layout.topMargin
        // Début de la cellule du panneau (x inclut la marge de centrage).
        readonly property real cellX: x - Layout.leftMargin

        onCachedWidthChanged: if (cachedWidth > root.paneMaxWidth)
            root.paneMaxWidth = cachedWidth
        onCachedHeightChanged: if (cachedHeight > root.paneMaxHeight)
            root.paneMaxHeight = cachedHeight
        asynchronous: paneIndex > 0
        active: false

        // Layout ignore les éléments invisibles : on masque donc l'élément chargé plutôt que le Loader.
        Binding {
            target: pane.item
            property: "visible"
            value: pane.shown
            when: pane.item !== null
        }

        Connections {
            target: pane.item
            ignoreUnknownSignals: true

            function onImplicitWidthChanged(): void {
                pane.cachedWidth = pane.item.implicitWidth;
            }

            function onImplicitHeightChanged(): void {
                pane.cachedHeight = pane.item.implicitHeight;
            }
        }

        onLoaded: {
            cachedWidth = item.implicitWidth;
            cachedHeight = item.implicitHeight;
        }
        onStatusChanged: {
            if (status === Loader.Ready)
                Qt.callLater(root.preloadStep);
        }
        onCurrentChanged: if (current)
            active = true
        Component.onCompleted: if (current)
            active = true
    }
}
