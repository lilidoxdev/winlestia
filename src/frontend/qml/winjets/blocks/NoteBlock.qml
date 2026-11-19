import QtQuick
import QtQuick.Controls as C
import qs.components
import qs.config
import qs.services
import ".."

// Bloc `note` : texte multiligne éditable, persisté dans state[key] (4000 caractères au plus). Écriture différée par un
// minuteur à un coup qui ne démarre qu'après une frappe : aucun minuteur au repos. Couleurs : rôles de palette seulement.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property int maxChars: 4000
    readonly property string key: spec && typeof spec.key === "string" ? spec.key : ""
    readonly property string stored: {
        const v = ctx && ctx.state && key !== "" ? ctx.state[key] : "";
        return typeof v === "string" ? v : "";
    }
    readonly property string hint: spec ? Bridge.winjets.renderText(spec.placeholder ?? "", ctx) : ""

    implicitWidth: 200 * sc
    implicitHeight: 96 * sc
    clip: true

    function flush(): void {
        saveTimer.stop();
        if (host && key !== "" && !host.editing)
            host.setState(key, area.text.substring(0, maxChars));
    }

    // Le texte stocké (re)charge l'éditeur sauf pendant la frappe : l'écho du backend ne doit pas déplacer le curseur.
    onStoredChanged: if (!area.activeFocus && !saveTimer.running && area.text !== stored)
        area.text = stored
    Component.onCompleted: area.text = stored
    Component.onDestruction: {
        if (saveTimer.running)
            flush();
        // Libère la fenêtre mise en cache même si block.Window.window est déjà nul.
        if (typing && cachedWindow) {
            const layer = Bridge.winjets.desktopLayer;
            if (layer && layer.acceptFocus)
                layer.acceptFocus(cachedWindow, false);
            typing = false;
        }
        cachedWindow = null;
    }

    // La fenêtre de la carte ne prend jamais le focus (WS_EX_NOACTIVATE) : on l'autorise le temps de la saisie seulement,
    // puis la couche bureau la rend de nouveau non activable et la repose au fond.
    property bool typing: false
    property var cachedWindow: null

    function setTyping(on: bool): void {
        if (on === typing)
            return;
        const layer = Bridge.winjets.desktopLayer;
        const win = on ? block.Window.window : cachedWindow;
        if (!layer || !layer.acceptFocus || !win)
            return;
        typing = on;
        cachedWindow = on ? win : null;
        layer.acceptFocus(win, on);
        if (on)
            Qt.callLater(() => area.forceActiveFocus());
    }

    Connections {
        target: block.Window.window
        ignoreUnknownSignals: true

        function onActiveChanged(): void {
            if (block.Window.window && !block.Window.window.active && block.typing)
                block.setTyping(false);
        }
    }

    Timer {
        id: saveTimer

        interval: 600
        repeat: false
        running: false
        onTriggered: block.flush()
    }

    // Filet de base très discret (onSurface 0,08) sous chaque ligne, au pas de l'interligne généreux.
    FontMetrics {
        id: metrics

        font: area.font
    }
    readonly property real lineStep: Math.max(8, metrics.height)
    Repeater {
        model: Math.ceil(block.height / block.lineStep)

        Rectangle {
            required property int index

            x: 4 * block.sc
            width: block.width - 8 * block.sc
            height: 1
            y: area.topPadding + (index + 1) * block.lineStep - 3 * block.sc
            color: Qt.alpha(WinjetTheme.textPrimary, 0.08)
        }
    }

    C.ScrollView {
        anchors.fill: parent
        enabled: !(block.host && block.host.editing)

        C.TextArea {
            id: area

            wrapMode: TextEdit.Wrap
            selectByMouse: true
            persistentSelection: false
            color: WinjetTheme.textPrimary
            placeholderText: block.hint
            placeholderTextColor: WinjetTheme.textSecondary
            selectionColor: WinjetTheme.accent
            selectedTextColor: Colours.palette.m3onPrimary
            font.family: Appearance.font.family.sans
            font.pointSize: (Math.round(15 * block.sc)) * 0.75
            padding: 4 * block.sc
            background: null
            // La limite s'applique à la saisie comme au collage.
            onTextChanged: {
                if (length > block.maxChars)
                    remove(block.maxChars, length);
                if (text !== block.stored)
                    saveTimer.restart();
            }
            Keys.onEscapePressed: block.setTyping(false)
            onActiveFocusChanged: {
                if (!activeFocus) {
                    if (saveTimer.running)
                        block.flush();
                    block.setTyping(false);
                }
            }

            // Clic dans le champ (hors édition de la grille) : la fenêtre devient activable avant de prendre le focus.
            TapHandler {
                acceptedButtons: Qt.LeftButton
                onPressedChanged: if (pressed && !(block.host && block.host.editing))
                    block.setTyping(true)
            }
        }
    }
}
