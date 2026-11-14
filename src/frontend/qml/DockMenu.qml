// Menus du dock : fenêtre indépendante et bornée, jamais une surface d'entrée plein écran qui capterait les clics.
import QtQuick
import QtQuick.Window
import QtQuick.Controls as C
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config

Window {
    id: menu
    title: "Panneaux · Menu du dock"
    transientParent: null
    flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint
    color: "transparent"
    width: kind === "desktop" ? 300 : kind === "running" ? 440 : 390
    height: Math.min(maxWindowHeight, body.implicitHeight + 28)
    readonly property int maxWindowHeight: Math.min(620, Bridge.screenHeight - 120)
    readonly property color panelColor: Qt.alpha(Colours.surfaceBase, .96)
    property string kind: "app"
    property var entry: ({})
    property bool fromRunning: false
    property bool backgroundTab: false
    property string powerChoice: ""
    property bool confirmStop: false
    property real anchorX: 0
    property real anchorY: 0
    function openAt(type, data, px, py) {
        if (type === "running")
            Bridge.action("runningRefresh", {});
        fromRunning = type === "running";
        kind = type;
        powerChoice = "";
        backgroundTab = false;
        entry = data ?? ({});
        confirmStop = false;
        anchorX = px;
        anchorY = py;
        position();
        clearRows();
        visible = true;
        syncRows(true);
        settleTimer.restart();
        requestActivate();
    }
    function position() {
        x = Math.max(Bridge.screenX + 8, Math.min(anchorX - width / 2, Bridge.screenX + Bridge.screenWidth - width - 8));
        y = Math.max(Bridge.screenY + 8, anchorY - height - 8);
    }
    onHeightChanged: if (visible)
        position()
    onWidthChanged: if (visible)
        position()
    // Se ferme quand une autre fenêtre prend le focus (événementiel) ; le sondage du pointeur
    // ci-dessous ne couvre que les clics sans changement de focus (ex. le dock lui-même).
    property bool wasActive: false
    onActiveChanged: {
        if (active)
            wasActive = true;
        else if (visible && wasActive) {
            wasActive = false;
            visible = false;
        }
    }
    onVisibleChanged: if (!visible) {
        wasActive = false;
        powerChoice = "";
        disarm();
        rowsHeld = false;
        settleTimer.stop();
        clearRows();
    }
    onKindChanged: {
        disarm();
        if (kind === "running")
            syncRows(true);
    }
    onBackgroundTabChanged: {
        disarm();
        clearRows();
        syncRows(true);
        settleTimer.restart();
    }
    // Vide la liste sans transition (les glissements ne servent qu'aux changements d'un relevé à l'autre).
    function clearRows() {
        rowsAnimated = false;
        rowsModel.clear();
    }
    function setSort(key) {
        if (sortKey === key)
            return;
        sortKey = key;
        disarm();
        syncRows(true);
    }
    // Liste de tâches de la vue « running » : lignes mises à jour en place (jamais recréées) pour garder survol, bouton armé et icônes.
    readonly property var liveApps: Bridge.runningApps
    onLiveAppsChanged: syncRows(false)
    // Tailles structurelles de la vue « running » (sans jeton Appearance équivalent).
    readonly property int taskRowHeight: 54
    readonly property int taskIconSize: 30
    readonly property int taskCpuWidth: 76
    readonly property int taskMemWidth: 64
    readonly property int taskFiguresWidth: taskCpuWidth + Appearance.spacing.small + taskMemWidth
    readonly property int taskStopSize: 32
    readonly property int taskListSpacing: 4
    readonly property int taskHeaderHeight: 26
    readonly property int taskFadeHeight: 24
    readonly property int taskEmptyHeight: 120
    property string armedId: ""
    property bool rowsHeld: false
    property bool rowsAnimated: false
    property string sortKey: "mem"
    property real maxMem: 0
    property real maxCpu: 0
    property real sumCpu: 0
    property real sumMem: 0
    property int runningCount: 0
    property int backgroundCount: 0
    ListModel {
        id: rowsModel
    }
    Timer {
        id: refreshTimer
        interval: 2000
        repeat: true
        running: menu.visible && menu.kind === "running"
        onTriggered: Bridge.action("runningRefresh", {})
    }
    // Les transitions ne s'activent qu'une fois la liste initiale posée : rien ne glisse à l'ouverture ni au changement d'onglet.
    Timer {
        id: settleTimer
        interval: 150
        repeat: false
        onTriggered: menu.rowsAnimated = true
    }
    Timer {
        id: armTimer
        interval: 3000
        repeat: false
        onTriggered: menu.armedId = ""
    }
    function arm(id) {
        armedId = id;
        armTimer.restart();
    }
    function disarm() {
        armedId = "";
        armTimer.stop();
    }
    function stopRow(id, isBackground) {
        if (armedId !== id) {
            arm(id);
            return;
        }
        disarm();
        const app = (Bridge.runningApps ?? []).find(a => a.id === id);
        Bridge.action("dockApp", {
            id: id,
            hwnd: app?.hwnd ?? 0,
            action: isBackground ? "stop" : "closeAll",
            confirmed: true
        });
    }
    function fmtCpu(value) {
        return value < 0.1 ? "—" : (value >= 10 ? Math.round(value) : value.toFixed(1).replace(".", ",")) + " %";
    }
    function fmtMem(mb) {
        return mb <= 0 ? "—" : mb >= 1024 ? (mb / 1024).toFixed(1).replace(".", ",") + " Go" : Math.round(mb) + " Mo";
    }
    function rowSub(app) {
        if (app.background)
            return (app.procs ?? 0) > 1 ? qsTr("%1 processus").arg(app.procs) : "";
        const windows = app.windows ?? [];
        return (app.count ?? 0) > 1 ? qsTr("%1 fenêtres").arg(app.count) : windows.length === 1 ? windows[0].title ?? "" : "";
    }
    function rowData(app) {
        return {
            rid: app.id,
            name: app.name ?? "",
            sub: rowSub(app),
            canStop: !!app.canStop,
            isBackground: !!app.background,
            cpu: app.cpu ?? 0,
            mem: app.mem ?? 0
        };
    }
    // Ordre par usage, sur des valeurs arrondies (CPU à l'entier, mémoire à 10 Mo) pour que le bruit d'un échantillon ne réordonne pas.
    function rowOrder(a, b) {
        const cpu = Math.round(b.cpu ?? 0) - Math.round(a.cpu ?? 0);
        const mem = Math.round((b.mem ?? 0) / 10) - Math.round((a.mem ?? 0) / 10);
        return (sortKey === "cpu" ? cpu || mem : mem || cpu) || (a.name ?? "").localeCompare(b.name ?? "");
    }
    function syncRows(force) {
        if (!visible || kind !== "running")
            return;
        const all = liveApps ?? [];
        runningCount = all.filter(a => !a.background).length;
        backgroundCount = all.length - runningCount;
        const fresh = all.filter(a => !!a.background === backgroundTab);
        const byId = {};
        for (const app of fresh)
            byId[app.id] = app;
        for (let i = rowsModel.count - 1; i >= 0; i--)
            if (!byId[rowsModel.get(i).rid])
                rowsModel.remove(i);
        if (armedId !== "" && !byId[armedId])
            disarm();
        const known = {};
        for (let i = 0; i < rowsModel.count; i++) {
            const current = rowsModel.get(i);
            known[current.rid] = true;
            const next = rowData(byId[current.rid]);
            for (const key in next)
                if (current[key] !== next[key])
                    rowsModel.setProperty(i, key, next[key]);
        }
        const wanted = fresh.slice().sort(rowOrder);
        if (!force && (rowsHeld || armedId !== "")) {
            for (const app of wanted)
                if (!known[app.id])
                    rowsModel.append(rowData(app));
        } else {
            for (let i = 0; i < wanted.length; i++) {
                let at = -1;
                for (let j = i; j < rowsModel.count; j++)
                    if (rowsModel.get(j).rid === wanted[i].id) {
                        at = j;
                        break;
                    }
                if (at < 0)
                    rowsModel.insert(i, rowData(wanted[i]));
                else if (at !== i)
                    rowsModel.move(at, i, 1);
            }
        }
        let cpu = 0, mem = 0, mostMem = 0, mostCpu = 0;
        for (const app of fresh) {
            cpu += app.cpu ?? 0;
            mem += app.mem ?? 0;
            mostMem = Math.max(mostMem, app.mem ?? 0);
            mostCpu = Math.max(mostCpu, app.cpu ?? 0);
        }
        sumCpu = Math.min(100, cpu);
        sumMem = mem;
        maxMem = mostMem;
        maxCpu = mostCpu;
    }
    function powerLabel(id) {
        return id === "logout" ? qsTr("Se déconnecter") : id === "sleep" ? qsTr("Mettre en veille") : id === "hibernate"
            ? qsTr("Hibernation") : id === "restart" ? qsTr("Redémarrer") : id === "shutdown" ? qsTr("Éteindre") : "";
    }
    function closeDesktop() {
        const index = (entry.index ?? 0) + 1;
        visible = false;
        Bridge.action("desktopClose", {
            index: index
        });
    }
    onConfirmStopChanged: confirmDialog.open = confirmStop
    Timer {
        id: focusCheck
        objectName: "outsideClickTimer"
        interval: 100
        repeat: true
        running: menu.visible
        onTriggered: if (Bridge.menuOutsideClick(menu.x, menu.y, menu.width, menu.height))
            menu.visible = false
    }
    Shortcut {
        sequence: "Escape"
        enabled: menu.visible && !confirmDialog.open
        onActivated: menu.visible = false
    }
    function selectEntry(data) {
        entry = data;
        confirmStop = false;
        kind = "app";
    }
    function appAction(action, hwnd) {
        if (action === "stop" && !confirmStop) {
            confirmStop = true;
            return;
        }
        const selected = entry;
        if (fromRunning) {
            kind = "running";
            confirmStop = false;
        } else
            visible = false;
        Bridge.action("dockApp", {
            id: selected.id,
            hwnd: hwnd ?? selected.hwnd ?? 0,
            action: action,
            confirmed: action === "stop"
        });
    }
    readonly property var systemItems: [
        {
            id: "apps",
            text: qsTr("Applications installées"),
            icon: "apps"
        },
        {
            id: "power",
            text: qsTr("Alimentation et veille"),
            icon: "power_settings_new"
        },
        {
            id: "events",
            text: qsTr("Observateur d’événements"),
            icon: "event_note"
        },
        {
            id: "system",
            text: qsTr("Informations système"),
            icon: "info"
        },
        {
            id: "devices",
            text: qsTr("Gestionnaire de périphériques"),
            icon: "devices"
        },
        {
            id: "network",
            text: qsTr("Connexions réseau"),
            icon: "lan"
        },
        {
            id: "disks",
            text: qsTr("Gestion des disques"),
            icon: "storage"
        },
        {
            id: "computer",
            text: qsTr("Gestion de l’ordinateur"),
            icon: "desktop_windows"
        },
        {
            id: "terminal",
            text: qsTr("Terminal Winlestia"),
            icon: "terminal"
        },
        {
            id: "terminalAdmin",
            text: qsTr("Terminal Winlestia — administrateur"),
            icon: "admin_panel_settings"
        },
        {
            id: "taskmgr",
            text: qsTr("Gestionnaire des tâches"),
            icon: "monitoring"
        },
        {
            id: "settings",
            text: qsTr("Paramètres Windows"),
            icon: "settings"
        },
        {
            id: "control",
            text: qsTr("Panneau de configuration"),
            icon: "tune"
        },
        {
            id: "explorer",
            text: qsTr("Explorateur de fichiers"),
            icon: "folder_open"
        },
        {
            id: "winjets",
            text: qsTr("Ajouter un Winjet"),
            icon: "widgets"
        },
        {
            id: "run",
            text: qsTr("Exécuter / rechercher"),
            icon: "search"
        },
        {
            id: "desktop",
            text: qsTr("Afficher le bureau"),
            icon: "desktop_windows"
        }
    ]
    readonly property var appItems: [
        {
            id: "open",
            text: qsTr("Ouvrir une nouvelle fenêtre"),
            icon: "open_in_new"
        },
        {
            id: "admin",
            text: qsTr("Exécuter en administrateur"),
            icon: "admin_panel_settings"
        },
        {
            id: "pin",
            text: entry.pinned ? qsTr("Détacher de la barre des tâches") : qsTr("Épingler à la barre des tâches"),
            icon: "keep"
        },
        {
            id: "favorite",
            text: entry.favorite ? qsTr("Retirer des favoris") : qsTr("Ajouter aux favoris et au dock"),
            icon: "star"
        },
        {
            id: "reveal",
            text: qsTr("Afficher dans l’Explorateur"),
            icon: "folder_open"
        },
        {
            id: "minimize",
            text: qsTr("Réduire la fenêtre"),
            icon: "minimize",
            window: true
        },
        {
            id: "maximize",
            text: qsTr("Agrandir la fenêtre"),
            icon: "fullscreen",
            window: true
        },
        {
            id: "close",
            text: qsTr("Fermer cette fenêtre"),
            icon: "close",
            window: true
        },
        {
            id: "closeAll",
            text: qsTr("Fermer toutes les fenêtres (") + (entry.count ?? 0) + ")",
            icon: "close_fullscreen",
            window: true
        },
        {
            id: "stop",
            text: qsTr("Arrêter l’application…"),
            icon: "stop_circle",
            stop: true
        }
    ].filter(item => (!item.window || (entry.count ?? 0) > 0) && (!item.stop || entry.canStop))
    Rectangle {
        anchors.fill: parent
        radius: Appearance.rounding.large
        color: menu.panelColor
        border.width: 1
        border.color: Colours.palette.m3outlineVariant
        C.ScrollView {
            anchors.fill: parent
            anchors.margins: 14
            clip: true
            contentWidth: availableWidth
            C.ScrollBar.vertical.policy: menu.kind === "running" ? C.ScrollBar.AlwaysOff : C.ScrollBar.AsNeeded
            Column {
                id: body
                width: menu.width - 28
                spacing: 3
                StyledText {
                    width: parent.width - 12
                    x: 6
                    text: menu.kind === "desktop" ? (menu.entry.name || qsTr("Bureau %1").arg((menu.entry.index ?? 0) + 1))
                        : menu.kind === "system" ? "Windows · Winlestia" : menu.kind === "power" ? qsTr("Alimentation")
                        : menu.kind === "running" ? qsTr("Applications") : menu.entry.name ?? qsTr("Application")
                    font.pointSize: Appearance.font.size.normal
                    elide: Text.ElideRight
                    topPadding: Appearance.padding.small
                    bottomPadding: Appearance.padding.normal
                }
                IconTextButton {
                    visible: menu.fromRunning && menu.kind === "app"
                    text: qsTr("Retour")
                    icon: "arrow_back"
                    type: IconTextButton.Text
                    onClicked: {
                        menu.kind = "running";
                        menu.confirmStop = false;
                    }
                }
                StyledText {
                    visible: menu.kind === "app"
                    width: parent.width - 12
                    x: 6
                    text: (menu.entry.background ? qsTr("En arrière-plan") : qsTr("%1 fenêtre(s)").arg(menu.entry.count
                        ?? 0)) + "\n" + (menu.entry.path ?? "")
                    font.pointSize: Appearance.font.size.small
                    wrapMode: Text.WrapAnywhere
                    color: Colours.palette.m3onSurfaceVariant
                    bottomPadding: Appearance.padding.smaller
                    maximumLineCount: 3
                    elide: Text.ElideRight
                }
                StyledText {
                    objectName: "runningSummary"
                    visible: menu.kind === "running"
                    width: parent.width - Appearance.padding.smaller * 2
                    x: Appearance.padding.smaller
                    text: (rowsModel.count === 1 ? qsTr("%1 application") : qsTr("%1 applications")).arg(rowsModel.count) + " · " + qsTr("processeur") + " " + Math.round(menu.sumCpu) + " % · "
                        + qsTr("mémoire") + " " + (menu.sumMem / 1024).toFixed(1).replace(".", ",") + " Go"
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3onSurfaceVariant
                    elide: Text.ElideRight
                    bottomPadding: Appearance.padding.smaller
                }
                Row {
                    visible: menu.kind === "running"
                    spacing: Appearance.padding.small
                    IconTextButton {
                        text: qsTr("En cours") + " " + menu.runningCount
                        checked: !menu.backgroundTab
                        onClicked: menu.backgroundTab = false
                    }
                    IconTextButton {
                        text: qsTr("Arrière-plan") + " " + menu.backgroundCount
                        checked: menu.backgroundTab
                        onClicked: menu.backgroundTab = true
                    }
                }
                StyledText {
                    visible: menu.kind === "power" && menu.powerChoice !== ""
                    width: parent.width - 12
                    x: 6
                    text: qsTr("%1 ?").arg(menu.powerLabel(menu.powerChoice))
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3onSurfaceVariant
                    bottomPadding: Appearance.padding.smaller
                }
                Repeater {
                    model: menu.kind === "power" ? (menu.powerChoice === "" ? [
                            {
                                id: "lock",
                                text: qsTr("Verrouiller"),
                                icon: "lock"
                            },
                            {
                                id: "logout",
                                text: qsTr("Se déconnecter"),
                                icon: "logout"
                            },
                            {
                                id: "sleep",
                                text: qsTr("Mettre en veille"),
                                icon: "bedtime"
                            },
                            {
                                id: "hibernate",
                                text: qsTr("Hibernation"),
                                icon: "downloading"
                            },
                            {
                                id: "restart",
                                text: qsTr("Redémarrer"),
                                icon: "restart_alt"
                            },
                            {
                                id: "shutdown",
                                text: qsTr("Éteindre"),
                                icon: "power_settings_new"
                            },
                            {
                                id: "quitWinlestia",
                                text: qsTr("Fermer Winlestia"),
                                icon: "exit_to_app"
                            }
                        ].filter(item => item.id !== "hibernate" || Bridge.hibernateAvailable) : [
                            {
                                id: "confirm",
                                text: qsTr("Confirmer"),
                                icon: "check"
                            },
                            {
                                id: "cancel",
                                text: qsTr("Annuler"),
                                icon: "close"
                            }
                        ]) : []
                    StyledRect {
                        required property var modelData
                        objectName: "power-" + modelData.id
                        width: body.width
                        height: 54
                        radius: Appearance.rounding.normal
                        color: Colours.palette.m3surfaceContainer
                        StateLayer {
                            function onClicked() {
                                if (parent.modelData.id === "quitWinlestia") {
                                    Bridge.action("quit", {});
                                    menu.visible = false;
                                } else if (["lock"].indexOf(parent.modelData.id) >= 0) {
                                    Bridge.action("systemAction", {
                                        id: parent.modelData.id
                                    });
                                    menu.visible = false;
                                } else if (parent.modelData.id === "cancel")
                                    menu.powerChoice = "";
                                else if (parent.modelData.id === "confirm") {
                                    Bridge.action("systemAction", {
                                        id: menu.powerChoice,
                                        confirmed: true
                                    });
                                    menu.visible = false;
                                } else
                                    menu.powerChoice = parent.modelData.id;
                            }
                        }
                        RowLayout {
                            anchors.fill: parent
                            anchors.margins: 14
                            spacing: Appearance.spacing.larger
                            MaterialIcon {
                                text: parent.parent.modelData.icon
                                font.pointSize: Appearance.font.size.title
                                color: Colours.palette.m3primary
                            }
                            StyledText {
                                Layout.fillWidth: true
                                text: parent.parent.modelData.text
                                font.pointSize: Appearance.font.size.normal
                            }
                        }
                    }
                }
                StyledRect {
                    visible: menu.kind === "desktop"
                    objectName: "desktopCloseRow"
                    width: body.width
                    height: visible ? 40 : 0
                    radius: Appearance.rounding.small
                    color: "transparent"
                    StateLayer {
                        disabled: !menu.entry.canClose
                        function onClicked() {
                            menu.closeDesktop();
                        }
                    }
                    RowLayout {
                        anchors.fill: parent
                        anchors.leftMargin: Appearance.spacing.normal
                        anchors.rightMargin: Appearance.spacing.smaller
                        spacing: Appearance.spacing.normal
                        opacity: menu.entry.canClose ? 1 : .4
                        MaterialIcon {
                            text: "close"
                            font.pointSize: Appearance.font.size.large
                            color: Colours.palette.m3error
                        }
                        StyledText {
                            Layout.fillWidth: true
                            text: qsTr("Fermer le bureau")
                            font.pointSize: Appearance.font.size.normal
                            elide: Text.ElideRight
                        }
                    }
                }
                StyledText {
                    visible: menu.kind === "desktop"
                    width: parent.width - 12
                    x: 6
                    text: qsTr("Les fenêtres passent sur le bureau voisin")
                    font.pointSize: Appearance.font.size.small
                    wrapMode: Text.WordWrap
                    color: Colours.palette.m3onSurfaceVariant
                    topPadding: Appearance.padding.small
                    bottomPadding: Appearance.padding.small
                }
                Repeater {
                    model: menu.kind === "system" ? menu.systemItems : menu.kind === "app" ? menu.appItems : []
                    delegate: StyledRect {
                        id: row
                        required property var modelData
                        width: body.width
                        height: 38
                        radius: Appearance.rounding.small
                        color: "transparent"
                        StateLayer {
                            function onClicked() {
                                if (menu.kind === "system") {
                                    menu.visible = false;
                                    if (row.modelData.id === "run")
                                        Bridge.navigate("palette");
                                    else if (row.modelData.id === "winjets")
                                        Bridge.action("settings", {
                                            section: "winjets"
                                        });
                                    else
                                        Bridge.action("systemAction", {
                                            id: row.modelData.id
                                        });
                                } else
                                    menu.appAction(row.modelData.id);
                            }
                        }
                        RowLayout {
                            anchors.fill: parent
                            anchors.leftMargin: Appearance.spacing.smaller
                            anchors.rightMargin: Appearance.padding.smaller
                            spacing: Appearance.spacing.smaller
                            MaterialIcon {
                                text: row.modelData.icon
                                font.pointSize: Appearance.font.size.large
                                color: row.modelData.id === "stop" ? Colours.palette.m3error : Colours.palette.m3primary
                            }
                            StyledText {
                                Layout.fillWidth: true
                                text: row.modelData.text
                                font.pointSize: Appearance.font.size.small
                                elide: Text.ElideRight
                            }
                        }
                    }
                }
                Repeater {
                    model: menu.kind === "app" ? (menu.entry.windows ?? []) : []
                    delegate: StyledRect {
                        id: windowRow
                        required property var modelData
                        width: body.width
                        height: 36
                        radius: Appearance.rounding.small
                        color: "transparent"
                        StateLayer {
                            function onClicked() {
                                menu.appAction("activate", windowRow.modelData.hwnd);
                            }
                        }
                        StyledText {
                            anchors.fill: parent
                            anchors.leftMargin: Appearance.spacing.smaller
                            verticalAlignment: Text.AlignVCenter
                            text: windowRow.modelData.title
                            font.pointSize: Appearance.font.size.small
                            elide: Text.ElideRight
                            color: Colours.palette.m3onSurfaceVariant
                        }
                    }
                }
                Item {
                    objectName: "runningEmpty"
                    visible: menu.kind === "running" && rowsModel.count === 0
                    width: body.width
                    height: visible ? menu.taskEmptyHeight : 0
                    Column {
                        anchors.centerIn: parent
                        spacing: Appearance.spacing.small
                        MaterialIcon {
                            anchors.horizontalCenter: parent.horizontalCenter
                            text: "apps"
                            font.pointSize: Appearance.font.size.extraLarge
                            color: Colours.palette.m3onSurfaceVariant
                        }
                        StyledText {
                            anchors.horizontalCenter: parent.horizontalCenter
                            text: menu.backgroundTab ? qsTr("Aucune application en arrière-plan") : qsTr("Aucune application ouverte")
                            font.pointSize: Appearance.font.size.small
                            color: Colours.palette.m3onSurfaceVariant
                        }
                    }
                }
                Item {
                    objectName: "runningHeader"
                    visible: menu.kind === "running" && rowsModel.count > 0
                    width: body.width
                    height: visible ? menu.taskHeaderHeight : 0
                    StyledText {
                        anchors.left: parent.left
                        anchors.leftMargin: Appearance.padding.large
                        anchors.verticalCenter: parent.verticalCenter
                        text: qsTr("Application")
                        font.pointSize: Appearance.font.size.small
                        color: Colours.palette.m3onSurfaceVariant
                    }
                    Repeater {
                        model: [
                            {
                                key: "cpu",
                                text: qsTr("Processeur"),
                                inset: menu.taskMemWidth + Appearance.spacing.small
                            },
                            {
                                key: "mem",
                                text: qsTr("Mémoire"),
                                inset: 0
                            }
                        ]
                        delegate: MouseArea {
                            id: sortHead
                            required property var modelData
                            readonly property bool active: menu.sortKey === modelData.key
                            objectName: "sort-" + modelData.key
                            anchors.right: parent.right
                            anchors.rightMargin: Appearance.padding.large + modelData.inset
                            anchors.verticalCenter: parent.verticalCenter
                            width: headRow.width
                            height: parent.height
                            cursorShape: Qt.PointingHandCursor
                            onClicked: menu.setSort(modelData.key)
                            Row {
                                id: headRow
                                anchors.right: parent.right
                                anchors.verticalCenter: parent.verticalCenter
                                MaterialIcon {
                                    visible: sortHead.active
                                    anchors.verticalCenter: parent.verticalCenter
                                    text: "arrow_drop_down"
                                    font.pointSize: Appearance.font.size.larger
                                    color: Colours.palette.m3primary
                                }
                                StyledText {
                                    anchors.verticalCenter: parent.verticalCenter
                                    text: sortHead.modelData.text
                                    font.pointSize: Appearance.font.size.small
                                    color: sortHead.active ? Colours.palette.m3primary : Colours.palette.m3onSurfaceVariant
                                }
                            }
                        }
                    }
                }
                Item {
                    id: runningBox
                    objectName: "runningList"
                    visible: menu.kind === "running"
                    width: body.width
                    height: visible ? Math.min(runningList.contentHeight, Math.max(menu.taskRowHeight, menu.maxWindowHeight - 28 - y)) : 0
                    HoverHandler {
                        onHoveredChanged: menu.rowsHeld = hovered
                    }
                    ListView {
                        id: runningList
                        anchors.fill: parent
                        clip: true
                        spacing: menu.taskListSpacing
                        model: rowsModel
                        interactive: contentHeight > height
                        boundsBehavior: Flickable.StopAtBounds
                        add: Transition {
                            enabled: menu.rowsAnimated && !Appearance.reducedEffects
                            ParallelAnimation {
                                Anim {
                                    property: "opacity"
                                    from: 0
                                    to: 1
                                    duration: Appearance.anim.durations.small
                                }
                                Anim {
                                    property: "scale"
                                    from: 0.96
                                    to: 1
                                    duration: Appearance.anim.durations.small
                                }
                            }
                        }
                        remove: Transition {
                            enabled: menu.rowsAnimated && !Appearance.reducedEffects
                            Anim {
                                property: "opacity"
                                to: 0
                                duration: Appearance.anim.durations.small
                            }
                        }
                        move: Transition {
                            enabled: menu.rowsAnimated && !Appearance.reducedEffects
                            ParallelAnimation {
                                Anim {
                                    property: "y"
                                    duration: Appearance.anim.durations.normal
                                }
                                Anim {
                                    properties: "opacity,scale"
                                    to: 1
                                    duration: Appearance.anim.durations.small
                                }
                            }
                        }
                        displaced: Transition {
                            enabled: menu.rowsAnimated && !Appearance.reducedEffects
                            ParallelAnimation {
                                Anim {
                                    property: "y"
                                    duration: Appearance.anim.durations.normal
                                }
                                Anim {
                                    properties: "opacity,scale"
                                    to: 1
                                    duration: Appearance.anim.durations.small
                                }
                            }
                        }
                        delegate: StyledRect {
                            id: taskRow
                            required property string rid
                            required property string name
                            required property string sub
                            required property bool canStop
                            required property bool isBackground
                            required property real cpu
                            required property real mem
                            readonly property bool armed: menu.armedId === rid
                            readonly property bool stoppable: !isBackground || canStop
                            readonly property bool showStop: stoppable && (rowHover.hovered || stopButton.activeFocus || armed)
                            readonly property real share: {
                                const most = menu.sortKey === "cpu" ? menu.maxCpu : menu.maxMem;
                                return most > 0 ? Math.min(1, (menu.sortKey === "cpu" ? cpu : mem) / most) : 0;
                            }
                            objectName: "running-row-" + rid
                            width: runningList.width
                            height: menu.taskRowHeight
                            radius: Appearance.rounding.normal
                            color: Colours.palette.m3surfaceContainer
                            HoverHandler {
                                id: rowHover
                            }
                            // Jauge : part de la clé de tri active par rapport à la plus grande ligne de l'onglet.
                            Rectangle {
                                objectName: "meter"
                                anchors.left: parent.left
                                anchors.top: parent.top
                                anchors.bottom: parent.bottom
                                radius: parent.radius
                                width: parent.width * taskRow.share
                                color: Qt.alpha(Colours.palette.m3primary, 0.14)
                                Behavior on width {
                                    enabled: !Appearance.reducedEffects
                                    Anim {
                                        duration: Appearance.anim.durations.normal
                                    }
                                }
                            }
                            StateLayer {
                                disabled: taskRow.isBackground
                                function onClicked() {
                                    const app = (Bridge.runningApps ?? []).find(a => a.id === parent.rid);
                                    if (app)
                                        menu.selectEntry(app);
                                }
                            }
                            RowLayout {
                                anchors.fill: parent
                                anchors.leftMargin: Appearance.padding.large
                                anchors.rightMargin: Appearance.padding.large
                                anchors.topMargin: Appearance.padding.small
                                anchors.bottomMargin: Appearance.padding.small
                                spacing: Appearance.spacing.normal
                                Item {
                                    Layout.preferredWidth: menu.taskIconSize
                                    Layout.preferredHeight: menu.taskIconSize
                                    Layout.alignment: Qt.AlignVCenter
                                    Image {
                                        id: appLogo
                                        anchors.fill: parent
                                        sourceSize.width: Math.ceil(menu.taskIconSize * Screen.devicePixelRatio)
                                        sourceSize.height: Math.ceil(menu.taskIconSize * Screen.devicePixelRatio)
                                        source: Bridge.iconUrl(taskRow.rid, Bridge.iconRevision)
                                        asynchronous: true
                                        fillMode: Image.PreserveAspectFit
                                    }
                                    MaterialIcon {
                                        anchors.centerIn: parent
                                        text: "apps"
                                        visible: appLogo.status !== Image.Ready
                                        color: Colours.palette.m3primary
                                    }
                                }
                                Column {
                                    Layout.fillWidth: true
                                    Layout.alignment: Qt.AlignVCenter
                                    StyledText {
                                        width: parent.width
                                        text: taskRow.name
                                        font.pointSize: Appearance.font.size.normal
                                        elide: Text.ElideRight
                                    }
                                    StyledText {
                                        visible: text !== ""
                                        width: parent.width
                                        text: taskRow.sub
                                        font.pointSize: Appearance.font.size.small
                                        color: Colours.palette.m3onSurfaceVariant
                                        elide: Text.ElideRight
                                    }
                                }
                                Item {
                                    Layout.preferredWidth: menu.taskFiguresWidth
                                    Layout.fillHeight: true
                                    Row {
                                        anchors.right: parent.right
                                        anchors.verticalCenter: parent.verticalCenter
                                        spacing: Appearance.spacing.small
                                        opacity: taskRow.showStop ? 0 : 1
                                        StyledText {
                                            width: menu.taskCpuWidth
                                            horizontalAlignment: Text.AlignRight
                                            text: menu.fmtCpu(taskRow.cpu)
                                            font.pointSize: Appearance.font.size.small
                                            font.family: Appearance.font.family.mono
                                            color: taskRow.cpu >= 10 ? Colours.palette.m3primary : taskRow.cpu < 0.1
                                                ? Colours.palette.m3onSurfaceVariant : Colours.palette.m3onSurface
                                        }
                                        StyledText {
                                            width: menu.taskMemWidth
                                            horizontalAlignment: Text.AlignRight
                                            text: menu.fmtMem(taskRow.mem)
                                            font.pointSize: Appearance.font.size.small
                                            font.family: Appearance.font.family.mono
                                            color: taskRow.mem <= 0 ? Colours.palette.m3onSurfaceVariant : Colours.palette.m3onSurface
                                        }
                                    }
                                    StyledRect {
                                        id: stopButton
                                        objectName: "stop-" + taskRow.rid
                                        visible: taskRow.stoppable
                                        opacity: taskRow.showStop ? 1 : 0
                                        anchors.right: parent.right
                                        anchors.verticalCenter: parent.verticalCenter
                                        height: menu.taskStopSize
                                        width: taskRow.armed ? armedLabel.implicitWidth + Appearance.padding.larger * 2 : menu.taskStopSize
                                        radius: Appearance.rounding.full
                                        color: taskRow.armed ? Colours.palette.m3error : "transparent"
                                        border.width: activeFocus ? 1 : 0
                                        border.color: Colours.palette.m3primary
                                        activeFocusOnTab: true
                                        Keys.onReturnPressed: menu.stopRow(taskRow.rid, taskRow.isBackground)
                                        Keys.onSpacePressed: menu.stopRow(taskRow.rid, taskRow.isBackground)
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
                                        StateLayer {
                                            disabled: !taskRow.showStop
                                            radius: parent.radius
                                            color: taskRow.armed ? Colours.palette.m3onError : Colours.palette.m3onSurface
                                            function onClicked() {
                                                menu.stopRow(taskRow.rid, taskRow.isBackground);
                                            }
                                        }
                                        MaterialIcon {
                                            anchors.centerIn: parent
                                            visible: !taskRow.armed
                                            text: "close"
                                            font.pointSize: Appearance.font.size.large
                                            color: Colours.palette.m3onSurfaceVariant
                                        }
                                        StyledText {
                                            id: armedLabel
                                            anchors.centerIn: parent
                                            visible: taskRow.armed
                                            text: qsTr("Arrêter ?")
                                            font.pointSize: Appearance.font.size.small
                                            color: Colours.palette.m3onError
                                        }
                                    }
                                }
                            }
                        }
                    }
                    // Fondu doux aux bords : seulement s'il reste du contenu dans cette direction (aucune barre de défilement).
                    Rectangle {
                        anchors.left: parent.left
                        anchors.right: parent.right
                        anchors.top: parent.top
                        height: menu.taskFadeHeight
                        opacity: runningList.atYBeginning ? 0 : 1
                        visible: opacity > 0
                        gradient: Gradient {
                            GradientStop {
                                position: 0
                                color: menu.panelColor
                            }
                            GradientStop {
                                position: 1
                                color: Qt.alpha(menu.panelColor, 0)
                            }
                        }
                        Behavior on opacity {
                            Anim {
                                duration: Appearance.anim.durations.small
                            }
                        }
                    }
                    Rectangle {
                        anchors.left: parent.left
                        anchors.right: parent.right
                        anchors.bottom: parent.bottom
                        height: menu.taskFadeHeight
                        opacity: runningList.atYEnd ? 0 : 1
                        visible: opacity > 0
                        gradient: Gradient {
                            GradientStop {
                                position: 0
                                color: Qt.alpha(menu.panelColor, 0)
                            }
                            GradientStop {
                                position: 1
                                color: menu.panelColor
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
        }
    }
    ConfirmDialog {
        id: confirmDialog
        cardWidth: 340
        destructive: true
        title: qsTr("Arrêter cette application ?")
        message: qsTr("« ") + (menu.entry.name ?? "") + qsTr(" » sera arrêté. Les modifications non enregistrées seront perdues.")
        confirmText: qsTr("Arrêter l’application")
        onConfirmed: menu.appAction("stop")
        onClosed: menu.confirmStop = false
    }
}
