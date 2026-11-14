// Vue d'ensemble : miniatures en direct de toutes les fenêtres du bureau virtuel courant.
// Créée par un Loader actif seulement à l'ouverture : au repos, aucun timer, aucune animation, aucune miniature.
// Les miniatures sont dessinées par DWM AU-DESSUS de cette fenêtre (enregistrées par Bridge.overview) : la zone de chaque vignette
// reste vide côté QML, et tout ce qui l'entoure (anneau de sélection, étiquette) est dessiné en dehors de son rectangle.
import QtQuick
import QtQuick.Window
import "OverviewLayout.js" as Layout
import qs.components
import qs.components.controls
import qs.services
import qs.config

Window {
    id: root
    objectName: "windowOverview"
    title: "Panneaux · Vue d’ensemble"
    transientParent: null
    color: Colours.palette.m3scrim
    flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint
    x: Bridge.screenX
    y: Bridge.screenY
    width: Bridge.screenWidth
    height: Bridge.screenHeight
    visible: true
    signal closeRequested

    readonly property int refreshInterval: 1500
    readonly property int headerHeight: 72
    readonly property int footerHeight: 44
    readonly property int closeSize: 28
    readonly property int iconSize: 20
    readonly property int ringWidth: 3
    readonly property int ringGap: 4
    readonly property int fadeSteps: 3
    readonly property int afterCloseDelay: 400
    readonly property real maxWidthRatio: 0.42
    readonly property real maxHeightRatio: 0.5
    readonly property real minimizedOpacity: 0.55
    readonly property real minAspect: 0.3
    readonly property real maxAspect: 4
    readonly property int edge: Appearance.padding.large * 3
    readonly property int labelBlock: closeSize + Appearance.spacing.small
    readonly property int areaWidth: width - edge * 2
    readonly property int areaHeight: height - headerHeight - footerHeight - edge

    property real scrim: 0
    readonly property int step: Math.round(scrim * fadeSteps)
    property bool ready: false
    property bool closing: false
    property bool seenActive: false
    property string filter: ""
    property string selected: ""
    property var rects: ({})
    property var order: []
    property string signature: ""
    readonly property int shownCount: order.length

    Behavior on scrim {
        enabled: !Appearance.reducedEffects
        Anim {
            duration: Appearance.anim.durations.small
        }
    }
    onStepChanged: placeAll()

    ListModel {
        id: windows
    }

    function indexOf(hwnd) {
        for (var i = 0; i < windows.count; i++)
            if (windows.get(i).hwnd === hwnd)
                return i;
        return -1;
    }
    // Mise à jour sur place par hwnd : les fenêtres connues gardent leur ligne, les nouvelles s'ajoutent, les disparues partent.
    function sync(list) {
        var seen = {}, i, keys = ["entryId", "title", "app", "iconId", "ww", "wh", "minimized"];
        for (i = 0; i < list.length; i++) {
            var it = list[i], row = {
                hwnd: String(it.hwnd),
                entryId: it.id || "",
                title: it.title || "",
                app: it.app || "",
                iconId: it.iconId || "",
                ww: Math.max(1, it.w || 1),
                wh: Math.max(1, it.h || 1),
                minimized: !!it.minimized,
                preview: false
            };
            seen[row.hwnd] = true;
            var at = indexOf(row.hwnd);
            if (at < 0)
                windows.append(row);
            else
                for (var k = 0; k < keys.length; k++)
                    if (windows.get(at)[keys[k]] !== row[keys[k]])
                        windows.setProperty(at, keys[k], row[keys[k]]);
        }
        for (i = windows.count - 1; i >= 0; i--)
            if (!seen[windows.get(i).hwnd]) {
                Bridge.overview.remove(windows.get(i).hwnd);
                windows.remove(i);
            }
        ready = true;
        relayout(false);
    }
    function aspectOf(row) {
        return Math.max(minAspect, Math.min(maxAspect, row.ww / row.wh));
    }
    function relayout(force) {
        var shown = [], aspects = [], sig = filter + "|";
        for (var i = 0; i < windows.count; i++) {
            var row = windows.get(i);
            if (!Layout.matches(row, filter))
                continue;
            shown.push(row.hwnd);
            aspects.push(aspectOf(row));
            sig += row.hwnd + ":" + Math.round(aspects[aspects.length - 1] * 100) + ",";
        }
        if (!force && sig === signature)
            return;
        signature = sig;
        var out = Layout.layout(aspects, {w: areaWidth, h: areaHeight}, Appearance.spacing.large, labelBlock,
            Math.round(areaWidth * maxWidthRatio), Math.round(areaHeight * maxHeightRatio));
        var map = {}, list = [];
        for (var j = 0; j < shown.length; j++) {
            var r = {x: out[j].x + edge, y: out[j].y + headerHeight, w: out[j].w, h: out[j].h};
            map[shown[j]] = r;
            list.push(r);
        }
        order = shown;
        rects = map;
        rectList = list;
        if (shown.length > 0 && shown.indexOf(selected) < 0)
            selected = shown[0];
        placeAll();
    }
    property var rectList: []
    function placeAll() {
        if (!ready || closing)
            return;
        var level = Math.min(fadeSteps, step) / fadeSteps;
        for (var i = 0; i < windows.count; i++) {
            var row = windows.get(i), r = rects[row.hwnd];
            if (!r) {
                Bridge.overview.remove(row.hwnd);
                continue;
            }
            var ok = Bridge.overview.place(root, row.hwnd, r.x, r.y, r.w, r.h, level * (row.minimized ? minimizedOpacity : 1));
            if (row.preview !== ok)
                windows.setProperty(i, "preview", ok);
        }
    }
    function move(dx, dy) {
        var at = order.indexOf(selected);
        var next = Layout.neighbour(rectList, at, dx, dy);
        if (next >= 0 && next < order.length)
            selected = order[next];
    }
    function entryOf(hwnd) {
        var at = indexOf(hwnd);
        return at < 0 ? null : windows.get(at);
    }
    function activateWindow(hwnd) {
        var row = entryOf(hwnd);
        if (!row)
            return;
        Bridge.overview.act(hwnd, "activate");
        dismiss();
    }
    function closeWindow(hwnd) {
        var row = entryOf(hwnd);
        if (!row)
            return;
        Bridge.overview.act(hwnd, "close");
        afterClose.restart();
    }
    function setFilter(text) {
        filter = text;
        relayout(true);
    }
    // Les miniatures partent d'abord, puis le fond s'efface.
    function dismiss() {
        if (closing)
            return;
        closing = true;
        Bridge.overview.clear();
        Bridge.overview.stop();
        scrim = 0;
        closeTimer.start();
    }
    function release() {
        Bridge.overview.clear();
        Bridge.overview.stop();
    }

    Component.onCompleted: {
        requestActivate();
        keys.forceActiveFocus();
        Bridge.overview.request();
        scrim = 1;
    }
    Component.onDestruction: release()
    onVisibleChanged: {
        if (visible) {
            requestActivate();
            keys.forceActiveFocus();
        } else {
            release();
        }
    }
    onActiveChanged: {
        if (active)
            seenActive = true;
        else if (seenActive)
            dismiss();
    }
    onClosing: event => {
        event.accepted = false;
        dismiss();
    }
    onWidthChanged: if (ready)
        relayout(true)
    onHeightChanged: if (ready)
        relayout(true)

    Connections {
        target: Bridge.overview
        function onWindowsChanged() {
            if (!root.closing)
                root.sync(Bridge.overview.windows);
        }
    }
    Timer {
        interval: root.refreshInterval
        repeat: true
        running: root.visible && root.ready && !root.closing
        onTriggered: {
            Bridge.overview.request();
            root.placeAll();
        }
    }
    Timer {
        id: afterClose
        interval: root.afterCloseDelay
        onTriggered: if (!root.closing)
            Bridge.overview.request()
    }
    Timer {
        id: closeTimer
        interval: Appearance.anim.durations.small
        onTriggered: root.closeRequested()
    }

    Item {
        id: keys
        anchors.fill: parent
        opacity: root.scrim
        focus: true
        Keys.onPressed: event => {
            if (event.key === Qt.Key_Escape) {
                if (root.filter !== "")
                    root.setFilter("");
                else
                    root.dismiss();
            } else if (event.key === Qt.Key_Left)
                root.move(-1, 0);
            else if (event.key === Qt.Key_Right)
                root.move(1, 0);
            else if (event.key === Qt.Key_Up)
                root.move(0, -1);
            else if (event.key === Qt.Key_Down)
                root.move(0, 1);
            else if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter) {
                if (root.shownCount > 0)
                    root.activateWindow(root.selected);
            } else if (event.key === Qt.Key_Delete) {
                if (root.shownCount > 0)
                    root.closeWindow(root.selected);
            } else if (event.key === Qt.Key_Backspace)
                root.setFilter(root.filter.slice(0, -1));
            else if (event.text.length === 1 && event.text >= " " && !(event.modifiers & (Qt.ControlModifier | Qt.AltModifier)))
                root.setFilter(root.filter + event.text);
            else
                return;
            event.accepted = true;
        }
        MouseArea {
            anchors.fill: parent
            onClicked: root.dismiss()
        }

        Row {
            id: header
            x: root.edge
            y: Appearance.padding.large * 2
            height: root.headerHeight - Appearance.padding.large * 2
            spacing: Appearance.spacing.large
            StyledText {
                anchors.verticalCenter: parent.verticalCenter
                text: qsTr("Vue d’ensemble")
                font.pointSize: Appearance.font.size.title
                color: Colours.palette.m3onSurface
            }
            StyledText {
                anchors.verticalCenter: parent.verticalCenter
                text: qsTr("%n fenêtre(s)", "", windows.count)
                font.pointSize: Appearance.font.size.normal
                color: Colours.palette.m3onSurfaceVariant
            }
            StyledRect {
                visible: root.filter !== ""
                anchors.verticalCenter: parent.verticalCenter
                radius: Appearance.rounding.full
                color: Colours.palette.m3surfaceContainerHigh
                implicitWidth: filterRow.implicitWidth + Appearance.padding.large * 2
                implicitHeight: filterRow.implicitHeight + Appearance.padding.normal * 2
                Row {
                    id: filterRow
                    anchors.centerIn: parent
                    spacing: Appearance.spacing.small
                    MaterialIcon {
                        anchors.verticalCenter: parent.verticalCenter
                        text: "search"
                        color: Colours.palette.m3primary
                    }
                    StyledText {
                        anchors.verticalCenter: parent.verticalCenter
                        text: root.filter
                        font.pointSize: Appearance.font.size.normal
                        color: Colours.palette.m3onSurface
                    }
                }
            }
        }

        StyledText {
            visible: root.ready && root.shownCount === 0
            anchors.centerIn: parent
            text: root.filter !== "" ? qsTr("Aucune fenêtre ne correspond") : qsTr("Aucune fenêtre ouverte")
            font.pointSize: Appearance.font.size.larger
            color: Colours.palette.m3onSurface
        }

        Repeater {
            model: windows
            delegate: Item {
                id: card
                required property string hwnd
                required property string entryId
                required property string title
                required property string app
                required property string iconId
                required property bool minimized
                required property bool preview
                readonly property var rect: root.rects[hwnd]
                readonly property bool current: root.selected === hwnd
                visible: rect !== undefined
                x: rect ? rect.x : 0
                y: rect ? rect.y : 0
                width: rect ? rect.w : 0
                height: rect ? rect.h + root.labelBlock : 0

                // Fond visible tant que DWM n'a rien dessiné : icône de l'application à la place de l'aperçu.
                StyledRect {
                    width: parent.width
                    height: card.rect ? card.rect.h : 0
                    radius: Appearance.rounding.small
                    color: Colours.palette.m3surfaceContainerHigh
                    visible: !card.preview
                    Image {
                        id: bigIcon
                        anchors.centerIn: parent
                        width: Math.min(parent.width, parent.height) * 0.4
                        height: width
                        sourceSize.width: Math.ceil(width * Screen.devicePixelRatio)
                        sourceSize.height: Math.ceil(height * Screen.devicePixelRatio)
                        source: card.iconId !== "" ? Bridge.iconUrl(card.iconId, Bridge.iconRevision) : ""
                        asynchronous: true
                        fillMode: Image.PreserveAspectFit
                    }
                    MaterialIcon {
                        anchors.centerIn: parent
                        visible: bigIcon.status !== Image.Ready
                        text: "web_asset"
                        font.pointSize: Appearance.font.size.extraLarge
                        color: Colours.palette.m3onSurfaceVariant
                    }
                }
                // Anneau de sélection : autour du rectangle de la vignette, jamais dedans (DWM dessine par-dessus).
                Rectangle {
                    x: -(root.ringGap + root.ringWidth)
                    y: x
                    width: parent.width - 2 * x
                    height: (card.rect ? card.rect.h : 0) - 2 * x
                    radius: Appearance.rounding.small + root.ringGap
                    color: "transparent"
                    border.width: root.ringWidth
                    border.color: Colours.palette.m3primary
                    visible: card.current
                }
                Row {
                    id: label
                    y: (card.rect ? card.rect.h : 0) + Appearance.spacing.small
                    width: parent.width
                    height: root.closeSize
                    spacing: Appearance.spacing.small
                    Item {
                        width: root.iconSize
                        height: root.iconSize
                        anchors.verticalCenter: parent.verticalCenter
                        Image {
                            id: smallIcon
                            anchors.fill: parent
                            sourceSize.width: Math.ceil(root.iconSize * Screen.devicePixelRatio)
                            sourceSize.height: Math.ceil(root.iconSize * Screen.devicePixelRatio)
                            source: card.iconId !== "" ? Bridge.iconUrl(card.iconId, Bridge.iconRevision) : ""
                            asynchronous: true
                            fillMode: Image.PreserveAspectFit
                        }
                        MaterialIcon {
                            anchors.centerIn: parent
                            visible: smallIcon.status !== Image.Ready
                            text: "web_asset"
                            color: Colours.palette.m3primary
                        }
                    }
                    StyledText {
                        anchors.verticalCenter: parent.verticalCenter
                        width: Math.max(0, label.width - root.iconSize - root.closeSize - hint.width - label.spacing * (hint.visible ? 3 : 2))
                        text: card.title
                        font.pointSize: Appearance.font.size.normal
                        color: card.current ? Colours.palette.m3onSurface : Colours.palette.m3onSurfaceVariant
                        elide: Text.ElideRight
                    }
                    StyledText {
                        id: hint
                        visible: card.minimized
                        anchors.verticalCenter: parent.verticalCenter
                        text: qsTr("Réduite")
                        font.pointSize: Appearance.font.size.small
                        color: Colours.palette.m3outline
                    }
                }
                MouseArea {
                    width: parent.width
                    height: card.rect ? card.rect.h : 0
                    hoverEnabled: true
                    acceptedButtons: Qt.LeftButton | Qt.MiddleButton
                    cursorShape: Qt.PointingHandCursor
                    onEntered: root.selected = card.hwnd
                    onClicked: mouse => {
                        if (mouse.button === Qt.MiddleButton)
                            root.closeWindow(card.hwnd);
                        else
                            root.activateWindow(card.hwnd);
                    }
                }
                StyledRect {
                    x: parent.width - width
                    y: label.y
                    width: root.closeSize
                    height: root.closeSize
                    radius: Appearance.rounding.full
                    color: "transparent"
                    StateLayer {
                        radius: parent.radius
                        function onClicked() {
                            root.closeWindow(card.hwnd);
                        }
                    }
                    MaterialIcon {
                        anchors.centerIn: parent
                        text: "close"
                        font.pointSize: Appearance.font.size.large
                        color: Colours.palette.m3onSurfaceVariant
                    }
                }
            }
        }

        StyledText {
            anchors.horizontalCenter: parent.horizontalCenter
            anchors.bottom: parent.bottom
            anchors.bottomMargin: Appearance.padding.large
            text: qsTr("Flèches choisir · Entrée ouvrir · Suppr fermer · Lettres filtrer · Échap quitter")
            font.pointSize: Appearance.font.size.small
            color: Colours.palette.m3outline
        }
    }
}
