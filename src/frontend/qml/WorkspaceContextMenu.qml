// Menu contextuel (clic droit) d'une tuile de l'espace.
// Extrait de WorkspaceBrowser.qml (pur découpage) : les ids du parent sont reçus en propriétés requises.
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.services
import qs.config

Item {
    id: context
    required property var browser
    required property var editor
    required property var nameField
    required property var categoryField
    objectName: "workspaceContext"
    anchors.fill: parent
    z: 90
    visible: false
    property bool opened: false
    function openMenu(px, py) {
        opened = true;
        menu.expanded = true;
        visible = true;
        catcher.enabled = true;
        menu.x = Math.max(8, Math.min(px, browser.width - menu.width - 8));
        menu.y = Math.max(8, Math.min(py, browser.height - menu.implicitHeight - 8));
    }
    function openAt(px, py) {
        openMenu(px, py);
    }
    function close() {
        opened = false;
        menu.expanded = false;
        catcher.enabled = false;
        visible = false;
    }
    MouseArea {
        id: catcher
        anchors.fill: parent
        enabled: false
        acceptedButtons: Qt.LeftButton | Qt.MiddleButton
        onPressed: context.close()
    }
    Menu {
        id: menu
        objectName: "workspaceMenu"
        width: Math.max(260, implicitWidth)
        height: implicitHeight
        readonly property bool app: browser.selectedKind !== "folder"
        readonly property var itemTexts: {
            const r = [];
            for (let i = 0; i < items.length; i++)
                r.push(items[i].text);
            return r;
        }
        property MenuItem miOpen: MenuItem {
            text: qsTr("Ouvrir")
            icon: "open_in_new"
            onClicked: Bridge.action("launch", {
                id: browser.selectedId
            })
        }
        property MenuItem miFav: MenuItem {
            text: browser.selectedFavorite ? qsTr("Retirer des favoris") : qsTr("Ajouter aux favoris")
            icon: "star"
            onClicked: Bridge.action("favorite", {
                id: browser.selectedId
            })
        }
        property MenuItem miReveal: MenuItem {
            text: qsTr("Ouvrir l’emplacement")
            icon: "folder_open"
            onClicked: Bridge.action("reveal", {
                id: browser.selectedId
            })
        }
        property MenuItem miAdmin: MenuItem {
            text: qsTr("Exécuter en administrateur")
            icon: "admin_panel_settings"
            onClicked: Bridge.action("dockApp", {
                id: browser.selectedId,
                action: "admin"
            })
        }
        property MenuItem miEdit: MenuItem {
            text: qsTr("Renommer et catégoriser")
            icon: "edit"
            onClicked: {
                nameField.text = browser.selectedName;
                categoryField.text = browser.selectedCategory;
                editor.open = true;
                nameField.forceActiveFocus();
            }
        }
        property MenuItem miIcon: MenuItem {
            text: qsTr("Choisir une icône")
            icon: "image"
            onClicked: Bridge.chooseEntryIcon(browser.selectedId)
        }
        property MenuItem miApps: MenuItem {
            text: qsTr("Ranger dans Applications")
            icon: "apps"
            onClicked: Bridge.action("move", {
                id: browser.selectedId,
                board: "apps"
            })
        }
        property MenuItem miGames: MenuItem {
            text: qsTr("Ranger dans Jeux")
            icon: "sports_esports"
            onClicked: Bridge.action("move", {
                id: browser.selectedId,
                board: "games"
            })
        }
        property MenuItem miProj: MenuItem {
            text: qsTr("Ranger dans Projets")
            icon: "code_blocks"
            onClicked: Bridge.action("move", {
                id: browser.selectedId,
                board: "projects"
            })
        }
        property MenuItem miDocs: MenuItem {
            text: qsTr("Ranger dans Documents")
            icon: "folder_open"
            onClicked: Bridge.action("move", {
                id: browser.selectedId,
                board: "documents"
            })
        }
        items: app ? [miOpen, miFav, miReveal, miAdmin, miEdit, miIcon, miApps, miGames, miProj, miDocs] : [miOpen, miFav, miReveal,
            miEdit, miIcon, miApps, miGames, miProj, miDocs]
        onItemSelected: item => {
            context.close();
            item.clicked();
        }
    }
}
