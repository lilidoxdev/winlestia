import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."
import "../winjets"

// Winjets : galerie, import d'un ZIP avec confirmation, instances posées sur le bureau et mode édition.
ColumnLayout {
    id: page

    property var host
    // Chemin du ZIP en cours d'inspection (confirmation affichée tant que `lastImport` le décrit).
    property string inspectedPath: ""
    // Après une importation réussie, pose aussitôt le paquet sur le bureau.
    property string addAfterImport: ""
    // Instance dont le formulaire de réglages est ouvert.
    property string openSettings: Bridge.winjets.pendingSettings

    readonly property var imported: Bridge.winjets.lastImport
    readonly property bool confirming: inspectedPath !== "" && imported.inspect === true && imported.ok === true
    readonly property var importedPackage: imported.package ?? ({})

    function inspectFile(url: string): void {
        inspectedPath = url;
        Bridge.winjets.inspect(url);
    }

    function previewUrl(pkg): string {
        return pkg && pkg.preview ? Bridge.winjets.fileUrl(pkg.preview) : "";
    }

    function networkText(pkg): string {
        const hosts = pkg && pkg.network ? pkg.network : [];
        return hosts.length > 0 ? qsTr("Accès réseau : %1").arg(hosts.join(", ")) : qsTr("Aucun accès réseau");
    }

    spacing: Appearance.spacing.larger

    Connections {
        target: Bridge.winjets

        function onImportChanged(): void {
            const last = Bridge.winjets.lastImport;
            if (page.addAfterImport !== "" && last.ok === true && last.inspect !== true) {
                Bridge.winjets.add(last.packageId || page.addAfterImport);
                page.addAfterImport = "";
                page.inspectedPath = "";
            }
        }
        function onSettingsRequested(id: string): void {
            page.openSettings = id;
        }
    }

    Component.onCompleted: openSettings = Bridge.winjets.pendingSettings

    // Entrée, bureau ------------------------------------------------------------------------------------------------

    SettingsSection {
        sectionId: "winjetsDesktop"
        label: qsTr("Bureau")

        SettingsRow {
            rowId: "winjetsEdit"
            preview: Component {
                PagePreview {
                    kind: "winjetsEdit"
                    value: editSwitch.checked
                }
            }
            title: qsTr("Modifier les Winjets")
            hint: qsTr("Déplacez, redimensionnez ou retirez les cartes directement sur le bureau.")

            SettingsSwitch {
                id: editSwitch

                objectName: "winjetsEditToggle"
                value: Bridge.winjets.editMode
                onToggled: Bridge.winjets.setEditMode(checked)
            }
        }

        SettingsRow {
            rowId: "winjetsRecover"
            preview: Component {
                PagePreview {
                    kind: "winjetsRecover"
                }
            }
            title: qsTr("Ramener tous les Winjets sur l'écran principal")
            hint: qsTr("Utile après le débranchement d'un écran.")

            IconTextButton {
                objectName: "winjetsRecoverButton"
                text: qsTr("Ramener")
                icon: "move_up"
                type: IconTextButton.Tonal
                onClicked: Bridge.winjets.recoverAll()
            }
        }
    }

    // Galerie -------------------------------------------------------------------------------------------------------

    SettingsSection {
        sectionId: "winjetsGallery"
        label: qsTr("Galerie")

        Flow {
            Layout.fillWidth: true
            Layout.margins: Appearance.padding.large
            spacing: Appearance.spacing.normal

            Repeater {
                model: Bridge.winjets.packages

                Rectangle {
                    id: tile

                    required property var modelData
                    objectName: "winjetTile"

                    width: 220
                    height: tileColumn.implicitHeight + Appearance.padding.normal * 2
                    radius: Appearance.rounding.normal
                    color: Colours.palette.m3surfaceContainerHigh

                    ColumnLayout {
                        id: tileColumn

                        anchors.fill: parent
                        anchors.margins: Appearance.padding.normal
                        spacing: Appearance.spacing.small

                        Image {
                            Layout.fillWidth: true
                            Layout.preferredHeight: 110
                            source: page.previewUrl(tile.modelData)
                            fillMode: Image.PreserveAspectFit
                            asynchronous: true
                            visible: source != ""
                        }
                        StyledText {
                            Layout.fillWidth: true
                            text: qsTr(tile.modelData.name ?? tile.modelData.id)
                            font.weight: Font.Medium
                            elide: Text.ElideRight
                        }
                        StyledText {
                            Layout.fillWidth: true
                            text: qsTr("par %1").arg(tile.modelData.author ?? "")
                            visible: !!tile.modelData.author
                            color: Colours.palette.m3outline
                            font.pointSize: Appearance.font.size.small
                            elide: Text.ElideRight
                        }
                        StyledText {
                            Layout.fillWidth: true
                            text: page.networkText(tile.modelData)
                            color: Colours.palette.m3outline
                            font.pointSize: Appearance.font.size.small
                            wrapMode: Text.WordWrap
                        }
                        RowLayout {
                            Layout.fillWidth: true
                            spacing: Appearance.spacing.small

                            IconTextButton {
                                objectName: "winjetAddButton"
                                text: qsTr("Ajouter")
                                icon: "add"
                                onClicked: Bridge.winjets.add(tile.modelData.id)
                            }
                            IconButton {
                                visible: tile.modelData.builtin !== true
                                icon: "delete"
                                onClicked: Bridge.winjets.uninstall(tile.modelData.id)
                            }
                        }
                    }
                }
            }
        }
    }

    // Importer un ZIP -----------------------------------------------------------------------------------------------

    SettingsSection {
        sectionId: "winjetsImport"
        label: qsTr("Importer un Winjet")

        Rectangle {
            id: drop

            objectName: "winjetsDropZone"
            Layout.fillWidth: true
            Layout.margins: Appearance.padding.large
            implicitHeight: 96
            radius: Appearance.rounding.normal
            color: dropArea.containsDrag ? Qt.alpha(Colours.palette.m3primary, 0.14) : "transparent"
            border.width: 2
            border.color: dropArea.containsDrag ? Colours.palette.m3primary : Colours.palette.m3outlineVariant

            DropArea {
                id: dropArea

                anchors.fill: parent
                onEntered: drag => drag.accepted = drag.hasUrls
                onDropped: drop => {
                    if (drop.hasUrls && drop.urls.length > 0)
                        page.inspectFile(drop.urls[0].toString());
                }
            }

            RowLayout {
                anchors.centerIn: parent
                spacing: Appearance.spacing.normal

                MaterialIcon {
                    text: "folder_zip"
                    color: Colours.palette.m3outline
                    font.pointSize: Appearance.font.size.large
                }
                StyledText {
                    text: qsTr("Déposez un fichier .zip ici")
                    color: Colours.palette.m3outline
                }
                IconTextButton {
                    objectName: "winjetsPickButton"
                    text: qsTr("Choisir un fichier")
                    icon: "upload_file"
                    type: IconTextButton.Tonal
                    onClicked: {
                        const path = Bridge.winjets.pickPackage();
                        if (path)
                            page.inspectedPath = path;
                    }
                }
            }
        }

        // Erreurs de validation.
        StyledText {
            Layout.fillWidth: true
            Layout.margins: Appearance.padding.large
            visible: page.inspectedPath !== "" && page.imported.ok === false
            text: (page.imported.errors ?? []).join("\n")
            color: Colours.palette.m3error
            wrapMode: Text.WordWrap
        }

        // Confirmation : aperçu, nom, auteur déclaré et accès réseau, avant toute installation.
        Rectangle {
            objectName: "winjetsConfirm"
            Layout.fillWidth: true
            Layout.margins: Appearance.padding.large
            visible: page.confirming
            implicitHeight: confirmRow.implicitHeight + Appearance.padding.large * 2
            radius: Appearance.rounding.normal
            color: Colours.palette.m3surfaceContainerHigh

            RowLayout {
                id: confirmRow

                anchors.fill: parent
                anchors.margins: Appearance.padding.large
                spacing: Appearance.spacing.large

                Image {
                    Layout.preferredWidth: 160
                    Layout.preferredHeight: 100
                    source: page.previewUrl(page.importedPackage)
                    fillMode: Image.PreserveAspectFit
                    visible: source != ""
                }
                ColumnLayout {
                    Layout.fillWidth: true
                    spacing: Appearance.spacing.smaller

                    StyledText {
                        Layout.fillWidth: true
                        text: qsTr(page.importedPackage.name ?? "")
                        font.weight: Font.Medium
                        font.pointSize: Appearance.font.size.larger
                        elide: Text.ElideRight
                    }
                    StyledText {
                        Layout.fillWidth: true
                        text: qsTr("Auteur déclaré : %1").arg(page.importedPackage.author ?? "")
                        color: Colours.palette.m3outline
                        elide: Text.ElideRight
                    }
                    StyledText {
                        Layout.fillWidth: true
                        text: page.networkText(page.importedPackage)
                        color: Colours.palette.m3outline
                        wrapMode: Text.WordWrap
                    }
                    StyledText {
                        Layout.fillWidth: true
                        visible: page.imported.updated === true
                        text: qsTr("Ce Winjet est déjà installé : l'importation le met à jour.")
                        wrapMode: Text.WordWrap
                    }
                    RowLayout {
                        spacing: Appearance.spacing.small

                        IconTextButton {
                            objectName: "winjetsConfirmAdd"
                            text: qsTr("Ajouter au bureau")
                            icon: "add"
                            onClicked: {
                                page.addAfterImport = page.importedPackage.id ?? "";
                                Bridge.winjets.importPackage(page.inspectedPath);
                            }
                        }
                        IconTextButton {
                            objectName: "winjetsConfirmImport"
                            text: page.imported.updated === true ? qsTr("Mettre à jour") : qsTr("Importer")
                            icon: "download"
                            type: IconTextButton.Tonal
                            onClicked: {
                                Bridge.winjets.importPackage(page.inspectedPath);
                                page.inspectedPath = "";
                            }
                        }
                        TextButton {
                            text: qsTr("Annuler")
                            type: TextButton.Text
                            onClicked: page.inspectedPath = ""
                        }
                    }
                }
            }
        }
    }

    // Instances posées -----------------------------------------------------------------------------------------------

    SettingsSection {
        sectionId: "winjetsInstances"
        label: qsTr("Sur le bureau")

        StyledText {
            Layout.fillWidth: true
            Layout.margins: Appearance.padding.large
            visible: Bridge.winjets.instances.length === 0
            text: qsTr("Aucun Winjet sur le bureau pour le moment.")
            color: Colours.palette.m3outline
        }

        Repeater {
            model: Bridge.winjets.instances

            ColumnLayout {
                id: entry

                required property var modelData
                readonly property var info: {
                    Bridge.winjets.revision;
                    return Bridge.winjets.instance(modelData.id);
                }
                readonly property bool open: page.openSettings === modelData.id

                objectName: "winjetInstance"
                Layout.fillWidth: true
                Layout.leftMargin: Appearance.padding.large
                Layout.rightMargin: Appearance.padding.large
                spacing: Appearance.spacing.small

                RowLayout {
                    Layout.fillWidth: true
                    spacing: Appearance.spacing.small

                    StyledText {
                        Layout.fillWidth: true
                        text: entry.info.packageName ?? entry.modelData.packageId
                        opacity: entry.modelData.hidden ? 0.6 : 1
                        elide: Text.ElideRight
                    }
                    IconButton {
                        icon: entry.modelData.hidden ? "visibility" : "visibility_off"
                        onClicked: Bridge.winjets.setHidden(entry.modelData.id, !entry.modelData.hidden)
                    }
                    IconButton {
                        icon: "tune"
                        toggle: true
                        checked: entry.open
                        onClicked: page.openSettings = entry.open ? "" : entry.modelData.id
                    }
                    IconButton {
                        icon: "system_update_alt"
                        onClicked: {
                            const path = Bridge.winjets.pickPackage();
                            if (path)
                                page.inspectedPath = path;
                        }
                    }
                    IconButton {
                        icon: "delete"
                        onClicked: Bridge.winjets.remove(entry.modelData.id)
                    }
                }

                Loader {
                    Layout.fillWidth: true
                    active: entry.open
                    visible: active
                    sourceComponent: WinjetSettingsForm {
                        inst: entry.info
                    }
                }
            }
        }
    }
}
