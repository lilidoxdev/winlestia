// Hôte Windows natif des vues Caelestia. Un seul état de navigation gère fermeture et focus.
import QtQuick
import QtQuick.Window
import QtQuick.Shapes
import QtQml
import Quickshell
import qs.config
import qs.services
import qs.components
import qs.modules.dashboard as Dashboard
import "winjets"
import "lock"
import "fx"

Window {
    id: window
    title: "Panneaux · Caelestia"
    color: "transparent"
    flags: (Bridge.preview ? Qt.Window : Qt.Tool) | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint
    property bool peek: Bridge.preview
    property string pageMode: "dashboard"
    property bool extraOpen: false
    // Panneau réellement ouvert (tableau de bord ou onglet), distinct de l'aperçu au survol qui montre aussi la fenêtre.
    readonly property bool panelOpen: pageMode === "dashboard" ? vis.dashboard : extraOpen
    // Page affichée dans le Centre de contrôle : "main", "wifi", "bluetooth" ou "sound".
    property string controlsPage: "main"
    property bool wifiSampling: extraOpen && pageMode === "controls" && controlsPage === "wifi"
    onWifiSamplingChanged: subscribe()
    property int menuDuration: Math.round(
        Appearance.anim.durations.expressiveFastSpatial
        * (["audio", "microphone", "mixer", "controls"].includes(pageMode) ? .6 : 1))
    property bool audioSampling: extraOpen && (pageMode === "audio" || pageMode === "microphone" || pageMode === "mixer" ||
        (pageMode === "controls" && controlsPage === "sound"))
    onAudioSamplingChanged: subscribe()
    property bool controlsOpen: pageMode === "controls" && extraOpen
    // Hauteur cible de la palette figée le temps de son ouverture (0 = aucune ouverture en cours) ; levée à la fin de l'animation.
    property real paletteOpenTarget: 0
    property real extraHeight: {
        if (!extraOpen)
            return 0;
        const paletteBody = paletteOpenTarget > 0 && pageMode === "palette";
        return (paletteBody ? paletteOpenTarget : extraBody.implicitHeight) + 36;
    }
    property real contentWidth: pageMode === "workspace" ? Math.min(1220, Bridge.screenWidth - 80) :
        pageMode === "palette" ?
            (palette.item && palette.item.wide ? Math.min(1320, Bridge.screenWidth - 80) : 660) :
        pageMode === "settings" ? Math.min(900, Bridge.screenWidth - 80) :
        pageMode === "clipboard" ? 730 :
        pageMode === "tools" ? 760 :
        pageMode === "audio" || pageMode === "microphone" ? 650 :
        pageMode === "mixer" ? 490 :
        pageMode === "capture" ? 620 :
        450

    // Réserve du rail social. Le rail est sa propre fenêtre collée au bord droit du même écran (Bridge.screenX/Width).
    // Seul le recouvrement réel entre le tableau de bord centré et le rail est réservé : rien ne bouge tant qu'ils ne se touchent pas ;
    // le panneau glisse alors vers la gauche (jusqu'à la marge d'écran) et seulement si cela ne suffit pas, les volets rétrécissent
    // (drawer.maxWidth, échelle minimale dans Content). Sur une très petite largeur logique, le reste recouvre le tableau de bord.
    readonly property bool railBound: pageMode === "dashboard"
    readonly property real railLeft: Bridge.screenWidth - socialRail.reservedWidth
    readonly property real dashboardOverlap: railBound ? Math.max(0, (Bridge.screenWidth + panelWidth) / 2 - railLeft) : 0
    readonly property real dashboardShift: Math.min(dashboardOverlap, Math.max(0, (Bridge.screenWidth - panelWidth) / 2 - 8))
    // L'ouverture du rail anime la réserve : panelWidth doit alors suivre le contenu mis à l'échelle image par image.
    readonly property bool railMoving: socialRail.reservedWidth > 0 && socialRail.reveal < 1
    property real panelWidth: Math.ceil((pageMode === "dashboard" ? Math.max(600, drawer.implicitWidth || 854) : contentWidth) + 40)
    property real panelHeight: Bridge.fileDragNearTop ? 0 :
        Math.ceil(Math.max(1, pageMode === "dashboard" ?
            (vis.dashboard ? drawer.implicitHeight : peek ? 82 : 0) : extraHeight))
    // Vrai tant qu'une des deux animations (largeur, hauteur) tourne ; le passage à faux est regroupé (relance du Behavior).
    property bool widthAnimRunning: false
    property bool heightAnimRunning: false
    readonly property bool animRunning: widthAnimRunning || heightAnimRunning
    property bool animating: false
    readonly property bool profiling: Bridge.panelProfiling
    property bool profileFrame: false
    function pmark(label) {
        if (profiling)
            Bridge.panelMark(label);
    }
    function panelName() {
        if (pageMode === "dashboard")
            return ["dashboard", "media", "performance", "access"][drawer.dashState.currentTab] ?? "dashboard";
        return pageMode;
    }
    function setAnimating(value) {
        if (animating === value)
            return;
        animating = value;
        pmark(value ? "anim-start" : "anim-end");
        Bridge.setPanelAnimating(value, panelName());
    }
    onAnimRunningChanged: {
        if (animRunning)
            setAnimating(true);
        else
            Qt.callLater(() => {
                if (!window.animRunning)
                    window.setAnimating(false);
            });
    }
    // La largeur de fenêtre reste stable (large) sauf si le panneau est décalé par le rail : elle épouse alors le panneau pour rester à
    // l'écran.
    // Filets : si le Behavior ne signale jamais sa fin, ou si la fenêtre est masquée, l'état retombe à faux ; un nouveau changement de
    // running réévalue normalement. Arrêté au repos.
    Timer {
        interval: Math.max(250 * (Bridge.settings.motionScale ?? 1), window.heightDuration * (Bridge.settings.motionScale ?? 1)) + 500
        running: window.animating
        repeat: false
        onTriggered: window.setAnimating(false)
    }
    width: dashboardShift > 0 ? Math.ceil(panelWidth) + 16 : Math.max(Math.min(1600, Bridge.screenWidth - 16), Math.ceil(panelWidth))
    height: Math.max(Math.min(900, Bridge.screenHeight - Config.border.thickness), Math.ceil(panelHeight))
    Behavior on panelWidth {
        enabled: !Appearance.reducedEffects && !window.railMoving
        Anim {
            onRunningChanged: window.widthAnimRunning = running
            duration: Math.round(250 * (Bridge.settings.motionScale ?? 1))
            easing.bezierCurve: Appearance.anim.curves.emphasizedDecel
        }
    }
    Behavior on panelHeight {
        enabled: !Appearance.reducedEffects
        Anim {
            duration: Math.round(window.heightDuration * (Bridge.settings.motionScale ?? 1))
            easing.bezierCurve: window.heightCurve
            onRunningChanged: {
                window.heightAnimRunning = running;
                if (running) {
                    if (window.tracing)
                        Bridge.trace("anim-start");
                    return;
                }
                window.paletteOpenTarget = 0;
                if (window.tracing)
                    Bridge.trace("anim-done");
            }
        }
    }
    // Filet : si aucune animation ne tourne à l'ouverture (hauteur déjà à la cible), la hauteur figée est levée ; arrêté au repos.
    Timer {
        interval: window.heightDuration + window.paletteOpenGuardMs
        running: window.paletteOpenTarget > 0
        repeat: false
        onTriggered: window.paletteOpenTarget = 0
    }
    readonly property int paletteOpenGuardMs: 400
    // Palette : ouverture courte à décélération franche (le contenu répond dès la première image), fermeture plus brève
    // encore ; les autres panneaux gardent la durée d'origine.
    readonly property int panelHeightDuration: 260
    // Préchargement du navigateur d'espaces, de la palette et de l'onde de choc dès la fin du démarrage : la première ouverture ne paie
    // plus la création.
    readonly property int warmDelay: 300
    readonly property int heightDuration: {
        if (pageMode !== "palette")
            return panelHeightDuration;
        return extraOpen ? Appearance.anim.durations.small : Appearance.anim.durations.expressiveFastEffects;
    }
    readonly property var heightCurve: {
        if (pageMode !== "palette")
            return Appearance.anim.curves.emphasizedDecel;
        return extraOpen ? Appearance.anim.curves.standardDecel : Appearance.anim.curves.emphasizedAccel;
    }
    // Trace d'ouverture opt-in (WINLESTIA_TRACE_OPEN=1, voir open_trace.py) : aucune connexion active sinon.
    readonly property bool tracing: Bridge.traceEnabled()
    property bool traceFrame: false
    Connections {
        target: window
        enabled: window.tracing && window.traceFrame
        function onFrameSwapped() {
            window.traceFrame = false;
            Bridge.trace("first-frame");
        }
    }
    Connections {
        target: window
        enabled: window.profiling && window.profileFrame
        function onFrameSwapped() {
            window.profileFrame = false;
            window.pmark("first-frame");
        }
    }
    onPanelWidthChanged: Bridge.updateSurfaces()
    onPanelHeightChanged: Bridge.updateSurfaces()
    property int dashboardTab: drawer.dashState.currentTab
    // Centré sur l'écran, décalé vers la gauche du recouvrement réel avec le rail social.
    x: Bridge.screenX + (Bridge.screenWidth - width) / 2 - dashboardShift
    y: Bridge.screenY + Math.max(0, Config.border.thickness)
    visible: panelHeight > 1 && !Bridge.screenFullscreen
    property int servicePage: pageMode === "dashboard" ? drawer.dashState.currentTab :
        pageMode === "controls" ? 4 :
        pageMode === "clipboard" ? 6 :
        pageMode === "audio" || pageMode === "microphone" || pageMode === "mixer" ? 7 : 5
    function subscribe() {
        Bridge.subscribe(pageMode === "dashboard" ? vis.dashboard : extraOpen, servicePage);
    }
    // Centre de la palette une fois ouverte, en pixels logiques depuis le coin haut-gauche de l'écran du shell : la fenêtre est centrée
    // sur l'écran (largeur stable, ou celle de la palette si elle est plus large), le panneau en occupe le haut sur extraHeight.
    readonly property int paletteWidth: 660
    readonly property real paletteMargin: 80
    readonly property real paletteBodyPadding: 36
    // Hauteur estimée de la palette tant que son chargement asynchrone n'a pas fourni l'élément (titre + liste + pied).
    readonly property real paletteHeightEstimate: 520
    function lockWindowsExist() {
        if (Bridge.lock.locked ?? false)
            return true;
        for (let i = 0; i < lockRepeater.count; i++) {
            const entry = lockRepeater.itemAt(i);
            if (entry && (entry.lingering || entry.item))
                return true;
        }
        return false;
    }
    function fireShockwave() {
        if (lockWindowsExist()) {
            if (tracing)
                Bridge.trace("shock skipped reason=locked");
            return;
        }
        const wide = palette.item && palette.item.wide;
        const panelW = Math.ceil((wide ? Math.min(1320, Bridge.screenWidth - paletteMargin) : paletteWidth) + 40);
        const paletteH = palette.item ? palette.item.implicitHeight : paletteHeightEstimate;
        const panelH = Math.ceil(paletteH + paletteBodyPadding);
        const winW = Math.max(Math.min(1600, Bridge.screenWidth - 16), panelW);
        const cx = (Bridge.screenWidth - winW) / 2 + winW / 2;
        const cy = Math.max(0, Config.border.thickness) + panelH / 2;
        shockwave.fire(window.screen, cx, cy);
    }
    Shockwave {
        id: shockwave
        shell: window
        enabled: Bridge.settings.shockwave ?? true
    }
    function openMode(mode) {
        // Un second événement palette sur la palette déjà ouverte la ferme (même chemin que Échap).
        if (mode === "palette" && extraOpen && pageMode === "palette" && visible) {
            if (tracing) {
                traceFrame = false;
                Bridge.trace("close");
            }
            Bridge.closePanel();
            return;
        }
        if (tracing) {
            traceFrame = mode !== "close";
            Bridge.trace("openMode " + mode);
        }
        if (profiling) {
            profileFrame = mode !== "close";
            pmark("open-request " + mode);
        }
        if (mode === "close") {
            vis.dashboard = false;
            peek = false;
            extraOpen = false;
            controlsPage = "main";
            subscribe();
            return;
        }
        if (mode === "toggle") {
            if (visible && pageMode === "dashboard" && vis.dashboard) {
                openMode("close");
                return;
            }
            mode = "dashboard";
        }
        if (mode === "dashboard" || mode === "media" || mode === "performance" || mode === "access") {
            extraOpen = false;
            pageMode = "dashboard";
            // Les modes explicites choisissent leur onglet ; une ouverture simple ou une bascule garde le dernier onglet de la session.
            if (mode === "media")
                drawer.dashState.currentTab = 1;
            else if (mode === "performance")
                drawer.dashState.currentTab = 2;
            else if (mode === "access")
                drawer.dashState.currentTab = 3;
            vis.dashboard = true;
            peek = true;
        } else {
            controlsPage = "main";
            vis.dashboard = false;
            peek = false;
            // Onde de choc : capture AVANT que le panneau n'apparaisse (jamais pendant la fermeture), puis la palette s'ouvre.
            if (mode.split(":")[0] === "palette" && !(extraOpen && pageMode === "palette") && !visible)
                fireShockwave();
            // Hauteur cible figée pendant l'ouverture de la palette : l'animation ne repart pas quand le chargement ou les résultats
            // changent la hauteur.
            if (mode.split(":")[0] === "palette" && !(extraOpen && pageMode === "palette"))
                paletteOpenTarget = palette.item ? palette.item.implicitHeight : paletteHeightEstimate;
            pageMode = mode.split(":")[0];
            extraOpen = true;
            if (pageMode === "palette")
                Qt.callLater(() => {
                    if (palette.item)
                        palette.item.reset();
                });
            if (pageMode === "settings" && mode === "settings:social") {
                if (preferences.item)
                    preferences.item.openSection("social");
                else
                    preferences.pendingSection = "social";
            }
            if (pageMode === "settings" && mode === "settings:bookmarks") {
                if (preferences.item)
                    preferences.item.openSection("bookmarks");
                else
                    preferences.pendingSection = "bookmarks";
            }
            if (pageMode === "settings" && mode === "settings:winjets") {
                if (preferences.item)
                    preferences.item.openSection("winjets");
                else
                    preferences.pendingSection = "winjets";
            }
        }
        subscribe();
        Bridge.activate();
    }
    onVisibleChanged: {
        if (tracing) {
            traceFrame = true;
            Bridge.trace(visible ? "visible" : "hidden");
        }
        if (!visible) {
            sessionDialog.open = false;
            setAnimating(false);
        }
        subscribe();
    }
    onExtraOpenChanged: if (!extraOpen) {
        paletteOpenTarget = 0;
        sessionDialog.open = false;
        if (shockwave.active)
            shockwave.stop();
    }
    onPageModeChanged: sessionDialog.open = false
    onControlsOpenChanged: subscribe()
    Shortcut {
        sequence: "Escape"
        enabled: window.visible && !sessionDialog.open && !(browser.item && browser.item.dialogOpen)
        onActivated: {
            if (window.pageMode === "controls" && window.controlsPage !== "main")
                window.controlsPage = "main";
            else
                Bridge.closePanel();
        }
    }
    Shortcut {
        sequence: "Ctrl+1"
        enabled: window.visible && window.pageMode !== "settings"
        onActivated: Bridge.navigate("workspace:apps")
    }
    Shortcut {
        sequence: "Ctrl+2"
        enabled: window.visible && window.pageMode !== "settings"
        onActivated: Bridge.navigate("workspace:games")
    }
    Shortcut {
        sequence: "Ctrl+3"
        enabled: window.visible && window.pageMode !== "settings"
        onActivated: Bridge.navigate("workspace:projects")
    }
    Shortcut {
        sequence: "Ctrl+4"
        enabled: window.visible && window.pageMode !== "settings"
        onActivated: Bridge.navigate("workspace:documents")
    }
    Shortcut {
        sequence: "Ctrl+H"
        enabled: window.visible && window.pageMode !== "settings"
        onActivated: Bridge.navigate("dashboard")
    }

    PersistentProperties {
        id: vis
        property bool dashboard: false
        property bool launcher: false
    }
    Binding {
        target: Config.appearance.anim.durations
        property: "scale"
        value: Bridge.settings.motionScale ?? 1
    }
    // Tous les fonds sont dessinés une seule fois par la surface d'écran partagée ; ce fond ne comble que le manque de lisibilité (voir
    // Colours.panelFloor) sous les onglets et le contenu du panneau, dans la silhouette de panel_path (corps entre 20 px et width - 20).
    Rectangle {
        anchors.horizontalCenter: parent.horizontalCenter
        y: 0
        width: Math.max(0, window.panelWidth - 40)
        height: window.panelHeight
        bottomLeftRadius: Math.min(25, height / 2)
        bottomRightRadius: Math.min(25, height / 2)
        color: Colours.panelBacking
        visible: height > 1 && color.a > 0
    }
    Dashboard.Wrapper {
        id: drawer
        maxWidth: window.railBound ? Math.max(drawer.minWidth, railLeft - 16 - 40) : 0
        y: Math.min(0, window.panelHeight - implicitHeight)
        anchors.horizontalCenter: parent.horizontalCenter
        visibilities: vis
        visible: window.pageMode === "dashboard" && (vis.dashboard || (!window.peek && window.panelHeight > 1))
    }
    Dashboard.Tabs {
        anchors.top: parent.top
        anchors.topMargin: 15
        anchors.horizontalCenter: parent.horizontalCenter
        width: window.panelWidth - 70
        nonAnimWidth: width
        state: drawer.dashState
        visible: window.pageMode === "dashboard" && window.peek && !vis.dashboard
    }
    Item {
        id: extraBody
        anchors.top: parent.top
        anchors.topMargin: 18
        anchors.horizontalCenter: parent.horizontalCenter
        width: window.panelWidth - 80
        implicitHeight: Math.max(sessionDialog.open ? 260 : 0,
            window.pageMode === "workspace" ? browser.implicitHeight :
            window.pageMode === "palette" ? palette.implicitHeight :
            window.pageMode === "settings" ? preferences.implicitHeight :
            window.pageMode === "clipboard" ? clipboard.implicitHeight :
            window.pageMode === "tools" ? tools.implicitHeight :
            window.pageMode === "audio" || window.pageMode === "microphone" ? audio.implicitHeight :
            window.pageMode === "mixer" ? fullMixer.implicitHeight :
            window.pageMode === "capture" ? capturePane.implicitHeight :
            controls.implicitHeight)
        visible: window.pageMode !== "dashboard"
        clip: true
        opacity: window.extraOpen ? 1 : 0
        Behavior on opacity {
            enabled: !Appearance.reducedEffects
            Anim {
                duration: Appearance.anim.durations.small
            }
        }
        WindowsControls {
            id: controls
            width: 410
            visible: window.pageMode === "controls"
            page: window.controlsPage
            onRequestPage: name => window.controlsPage = name
        }
        Timer {
            id: warmTimer
            interval: window.warmDelay
            running: true
            repeat: false
            onTriggered: {
                browser.warm = true;
                palette.warm = true;
                shockwave.warm();
            }
        }
        PaneLoader {
            id: tools
            want: window.pageMode === "tools"
            sourceComponent: ToolsPane {}
        }
        PaneLoader {
            id: audio
            want: window.pageMode === "audio" || window.pageMode === "microphone"
            sourceComponent: AudioPane {
                microphone: window.pageMode === "microphone"
            }
        }
        PaneLoader {
            id: fullMixer
            want: window.pageMode === "mixer"
            sourceComponent: MixerPane {
                standalone: true
                onDismiss: Bridge.closePanel()
            }
        }
        PaneLoader {
            id: capturePane
            want: window.pageMode === "capture"
            sourceComponent: CapturePane {}
        }
        PaneLoader {
            id: browser
            want: window.pageMode === "workspace"
            asynchronous: true
            sourceComponent: WorkspaceBrowser {}
        }
        PaneLoader {
            id: palette
            want: window.pageMode === "palette"
            asynchronous: true
            sourceComponent: PalettePane {}
            onLoaded: {
                if (window.tracing)
                    Bridge.trace("palette-loaded");
                if (want)
                    item.reset();
                else
                    // Préchargement : mêmes résultats vides que l'ouverture (sans le focus), la hauteur est définitive avant la première
                    // ouverture.
                    Bridge.searchPalette("");
            }
        }
        PaneLoader {
            id: preferences
            want: window.pageMode === "settings"
            property string pendingSection: ""
            sourceComponent: ShellPreferences {}
            onLoaded: if (pendingSection !== "") {
                item.openSection(pendingSection);
                pendingSection = "";
            }
        }
        PaneLoader {
            id: clipboard
            want: window.pageMode === "clipboard"
            sourceComponent: ClipboardPane {}
        }
        ConfirmDialog {
            id: sessionDialog
            cardWidth: 340
            destructive: true
            property string choice: ""
            function label(id) {
                return id === "lock" ? qsTr("Verrouiller") :
                    id === "logout" ? qsTr("Se déconnecter") :
                    id === "sleep" ? qsTr("Mettre en veille") :
                    id === "hibernate" ? qsTr("Hibernation") :
                    id === "restart" ? qsTr("Redémarrer") :
                    id === "shutdown" ? qsTr("Éteindre") : "";
            }
            title: qsTr("%1 ?").arg(label(choice))
            message: choice === "lock" ? qsTr("Ta session sera verrouillée.") : qsTr("Les modifications non enregistrées seront perdues.")
            confirmText: label(choice)
            onConfirmed: {
                const id = choice;
                Bridge.closePanel();
                Bridge.action("systemAction", {
                    id: id,
                    confirmed: true
                });
            }
        }
        Connections {
            target: Bridge
            function onSessionRequested(id) {
                sessionDialog.choice = id;
                sessionDialog.open = true;
            }
        }
    }
    // Panneaux paresseux conservés : instanciés au premier affichage, puis gardés chargés.
    // Espace et Palette sont aussi préchargés en asynchrone après l'inactivité.
    component PaneLoader: Loader {
        property bool want: false
        property bool once: false
        property bool warm: false
        width: parent.width
        visible: want && item !== null
        active: want || once || warm
        onWantChanged: if (want)
            once = true
        Component.onCompleted: if (want)
            once = true
    }
    Connections {
        target: drawer.dashState
        function onCurrentTabChanged() {
            window.subscribe();
        }
    }
    Connections {
        target: Bridge
        function onNavigationRequested(mode) {
            window.openMode(mode);
        }
        function onTabRequested(index) {
            window.pageMode = "dashboard";
            window.extraOpen = false;
            drawer.dashState.currentTab = index;
            vis.dashboard = true;
            window.peek = true;
            window.subscribe();
            Bridge.activate();
        }
        function onShellEvent(event) {
            if (event === "peek" && !vis.dashboard && !window.extraOpen) {
                window.pageMode = "dashboard";
                window.peek = true;
            }
        }
        function onNoticeChanged() {
            noticeTimer.restart();
        }
    }
    Timer {
        id: noticeTimer
        interval: 5000
    }
    StyledRect {
        anchors.bottom: parent.bottom
        anchors.bottomMargin: window.height - window.panelHeight + 14
        anchors.horizontalCenter: parent.horizontalCenter
        width: Math.min(window.panelWidth - 80, 640)
        height: noticeLabel.implicitHeight + 22
        radius: Appearance.rounding.normal
        color: Colours.palette.m3surfaceContainerHighest
        border.width: 1
        border.color: Colours.palette.m3outlineVariant
        opacity: noticeTimer.running ? 1 : 0
        visible: opacity > .01
        Behavior on opacity {
            enabled: !Appearance.reducedEffects
            Anim {
                duration: Appearance.anim.durations.small
            }
        }
        StyledText {
            id: noticeLabel
            anchors.centerIn: parent
            width: parent.width - 26
            text: qsTr(Bridge.notice)
            wrapMode: Text.Wrap
            font.pointSize: Appearance.font.size.small
        }
    }
    Loader {
        id: recycleLoader
        property bool once: false
        active: Bridge.fileDragNearTop || once
        onLoaded: once = true
        sourceComponent: RecycleDrop {}
    }
    Repeater {
        model: Bridge.frameScreens
        delegate: Item {
            id: frameHolder
            required property var modelData
            ScreenFrame {
                modelData: frameHolder.modelData
            }
            Instantiator {
                model: 1
                active: Bridge.settings.desktopLogo ?? false
                delegate: DesktopLogo {
                    modelData: frameHolder.modelData
                }
            }
        }
    }
    // Écran de verrouillage : n'existe que verrouillé (et le temps de l'animation de sortie).
    Repeater {
        id: lockRepeater
        model: Bridge.frameScreens
        delegate: Loader {
            id: lockLoader
            required property var modelData
            required property int index
            property bool lingering: false
            active: (Bridge.lock.locked ?? false) || lingering
            onLoaded: lingering = true
            sourceComponent: LockScreen {
                modelData: lockLoader.modelData
                primary: lockLoader.index === 0
                screenIndex: lockLoader.index
                onClosed: lockLoader.lingering = false
            }
        }
    }
    CaptureWindow {}
    DictationOverlay {
        dock: bottomDock
    }
    Loader {
        id: wallpaperSwitcher
        active: false
        sourceComponent: WallpaperSwitcher {
            onCloseRequested: wallpaperSwitcher.active = false
        }
    }
    Loader {
        id: windowOverview
        active: false
        sourceComponent: WindowOverview {
            onCloseRequested: windowOverview.active = false
        }
    }
    Connections {
        target: Bridge
        function onShellEvent(event) {
            if (event === "wallpaper-switcher")
                wallpaperSwitcher.active = !wallpaperSwitcher.active;
            else if (event === "overview")
                windowOverview.active = !windowOverview.active;
        }
    }
    // Un seul retour visuel : la Pilule quand elle est réglée et pas en silence, sinon l'indicateur du bord droit.
    readonly property bool pillShown: (Bridge.settings.pill ?? true) && !Bridge.quiet
    Loader {
        id: osdLoader
        active: (Bridge.settings.osd ?? true) && !window.pillShown
        sourceComponent: Osd {}
    }
    Loader {
        id: livePillLoader
        objectName: "livePillLoader"
        active: window.pillShown
        sourceComponent: LivePill {}
    }
    // Cartes Winjets du bureau (fenêtres natives sous les autres fenêtres) ; rien tant qu'il n'y a pas d'instance.
    WinjetLayer {
        id: winjetLayer
    }
    Dock {
        id: bottomDock
        objectName: "bottomDock"
    }
    SocialRail {
        id: socialRail
    }
    BookmarksRail {}
}
