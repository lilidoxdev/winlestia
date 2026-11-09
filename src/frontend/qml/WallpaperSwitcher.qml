// Sélecteur de fond d'écran plein écran (disposition du sélecteur Caelestia).
// Créé par un Loader actif seulement à l'ouverture : au repos, aucun timer, aucune animation, aucune image.
// Le fond est un aperçu en direct du fichier sélectionné ; le fond d'écran du système ne change qu'avec Entrée.
import QtQuick
import QtQuick.Window
import qs.components
import qs.components.controls
import qs.services
import qs.config

Window {
    id: root
    objectName: "wallpaperSwitcher"
    title: "Panneaux · Fonds d’écran"
    transientParent: null
    color: Colours.palette.m3scrim
    flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint
    x: Bridge.screenX
    y: Bridge.screenY
    width: Bridge.screenWidth
    height: Bridge.screenHeight
    visible: true
    signal closeRequested
    readonly property bool reduced: Appearance.reducedEffects
    readonly property real ratio: Bridge.screenWidth / Math.max(1, Bridge.screenHeight)
    readonly property int selectedHeight: Math.round(Bridge.screenHeight * .17)
    readonly property int normalHeight: Math.round(selectedHeight * .68)
    property int current: 0
    property bool dialogOpen: false
    property bool confirmDelete: false
    property bool notRemovable: false
    property int hiddenCount: 0
    property string wantPath: ""
    property bool seenActive: false
    property int front: 0
    property int target: 0   // 0 = tous les écrans, sinon l'id d'écran du backend (proposé seulement avec plus d'un écran)
    readonly property var displays: (Bridge.wallpaperPicker || {}).displays || []
    readonly property var animated: (Bridge.wallpaperPicker || {}).animated || []
    readonly property bool multi: displays.length > 1
    property int pending: -1
    readonly property string currentPath: current >= 0 && current < items.count ? items.get(current).path : ""
    readonly property string currentUrl: current >= 0 && current < items.count ? items.get(current).url : ""
    readonly property string currentName: current >= 0 && current < items.count ? items.get(current).name : ""
    readonly property string currentOrigin: current >= 0 && current < items.count ? items.get(current).origin : ""
    readonly property bool currentRemovable: currentOrigin === "user" || currentOrigin === "bundled"

    ListModel {
        id: items
    }
    onDisplaysChanged: {
        if (target !== 0 && !displays.some(function (d) {
            return d.id === target;
        }))
            target = 0;
    }
    function norm(value) {
        return String(value || "").toLowerCase().split("\\").join("/").replace("file:///", "");
    }
    function load() {
        var keep = wantPath || currentPath, choices = Bridge.wallpaperChoices(""), want = keep || norm((Bridge.wallpaperInfo || {}).source);
        wantPath = "";
        confirmDelete = false;
        notRemovable = false;
        hiddenCount = Bridge.hiddenWallpaperCount();
        items.clear();
        var index = 0;
        for (var i = 0; i < choices.length; i++) {
            items.append({
                path: choices[i].path,
                name: choices[i].name,
                url: choices[i].url,
                origin: choices[i].origin || "",
                thumb: ""
            });
            if (want && norm(choices[i].path) === norm(want))
                index = i;
        }
        index = Math.max(0, Math.min(index, items.count - 1));
        current = index;
        list.currentIndex = index;
        show(currentUrl);
        var order = [];
        for (var j = 0; j < choices.length; j++)
            order.push(choices[(index + j) % choices.length].path);
        Bridge.requestWallpaperThumbs(order);
    }
    function move(step) {
        if (items.count < 1)
            return;
        confirmDelete = false;
        notRemovable = false;
        current = (current + step + items.count) % items.count;
        list.currentIndex = current;
        show(currentUrl);
    }
    function select(index) {
        if (index < 0 || index >= items.count)
            return;
        confirmDelete = false;
        notRemovable = false;
        current = index;
        list.currentIndex = index;
        show(currentUrl);
    }
    function apply() {
        if (currentPath !== "" && Bridge.wallpaperChoices("").some(function (w) {
            return w.path === currentPath;
        }))
            Bridge.applyWallpaper(currentPath, multi ? target : 0);
        closeRequested();
    }
    function cycleTarget() {
        if (multi)
            target = (target + 1) % (displays.length + 1);
    }
    function displayLabel(d) {
        return d.primary ?
            qsTr("Écran %1 (principal) · %2×%3").arg(d.id).arg(d.width).arg(d.height) :
            qsTr("Écran %1 · %2×%3").arg(d.id).arg(d.width).arg(d.height);
    }
    // Le dialogue natif ne peut pas passer au-dessus de cette fenêtre sans cadre toujours au premier plan :
    // on la masque pendant le dialogue (sans fermeture automatique), puis on revient avec le focus.
    function addImages() {
        if (dialogOpen)
            return;
        confirmDelete = false;
        dialogOpen = true;
        visible = false;
        addTimer.start();
    }
    function finishAdd() {
        var added = Bridge.addWallpaperImages();
        dialogOpen = false;
        visible = true;
        requestActivate();
        keys.forceActiveFocus();
        if (added.length > 0) {
            wantPath = norm(added[0]);
            Bridge.refreshWallpapersNow();
        }
    }
    function removeCurrent() {
        var path = currentPath,
            next = current + 1 < items.count ? items.get(current + 1).path :
                current > 0 ? items.get(current - 1).path : "";
        var bundled = currentOrigin === "bundled";
        confirmDelete = false;
        var ok = path !== "" && (bundled ? Bridge.hideWallpaper(path) : Bridge.removeWallpaper(path));
        if (ok) {
            wantPath = norm(next);
            Bridge.refreshWallpapersNow();
            load();
        }
    }
    function restoreHidden() {
        if (Bridge.restoreHiddenWallpapers()) {
            wantPath = currentPath;
            Bridge.refreshWallpapersNow();
            load();
        }
    }
    Timer {
        id: addTimer
        interval: 80
        repeat: false
        onTriggered: root.finishAdd()
    }
    // Fondu enchaîné : la couche cachée charge le fichier suivant en asynchrone et prend le relais une fois Ready.
    function show(url) {
        if (url === "")
            return;
        var layers = [layerA, layerB], visibleLayer = layers[front], next = layers[1 - front];
        if (visibleLayer.source.toString() === url) {
            pending = -1;
            return;
        }
        if (next.source.toString() === url && next.status === Image.Ready) {
            front = 1 - front;
            pending = -1;
            return;
        }
        pending = 1 - front;
        next.source = url;
    }
    function layerReady(index) {
        if (pending === index) {
            front = index;
            pending = -1;
        }
    }

    Component.onCompleted: {
        Bridge.requestWallpaperPicker();
        Bridge.refreshWallpapersNow();
        load();
        requestActivate();
        keys.forceActiveFocus();
    }
    onVisibleChanged: if (visible) {
        requestActivate();
        keys.forceActiveFocus();
    }
    onActiveChanged: {
        if (active)
            seenActive = true;
        else if (seenActive && !dialogOpen)
            closeRequested();
    }
    onClosing: event => {
        event.accepted = false;
        closeRequested();
    }
    Connections {
        target: Bridge
        function onWallpapersReady() {
            root.load();
        }
        function onWallpaperThumbReady(path, url) {
            for (var i = 0; i < items.count; i++)
                if (items.get(i).path === path) {
                    items.setProperty(i, "thumb", url);
                    break;
                }
        }
    }

    Item {
        id: keys
        anchors.fill: parent
        focus: true
        Keys.onPressed: event => {
            if (root.confirmDelete) {
                if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter)
                    root.removeCurrent();
                else
                    root.confirmDelete = false;
                event.accepted = true;
                return;
            }
            if (event.key === Qt.Key_Left)
                root.move(-1);
            else if (event.key === Qt.Key_Right)
                root.move(1);
            else if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter)
                root.apply();
            else if (event.key === Qt.Key_Escape)
                root.closeRequested();
            else if (event.key === Qt.Key_Tab && root.multi)
                root.cycleTarget();
            else if (event.key === Qt.Key_O && (event.modifiers & Qt.ControlModifier))
                root.addImages();
            else if (event.key === Qt.Key_Delete) {
                if (root.currentRemovable)
                    root.confirmDelete = true;
                else if (root.currentPath !== "")
                    root.notRemovable = true;
            } else
                return;
            event.accepted = true;
        }
        MouseArea {
            anchors.fill: parent
            onWheel: wheel => {
                root.move(wheel.angleDelta.y > 0 || wheel.angleDelta.x > 0 ? -1 : 1);
            }
        }

        Image {
            id: layerA
            anchors.fill: parent
            asynchronous: true
            cache: false
            fillMode: Image.PreserveAspectCrop
            sourceSize: Qt.size(Bridge.screenWidth, Bridge.screenHeight)
            opacity: root.front === 0 ? 1 : 0
            onStatusChanged: if (status === Image.Ready)
                root.layerReady(0)
            Behavior on opacity {
                enabled: !root.reduced
                Anim {
                    duration: 250
                }
            }
        }
        Image {
            id: layerB
            anchors.fill: parent
            asynchronous: true
            cache: false
            fillMode: Image.PreserveAspectCrop
            sourceSize: Qt.size(Bridge.screenWidth, Bridge.screenHeight)
            opacity: root.front === 1 ? 1 : 0
            onStatusChanged: if (status === Image.Ready)
                root.layerReady(1)
            Behavior on opacity {
                enabled: !root.reduced
                Anim {
                    duration: 250
                }
            }
        }

        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.bottom: parent.bottom
            height: parent.height * .4
            gradient: Gradient {
                GradientStop {
                    position: 0
                    color: Qt.alpha(Colours.palette.m3scrim, 0)
                }
                GradientStop {
                    position: 1
                    color: Qt.alpha(Colours.palette.m3scrim, .7)
                }
            }
        }

        StyledRect {
            visible: root.confirmDelete || root.notRemovable
            anchors.horizontalCenter: parent.horizontalCenter
            anchors.top: parent.top
            anchors.topMargin: Appearance.padding.large * 2
            radius: Appearance.rounding.full
            color: Qt.alpha(Colours.palette.m3surfaceContainer, .92)
            implicitWidth: confirmText.implicitWidth + Appearance.padding.large * 2
            implicitHeight: confirmText.implicitHeight + Appearance.padding.normal * 2
            StyledText {
                id: confirmText
                anchors.centerIn: parent
                text: root.notRemovable ? qsTr("Les images du dossier Images ne sont pas supprimées ici") :
                    root.currentOrigin === "bundled" ?
                        qsTr("Masquer ce fond par défaut ? Entrée pour confirmer · Échap annuler") :
                        qsTr("Supprimer ce fond de votre dossier ? Entrée confirmer · Échap annuler")
                font.pointSize: Appearance.font.size.small
                color: Colours.palette.m3onSurface
            }
        }

        StyledText {
            visible: items.count === 0
            anchors.centerIn: parent
            text: qsTr("Aucun fond d’écran trouvé")
            font.pointSize: Appearance.font.size.larger
            color: Colours.palette.m3onSurface
        }

        Column {
            anchors.horizontalCenter: parent.horizontalCenter
            anchors.bottom: parent.bottom
            anchors.bottomMargin: Appearance.padding.large * 2
            width: parent.width
            spacing: Appearance.spacing.normal
            ListView {
                id: list
                width: parent.width
                height: root.selectedHeight + Appearance.padding.normal * 2
                orientation: ListView.Horizontal
                model: items
                spacing: Appearance.spacing.normal
                clip: false
                interactive: false
                highlightRangeMode: ListView.StrictlyEnforceRange
                highlightMoveDuration: root.reduced ? 0 : 250
                preferredHighlightBegin: (width - root.selectedHeight * root.ratio) / 2
                preferredHighlightEnd: (width + root.selectedHeight * root.ratio) / 2
                delegate: Item {
                    id: cell
                    required property int index
                    required property string name
                    required property string thumb
                    readonly property bool selected: ListView.isCurrentItem
                    width: (selected ? root.selectedHeight : root.normalHeight) * root.ratio
                    height: list.height
                    Behavior on width {
                        enabled: !root.reduced
                        Anim {
                            duration: 250
                        }
                    }
                    StyledClippingRect {
                        id: frame
                        anchors.horizontalCenter: parent.horizontalCenter
                        anchors.verticalCenter: parent.verticalCenter
                        width: parent.width
                        height: cell.selected ? root.selectedHeight : root.normalHeight
                        radius: Appearance.rounding.normal
                        color: Colours.palette.m3surfaceContainerHigh
                        Behavior on height {
                            enabled: !root.reduced
                            Anim {
                                duration: 250
                            }
                        }
                        Image {
                            anchors.fill: parent
                            source: cell.thumb
                            asynchronous: true
                            cache: false
                            fillMode: Image.PreserveAspectCrop
                            sourceSize.width: 480
                        }
                    }
                    Rectangle {
                        anchors.fill: frame
                        radius: Appearance.rounding.normal
                        color: "transparent"
                        border.width: cell.selected ? 3 : 0
                        border.color: Colours.palette.m3primary
                    }
                    MouseArea {
                        anchors.fill: parent
                        onClicked: root.select(cell.index)
                    }
                }
            }
            Row {
                anchors.horizontalCenter: parent.horizontalCenter
                spacing: Appearance.spacing.normal
                StyledText {
                    text: root.currentName
                    font.pointSize: Appearance.font.size.normal
                    color: Colours.palette.m3onSurface
                    elide: Text.ElideRight
                    width: Math.min(implicitWidth, root.width * .6)
                }
                StyledText {
                    visible: items.count > 0
                    text: (root.current + 1) + " / " + items.count
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3onSurfaceVariant
                }
            }
            StyledText {
                visible: root.animated.length > 0
                anchors.horizontalCenter: parent.horizontalCenter
                text: qsTr("%1 masque le fond choisi tant qu’il tourne").arg(root.animated.join(", "))
                font.pointSize: Appearance.font.size.small
                color: Colours.palette.m3tertiary
            }
            Row {
                visible: root.multi
                anchors.horizontalCenter: parent.horizontalCenter
                spacing: Appearance.spacing.small
                StyledText {
                    anchors.verticalCenter: parent.verticalCenter
                    text: qsTr("Tab changer d’écran")
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3outline
                }
                IconTextButton {
                    text: qsTr("Tous les écrans")
                    icon: "desktop_windows"
                    type: root.target === 0 ? IconTextButton.Tonal : IconTextButton.Text
                    onClicked: root.target = 0
                }
                Repeater {
                    model: root.displays
                    IconTextButton {
                        required property var modelData
                        text: root.displayLabel(modelData)
                        icon: "monitor"
                        type: root.target === modelData.id ? IconTextButton.Tonal : IconTextButton.Text
                        onClicked: root.target = modelData.id
                    }
                }
            }
            Row {
                anchors.horizontalCenter: parent.horizontalCenter
                spacing: Appearance.spacing.normal
                IconTextButton {
                    text: qsTr("Ajouter des images")
                    icon: "add_photo_alternate"
                    type: IconTextButton.Tonal
                    onClicked: root.addImages()
                }
                IconTextButton {
                    visible: root.hiddenCount > 0
                    text: qsTr("Restaurer les fonds par défaut")
                    icon: "restore"
                    type: IconTextButton.Text
                    onClicked: root.restoreHidden()
                }
                IconTextButton {
                    text: qsTr("Ouvrir le dossier")
                    icon: "folder_open"
                    type: IconTextButton.Text
                    onClicked: Bridge.openWallpaperFolder()
                }
            }
            StyledText {
                anchors.horizontalCenter: parent.horizontalCenter
                text: qsTr("← → choisir · Entrée appliquer · Ctrl+O ajouter · Suppr retirer · Échap fermer")
                font.pointSize: Appearance.font.size.small
                color: Colours.palette.m3outline
            }
        }
    }
}
