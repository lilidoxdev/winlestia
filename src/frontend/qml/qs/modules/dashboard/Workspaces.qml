// Workspaces tab: greeting header, 4 category cards (blob shapes + icon preview), favourites, recents, quick actions.
// Motion: staggered entrance when the tab becomes visible (expressive spatial curve, slight overshoot),
// spring hover/press. Everything is gated on `shown` (tab active AND dashboard visible); no Timer, no looping animation.
import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes
import qs.components
import qs.components.controls
import qs.services
import qs.config
Item {
    id: root
    implicitWidth: 900
    implicitHeight: col.implicitHeight

    // Set by the dashboard (current tab == Workspaces). Defaults to true for standalone use.
    property bool tabActive: true
    readonly property bool shown: tabActive && visible
    readonly property bool fx: !Appearance.reducedEffects
    property int hour: new Date().getHours()
    onShownChanged: if (shown) hour = new Date().getHours()

    readonly property var boards: [
        {id: "apps", name: qsTr("Applications"), icon: "apps", lobes: 8, amp: 0.09},
        {id: "games", name: qsTr("Jeux"), icon: "sports_esports", lobes: 4, amp: 0.17},
        {id: "projects", name: qsTr("Projets"), icon: "code_blocks", lobes: 6, amp: 0.13},
        {id: "documents", name: qsTr("Documents"), icon: "folder_open", lobes: 5, amp: 0.12}
    ]
    readonly property int favCount: Math.min(6, (Bridge.favorites ?? []).length)
    readonly property int recentCount: Math.min(3, (Bridge.recent ?? []).length)

    // One pass over Bridge.apps (re-evaluated only when the catalogue changes): count + 3 preview icons per board.
    readonly property var boardInfo: {
        const info = {};
        for (const b of boards) info[b.id] = {count: 0, top: []};
        const list = Bridge.apps ?? [];
        for (let i = 0; i < list.length; i++) {
            const e = list[i], x = info[e.board];
            if (!x) continue;
            x.count++;
            if (x.top.some(t => t.id === e.id)) continue;
            const rank = e.recent >= 0 ? e.recent : (e.favorite ? 500 + i : 1000 + i);
            let at = x.top.findIndex(t => t.rank > rank);
            if (at < 0) at = x.top.length;
            if (at < 3) { x.top.splice(at, 0, {id: e.id, name: e.name, rank: rank}); if (x.top.length > 3) x.top.pop(); }
        }
        return info;
    }

    function tone(id: string): color {
        const p = Colours.palette;
        switch (id) {
        case "games": return p.m3secondaryContainer;
        case "projects": return Qt.tint(p.m3surfaceContainerHigh, Qt.alpha(p.m3tertiary, 0.30));
        case "documents": return Qt.tint(p.m3surfaceContainerHigh, Qt.alpha(p.m3error, 0.24));
        default: return p.m3primaryContainer;
        }
    }
    function onTone(id: string): color {
        const p = Colours.palette;
        switch (id) {
        case "games": return p.m3onSecondaryContainer;
        case "projects": return p.m3tertiary;
        case "documents": return p.m3error;
        default: return p.m3onPrimaryContainer;
        }
    }

    // Spring-like number animation (overshoot curve). Reduced effects: plain standard curve, durations already scaled.
    component Spring: NumberAnimation {
        duration: Appearance.anim.durations.expressiveFastSpatial
        easing.type: Easing.BezierSpline
        easing.bezierCurve: Appearance.reducedEffects ? Appearance.anim.curves.standard : Appearance.anim.curves.expressiveFastSpatial
    }

    // Staggered entrance. Place as a child of the animated item and bind opacity/scale/transform to alpha/k/dy.
    // Plays from 0 each time `shown` turns true; stops when hidden. Nothing runs while the tab is not shown.
    component Entrance: Item {
        id: ent
        property int order: 0
        property real reveal: 0
        readonly property real alpha: Math.min(1, reveal * 1.8)
        readonly property real dy: root.fx ? (1 - reveal) * 22 : 0
        readonly property real k: root.fx ? 0.9 + 0.1 * reveal : 1
        visible: false
        function play(): void {
            seq.stop();
            reveal = 0;
            if (root.shown) seq.start();
        }
        Component.onCompleted: play()
        Connections {
            target: root
            function onShownChanged(): void { if (root.shown) ent.play(); else seq.stop(); }
        }
        SequentialAnimation {
            id: seq
            PauseAnimation { duration: root.fx ? ent.order * 35 : 0 }
            NumberAnimation {
                target: ent
                property: "reveal"
                from: 0
                to: 1
                duration: Appearance.anim.durations.expressiveDefaultSpatial
                easing.type: Easing.BezierSpline
                easing.bezierCurve: root.fx ? Appearance.anim.curves.expressiveDefaultSpatial : Appearance.anim.curves.standard
            }
        }
    }

    // Cookie / clover style blob: r(t) = 1 + amp * cos(lobes * t), polygon computed once per parameter change.
    component Blob: Shape {
        id: blob
        property int lobes: 8
        property real amp: 0.07
        property color color: Colours.palette.m3primaryContainer
        readonly property var pts: {
            const n = 96, r0 = Math.min(width, height) / 2 / (1 + amp), out = [];
            for (let i = 0; i <= n; i++) {
                const t = i / n * Math.PI * 2, r = r0 * (1 + amp * Math.cos(lobes * t));
                out.push(Qt.point(width / 2 + r * Math.cos(t), height / 2 + r * Math.sin(t)));
            }
            return out;
        }
        preferredRendererType: Shape.CurveRenderer
        implicitWidth: 56
        implicitHeight: 56
        ShapePath {
            strokeWidth: -1
            fillColor: blob.color
            PathPolyline { path: blob.pts }
            Behavior on fillColor { CAnim {} }
        }
    }

    // App icon with letter fallback (used only when the icon is missing or failed to load).
    component Letter: StyledRect {
        id: letter
        property string name
        property real size: 36
        implicitWidth: size
        implicitHeight: size
        radius: Appearance.rounding.full
        color: Colours.palette.m3primaryContainer
        StyledText {
            anchors.centerIn: parent
            text: (letter.name ?? "").trim().charAt(0).toUpperCase()
            color: Colours.palette.m3onPrimaryContainer
            font.pointSize: Math.round(letter.size * 0.17) + 3
            font.weight: 600
        }
    }
    component AppIcon: Item {
        id: ai
        property string appId
        property string appName
        property real size: 40
        implicitWidth: size
        implicitHeight: size
        Image {
            id: img
            anchors.fill: parent
            fillMode: Image.PreserveAspectFit
            source: ai.appId ? Bridge.iconUrl(ai.appId, Bridge.iconRevision) : ""
            asynchronous: true
            mipmap: true
            sourceSize: Qt.size(ai.size * 2, ai.size * 2)
            visible: status === Image.Ready
        }
        Letter { anchors.fill: parent; size: ai.size; name: ai.appName; visible: img.status !== Image.Ready }
    }

    // Tonal pill button: state layer, spring hover/press, focus ring, staggered entrance.
    component Pill: StyledRect {
        id: pill
        property string icon
        property string text
        property bool filled
        property alias order: pe.order
        signal clicked
        property real hk: pl.pressed ? 0.97 : pl.containsMouse ? 1.04 : 1
        Behavior on hk { Spring {} }
        implicitHeight: 36
        implicitWidth: pillRow.implicitWidth + Appearance.padding.large * 2
        radius: Appearance.rounding.full
        color: filled ? Colours.palette.m3primaryContainer : Colours.palette.m3surfaceContainerHigh
        activeFocusOnTab: true
        Accessible.role: Accessible.Button
        Accessible.name: text
        Keys.onReturnPressed: clicked()
        Keys.onSpacePressed: clicked()
        opacity: pe.alpha
        scale: pe.k * hk
        transform: Translate { y: pe.dy }
        Entrance { id: pe; order: 9 }
        StateLayer {
            id: pl
            color: pill.filled ? Colours.palette.m3onPrimaryContainer : Colours.palette.m3onSurface
            function onClicked(): void { pill.clicked() }
        }
        Row {
            id: pillRow
            anchors.centerIn: parent
            spacing: Appearance.spacing.small
            MaterialIcon {
                anchors.verticalCenter: parent.verticalCenter
                text: pill.icon
                fill: pl.containsMouse ? 1 : 0
                color: pill.filled ? Colours.palette.m3onPrimaryContainer : Colours.palette.m3primary
                font.pointSize: Appearance.font.size.normal
                Behavior on fill { Anim {} }
            }
            StyledText {
                anchors.verticalCenter: parent.verticalCenter
                text: pill.text
                color: pill.filled ? Colours.palette.m3onPrimaryContainer : Colours.palette.m3onSurface
            }
        }
        Rectangle {
            anchors.fill: parent
            radius: parent.radius
            color: "transparent"
            border.width: 2
            border.color: Colours.palette.m3primary
            visible: pill.activeFocus
        }
    }

    ColumnLayout {
        id: col
        anchors.fill: parent
        spacing: Appearance.spacing.normal

        // Header: greeting by time of day
        RowLayout {
            id: header
            Layout.fillWidth: true
            spacing: Appearance.spacing.normal
            opacity: he.alpha
            transform: Translate { y: he.dy }
            Entrance { id: he; order: 0 }
            Item {
                implicitWidth: 52
                implicitHeight: 52
                Blob { anchors.fill: parent; lobes: 9; amp: 0.06; color: Colours.palette.m3primaryContainer
                    rotation: he.reveal * 40
                }
                MaterialIcon {
                    anchors.centerIn: parent
                    text: root.hour < 5 || root.hour >= 20 ? "bedtime" : root.hour < 12 ? "wb_twilight" : "wb_sunny"
                    fill: 1
                    color: Colours.palette.m3onPrimaryContainer
                    font.pointSize: Appearance.font.size.large
                }
            }
            ColumnLayout {
                spacing: 0
                StyledText {
                    text: root.hour < 5 ? qsTr("Bonne nuit") : root.hour < 12 ? qsTr("Bonjour") : root.hour < 18 ? qsTr("Bon après-midi") : qsTr("Bonsoir")
                    font.pointSize: Appearance.font.size.small
                    font.weight: 500
                    color: Colours.palette.m3primary
                }
                StyledText { text: qsTr("Reprendre ton espace"); font.pointSize: Appearance.font.size.title; font.weight: 600 }
            }
            Item { Layout.fillWidth: true }
            Pill { order: 1; icon: "arrow_forward"; text: qsTr("Tout afficher"); filled: true; onClicked: Bridge.navigate("workspace:all") }
        }

        // Category cards
        RowLayout {
            Layout.fillWidth: true
            spacing: Appearance.spacing.normal
            Repeater {
                model: root.boards
                StyledRect {
                    id: space
                    required property var modelData
                    required property int index
                    readonly property var info: root.boardInfo[modelData.id] ?? ({count: 0, top: []})
                    readonly property bool hot: sl.containsMouse || activeFocus
                    property real hk: sl.pressed ? 0.97 : sl.containsMouse ? 1.03 : 1
                    Behavior on hk { Spring {} }
                    objectName: "workspace-" + modelData.id
                    Layout.fillWidth: true
                    Layout.preferredWidth: 1
                    implicitHeight: 118
                    radius: Appearance.rounding.large
                    color: Colours.palette.m3surfaceContainer
                    z: hot ? 1 : 0
                    opacity: ce.alpha
                    scale: ce.k * hk
                    transform: Translate { y: ce.dy }
                    Entrance { id: ce; order: 1 + space.index }
                    activeFocusOnTab: true
                    Accessible.role: Accessible.Button
                    Accessible.name: modelData.name
                    Keys.onReturnPressed: Bridge.navigate("workspace:" + modelData.id)
                    Keys.onSpacePressed: Bridge.navigate("workspace:" + modelData.id)
                    StateLayer { id: sl; function onClicked(): void { Bridge.navigate("workspace:" + space.modelData.id) } }

                    Blob {
                        id: shape
                        x: Appearance.padding.normal
                        y: Appearance.padding.normal
                        width: 62
                        height: 62
                        lobes: space.modelData.lobes
                        amp: space.modelData.amp
                        color: root.tone(space.modelData.id)
                        rotation: space.hot ? 24 : 0
                        scale: space.hot ? 1.08 : 1
                        Behavior on rotation { Spring {} }
                        Behavior on scale { Spring {} }
                    }
                    MaterialIcon {
                        x: shape.x + (shape.width - width) / 2
                        y: shape.y + (shape.height - height) / 2
                        text: space.modelData.icon
                        fill: space.hot ? 1 : 0
                        color: root.onTone(space.modelData.id)
                        font.pointSize: Appearance.font.size.extraLarge - 3
                        Behavior on fill { Anim {} }
                    }
                    Column {
                        anchors.right: parent.right
                        anchors.top: parent.top
                        anchors.rightMargin: Appearance.padding.large
                        anchors.topMargin: Appearance.padding.normal
                        StyledText {
                            anchors.right: parent.right
                            text: space.info.count
                            font.pointSize: Appearance.font.size.extraLarge + 6
                            font.weight: 600
                        }
                        StyledText {
                            anchors.right: parent.right
                            text: qsTr("accès")
                            font.pointSize: Appearance.font.size.small
                            color: Colours.palette.m3onSurfaceVariant
                        }
                    }
                    StyledText {
                        anchors.left: parent.left
                        anchors.bottom: parent.bottom
                        anchors.right: stack.left
                        anchors.leftMargin: Appearance.padding.normal + 2
                        anchors.bottomMargin: Appearance.padding.normal
                        text: space.modelData.name
                        elide: Text.ElideRight
                        font.pointSize: Appearance.font.size.normal
                        font.weight: 500
                    }
                    // Overlapped mini-preview of up to 3 real icons; fans out on hover.
                    Item {
                        id: stack
                        anchors.right: parent.right
                        anchors.bottom: parent.bottom
                        anchors.rightMargin: Appearance.padding.normal
                        anchors.bottomMargin: Appearance.padding.normal
                        width: 26 + Math.max(0, space.info.top.length - 1) * (space.hot ? 20 : 15)
                        height: 26
                        Behavior on width { Spring {} }
                        Repeater {
                            model: space.info.top.length
                            Rectangle {
                                id: chip
                                required property int index
                                readonly property var app: space.info.top[index]
                                x: (space.info.top.length - 1 - index) * (space.hot ? 20 : 15)
                                width: 26
                                height: 26
                                radius: 13
                                color: Colours.palette.m3surfaceContainerHigh
                                border.width: 2
                                border.color: Colours.palette.m3surfaceContainer
                                z: 3 - index
                                Behavior on x { Spring {} }
                                AppIcon { anchors.centerIn: parent; size: 18; appId: chip.app?.id ?? ""; appName: chip.app?.name ?? "" }
                            }
                        }
                    }
                    Rectangle { anchors.fill: parent; radius: parent.radius; color: "transparent"; border.width: 2; border.color: Colours.palette.m3primary; visible: space.activeFocus }
                }
            }
        }

        // Favourites + recents
        RowLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            spacing: Appearance.spacing.normal

            StyledRect {
                id: favCard
                Layout.fillWidth: true
                Layout.fillHeight: true
                Layout.preferredWidth: 5
                implicitHeight: 188
                radius: Appearance.rounding.large
                color: Colours.palette.m3surfaceContainer
                opacity: fe.alpha
                transform: Translate { y: fe.dy }
                Entrance { id: fe; order: 5 }
                ColumnLayout {
                    anchors.fill: parent
                    anchors.margins: Appearance.padding.large
                    spacing: Appearance.spacing.small
                    RowLayout {
                        Layout.fillWidth: true
                        MaterialIcon { text: "star"; fill: 1; color: Colours.palette.m3primary; font.pointSize: Appearance.font.size.large }
                        StyledText { text: qsTr("Tes favoris"); font.pointSize: Appearance.font.size.normal; font.weight: 500; Layout.fillWidth: true }
                        StyledText {
                            visible: root.favCount === 0
                            text: qsTr("Ajoute des favoris depuis le clic droit d’un accès")
                            font.pointSize: Appearance.font.size.small
                            color: Colours.palette.m3onSurfaceVariant
                        }
                    }
                    RowLayout {
                        id: favFlow
                        Layout.fillWidth: true
                        Layout.fillHeight: true
                        spacing: Appearance.spacing.small
                        Repeater {
                            // Count-based model: delegates rebind to Bridge.favorites[index] instead of being rebuilt.
                            model: root.favCount
                            StyledRect {
                                id: tile
                                required property int index
                                readonly property var fav: (Bridge.favorites ?? [])[index] ?? ({})
                                readonly property bool hot: tl.containsMouse || activeFocus
                                property real hk: tl.pressed ? 0.96 : tl.containsMouse ? 1.05 : 1
                                Behavior on hk { Spring {} }
                                Layout.fillWidth: true
                                Layout.fillHeight: true
                                Layout.preferredWidth: 1
                                Layout.maximumWidth: 120
                                radius: Appearance.rounding.normal
                                color: Colours.palette.m3surfaceContainerHigh
                                z: hot ? 1 : 0
                                opacity: te.alpha
                                scale: te.k * hk
                                transform: [Translate { y: te.dy }, Translate { y: tile.hot ? -4 : 0; Behavior on y { Spring {} } }]
                                Entrance { id: te; order: 6 + tile.index }
                                activeFocusOnTab: true
                                Accessible.role: Accessible.Button
                                Accessible.name: fav.name ?? ""
                                Keys.onReturnPressed: Bridge.action("launch", {id: fav.id})
                                Keys.onSpacePressed: Bridge.action("launch", {id: fav.id})
                                StateLayer { id: tl; function onClicked(): void { Bridge.action("launch", {id: tile.fav.id}) } }
                                Column {
                                    anchors.centerIn: parent
                                    width: parent.width - 12
                                    spacing: Appearance.spacing.small
                                    AppIcon { anchors.horizontalCenter: parent.horizontalCenter; size: 48; appId: tile.fav.id ?? ""; appName: tile.fav.name ?? "" }
                                    StyledText {
                                        width: parent.width
                                        text: tile.fav.name ?? ""
                                        horizontalAlignment: Text.AlignHCenter
                                        font.pointSize: Appearance.font.size.small
                                        elide: Text.ElideRight
                                    }
                                }
                                Rectangle { anchors.fill: parent; radius: parent.radius; color: "transparent"; border.width: 2; border.color: Colours.palette.m3primary; visible: tile.activeFocus }
                                Component.onCompleted: if (fav.id) Bridge.action("icon", {id: fav.id})
                                onFavChanged: if (fav.id) Bridge.action("icon", {id: fav.id})
                            }
                        }
                        // "+" add tile, styled like the favourites
                        StyledRect {
                            id: addTile
                            readonly property bool hot: al.containsMouse || activeFocus
                            property real hk: al.pressed ? 0.96 : al.containsMouse ? 1.05 : 1
                            Behavior on hk { Spring {} }
                            visible: root.favCount < 6
                            Layout.fillWidth: true
                            Layout.fillHeight: true
                            Layout.preferredWidth: 1
                            Layout.maximumWidth: 120
                            radius: Appearance.rounding.normal
                            color: "transparent"
                            border.width: 2
                            border.color: addTile.hot ? Colours.palette.m3primary : Colours.palette.m3outlineVariant
                            Behavior on border.color { CAnim {} }
                            z: hot ? 1 : 0
                            opacity: ae.alpha
                            scale: ae.k * hk
                            transform: Translate { y: ae.dy }
                            Entrance { id: ae; order: 6 + root.favCount }
                            activeFocusOnTab: true
                            Accessible.role: Accessible.Button
                            Accessible.name: qsTr("Ajouter")
                            Keys.onReturnPressed: Bridge.navigate("workspace:apps")
                            Keys.onSpacePressed: Bridge.navigate("workspace:apps")
                            StateLayer { id: al; function onClicked(): void { Bridge.navigate("workspace:apps") } }
                            Column {
                                anchors.centerIn: parent
                                spacing: Appearance.spacing.small
                                Item {
                                    anchors.horizontalCenter: parent.horizontalCenter
                                    width: 48
                                    height: 48
                                    Blob { anchors.fill: parent; lobes: 8; amp: 0.07; color: Colours.palette.m3primaryContainer
                                        rotation: addTile.hot ? 45 : 0
                                        Behavior on rotation { Spring {} }
                                    }
                                    MaterialIcon { anchors.centerIn: parent; text: "add"; color: Colours.palette.m3onPrimaryContainer; font.pointSize: Appearance.font.size.large }
                                }
                                StyledText { anchors.horizontalCenter: parent.horizontalCenter; text: qsTr("Ajouter"); font.pointSize: Appearance.font.size.small; color: Colours.palette.m3onSurfaceVariant }
                            }
                        }
                        Item { Layout.fillWidth: true; Layout.preferredWidth: 0.01 }
                    }
                }
            }

            StyledRect {
                id: recentCard
                Layout.fillWidth: true
                Layout.fillHeight: true
                Layout.preferredWidth: 3
                implicitHeight: 188
                radius: Appearance.rounding.large
                color: Colours.palette.m3surfaceContainer
                opacity: re.alpha
                transform: Translate { y: re.dy }
                Entrance { id: re; order: 6 }
                ColumnLayout {
                    anchors.fill: parent
                    anchors.margins: Appearance.padding.large
                    spacing: Appearance.spacing.small
                    RowLayout {
                        Layout.fillWidth: true
                        MaterialIcon { text: "history"; color: Colours.palette.m3primary; font.pointSize: Appearance.font.size.large }
                        StyledText { text: qsTr("Récents"); font.pointSize: Appearance.font.size.normal; font.weight: 500; Layout.fillWidth: true }
                        Pill { order: 7; implicitHeight: 28; icon: "arrow_forward"; text: qsTr("Voir"); onClicked: Bridge.navigate("workspace:recent") }
                    }
                    Repeater {
                        model: root.recentCount
                        StyledRect {
                            id: rec
                            required property int index
                            readonly property var item: (Bridge.recent ?? [])[index] ?? ({})
                            readonly property var board: root.boards.find(b => b.id === item.board)
                            readonly property bool hot: rl.containsMouse || activeFocus
                            Layout.fillWidth: true
                            implicitHeight: 34
                            radius: Appearance.rounding.normal
                            color: "transparent"
                            opacity: rce.alpha
                            transform: Translate { y: rce.dy }
                            Entrance { id: rce; order: 7 + rec.index }
                            activeFocusOnTab: true
                            Keys.onReturnPressed: Bridge.action("launch", {id: item.id})
                            StateLayer { id: rl; function onClicked(): void { Bridge.action("launch", {id: rec.item.id}) } }
                            RowLayout {
                                anchors.fill: parent
                                anchors.leftMargin: Appearance.padding.small
                                anchors.rightMargin: Appearance.padding.small
                                spacing: Appearance.spacing.small
                                transform: Translate { x: rec.hot ? 4 : 0; Behavior on x { Spring {} } }
                                AppIcon { Layout.preferredWidth: 28; Layout.preferredHeight: 28; size: 28; appId: rec.item.id ?? ""; appName: rec.item.name ?? "" }
                                ColumnLayout {
                                    Layout.fillWidth: true
                                    spacing: 0
                                    StyledText { Layout.fillWidth: true; text: rec.item.name ?? ""; elide: Text.ElideRight; font.pointSize: Appearance.font.size.normal }
                                    RowLayout {
                                        spacing: 4
                                        visible: !!rec.board
                                        MaterialIcon { text: rec.board?.icon ?? ""; color: root.onTone(rec.board?.id ?? ""); font.pointSize: Appearance.font.size.small }
                                        StyledText { text: rec.board?.name ?? ""; elide: Text.ElideRight; font.pointSize: Appearance.font.size.small; color: Colours.palette.m3outline }
                                    }
                                }
                                MaterialIcon {
                                    text: "north_east"
                                    color: Colours.palette.m3primary
                                    font.pointSize: Appearance.font.size.normal
                                    opacity: rec.hot ? 1 : 0
                                    Behavior on opacity { Anim { duration: Appearance.anim.durations.small } }
                                }
                            }
                            Component.onCompleted: if (item.id) Bridge.action("icon", {id: item.id})
                            onItemChanged: if (item.id) Bridge.action("icon", {id: item.id})
                        }
                    }
                    RowLayout {
                        visible: root.recentCount === 0
                        Layout.fillWidth: true
                        Layout.fillHeight: true
                        spacing: Appearance.spacing.normal
                        Item {
                            implicitWidth: 48
                            implicitHeight: 48
                            Blob { anchors.fill: parent; lobes: 6; amp: 0.1; color: Colours.palette.m3surfaceContainerHigh }
                            MaterialIcon { anchors.centerIn: parent; text: "history"; color: Colours.palette.m3outline }
                        }
                        StyledText { Layout.fillWidth: true; text: qsTr("Tes derniers accès apparaîtront ici"); wrapMode: Text.WordWrap; font.pointSize: Appearance.font.size.small; color: Colours.palette.m3onSurfaceVariant }
                    }
                    Item { Layout.fillHeight: true; visible: root.recentCount > 0 }
                }
            }
        }

        // Quick actions
        StyledRect {
            Layout.fillWidth: true
            implicitHeight: 56
            radius: Appearance.rounding.large
            color: Colours.palette.m3surfaceContainer
            opacity: qe.alpha
            transform: Translate { y: qe.dy }
            Entrance { id: qe; order: 8 }
            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: Appearance.padding.normal
                anchors.rightMargin: Appearance.padding.normal
                spacing: Appearance.spacing.small
                Pill { order: 9; icon: "content_paste"; text: qsTr("Presse-papiers"); onClicked: Bridge.navigate("clipboard") }
                Pill { order: 10; icon: "search"; text: qsTr("Commande rapide"); onClicked: Bridge.navigate("palette") }
                Pill { order: 11; icon: "add_link"; text: qsTr("Ajouter un site"); onClicked: Bridge.action("websites", {}) }
                Item { Layout.fillWidth: true }
                Pill { order: 12; icon: "tune"; text: qsTr("Personnaliser"); onClicked: Bridge.navigate("settings") }
            }
        }
    }
}
