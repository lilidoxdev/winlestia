// Paroles du morceau en cours (Bridge.lyrics). Deux présentations :
//  - complète : liste centrée sur la ligne en cours, voisines estompées par la distance, défilement doux, fondu haut et bas ;
//  - compact : une seule ligne qui glisse vers le haut à chaque changement (repli sur fallbackText sans ligne synchronisée).
// La position n'est lue que lorsque la vue est visible, active, que la musique joue et que les paroles sont synchronisées :
// un seul minuteur modeste, et les liaisons ne bougent que si l'indice de la ligne change.
pragma ComponentBehavior: Bound

import QtQuick
import QtQuick.Effects
import qs.components
import qs.services
import qs.config
import "LyricsMath.js" as LyricsMath

Item {
    id: root

    property bool compact: false
    // Posé à faux par le parent quand la surface qui héberge la vue n'est pas à l'écran.
    property bool active: true
    property string fallbackText: ""
    // Part de la hauteur où la ligne en cours est ancrée (0.5 : centre ; 0.33 : tiers haut).
    property real anchorFraction: 0.5
    property real fontSize: compact ? Appearance.font.size.normal : Appearance.font.size.large
    property int lineGap: Appearance.spacing.smaller

    readonly property int sampleMs: 150
    readonly property real fadeAmount: 0.18
    readonly property real neighbourOpacity: 0.55
    readonly property real falloffStep: 0.17
    readonly property real minOpacity: 0.1
    readonly property real hoverOpacity: 0.85
    readonly property real dimScale: 0.94
    readonly property real iconScale: 1.6
    readonly property real interludeLow: 0.25
    readonly property int interludeMs: 900

    readonly property var player: Players.active
    readonly property var lyr: Bridge.lyrics ?? null
    readonly property string phase: lyr?.state ?? "idle"
    readonly property var lines: phase === "synced" ? (lyr?.lines ?? []) : []
    readonly property bool playing: player?.isPlaying ?? false
    // Référence prise dès que la vue est à l'écran, quel que soit l'état : c'est elle qui fait sortir le pont de « idle ».
    readonly property bool refWanted: active && visible && (player?.available ?? false)
    readonly property bool wanted: active && visible && phase !== "off" && phase !== "idle"
    readonly property bool running: wanted && playing && phase === "synced" && lines.length > 0
    readonly property bool reduced: Appearance.reducedEffects
    // Ligne en cours (-1 avant la première) ; seule valeur recalculée par le minuteur.
    property int current: -1
    readonly property string currentText: current >= 0 && current < lines.length ? String(lines[current].text ?? "") : ""
    readonly property string compactText: phase === "synced" && currentText !== "" ? currentText : fallbackText
    readonly property bool messageShown: wanted && (phase === "loading" || phase === "none" || phase === "instrumental")

    implicitHeight: compact ? compactBox.implicitHeight : 0

    function sample(): void {
        if (!wanted || phase !== "synced")
            return;
        const i = LyricsMath.activeIndex(lines, player?.position ?? 0);
        if (i !== current)
            current = i;
    }
    function seekTo(time: real): void {
        if (player?.canSeek) {
            player.position = time;
            sample();
        }
    }
    function messageIcon(): string {
        return phase === "instrumental" ? "music_note" : "lyrics";
    }
    function messageText(): string {
        if (phase === "loading")
            return qsTr("Recherche des paroles…");
        if (phase === "instrumental")
            return qsTr("Morceau instrumental");
        return qsTr("Paroles introuvables");
    }

    onLinesChanged: sample()
    onRunningChanged: sample()
    onRefWantedChanged: syncRef()
    onWantedChanged: sample()
    onCompactTextChanged: compactBox.push(compactText)
    Component.onCompleted: {
        syncRef();
        sample();
        compactBox.push(compactText);
    }
    Component.onDestruction: {
        if (held)
            Bridge.lyricsRef?.(false);
    }

    // Une seule référence sur le compteur partagé du pont tant que la vue est visible.
    property bool held: false
    function syncRef(): void {
        if (refWanted === held)
            return;
        held = refWanted;
        Bridge.lyricsRef?.(refWanted);
    }

    Timer {
        running: root.running
        interval: root.sampleMs
        repeat: true
        onTriggered: root.sample()
    }
    // Saut de position (recherche, nouvel instantané) pendant une pause : l'indice suit sans minuteur.
    Connections {
        target: root.player
        enabled: root.wanted && root.phase === "synced"
        ignoreUnknownSignals: true
        function onPositionUpdated(): void {
            root.sample();
        }
    }

    // --- Présentation compacte : une ligne qui glisse ---------------------------------------------------------------------
    Item {
        id: compactBox
        visible: root.compact
        anchors.left: parent.left
        anchors.right: parent.right
        implicitHeight: labelA.implicitHeight
        height: implicitHeight
        clip: true

        property bool flip: false
        property real slide: 1
        readonly property real travel: height

        function push(t: string): void {
            if (!root.compact)
                return;
            if (root.reduced || !root.visible || !root.wanted) {
                labelA.text = t;
                labelB.text = t;
                slide = 1;
                return;
            }
            flip = !flip;
            (flip ? labelA : labelB).text = t;
            slide = 0;
            slideAnim.restart();
        }

        Anim {
            id: slideAnim
            target: compactBox
            property: "slide"
            from: 0
            to: 1
            duration: Appearance.anim.durations.expressiveDefaultSpatial
            easing.bezierCurve: Appearance.anim.curves.emphasizedDecel
        }

        StyledText {
            id: labelA
            width: parent.width
            elide: Text.ElideRight
            color: Colours.palette.m3onSurface
            font.pointSize: root.fontSize
            font.weight: Font.DemiBold
            y: compactBox.flip ? (1 - compactBox.slide) * compactBox.travel : -compactBox.slide * compactBox.travel
            opacity: compactBox.flip ? compactBox.slide : 1 - compactBox.slide
        }
        StyledText {
            id: labelB
            width: parent.width
            elide: Text.ElideRight
            color: Colours.palette.m3onSurface
            font.pointSize: root.fontSize
            font.weight: Font.DemiBold
            y: compactBox.flip ? -compactBox.slide * compactBox.travel : (1 - compactBox.slide) * compactBox.travel
            opacity: compactBox.flip ? 1 - compactBox.slide : compactBox.slide
        }
    }

    // --- Présentation complète --------------------------------------------------------------------------------------------
    Item {
        id: full
        anchors.fill: parent
        visible: !root.compact
        clip: true

        readonly property bool listShown: root.wanted && root.phase === "synced" && root.lines.length > 0
        readonly property bool plainShown: root.wanted && root.phase === "plain"

        layer.enabled: visible && (listShown || plainShown)
        layer.effect: MultiEffect {
            maskEnabled: true
            maskSource: edgeMask
            maskThresholdMin: 0.5
            maskSpreadAtMin: 1
        }

        Item {
            id: edgeMask
            visible: false
            layer.enabled: true
            width: full.width
            height: full.height
            Rectangle {
                anchors.fill: parent
                gradient: Gradient {
                    orientation: Gradient.Vertical
                    GradientStop {
                        position: 0
                        color: Qt.alpha(Colours.palette.m3onSurface, 0)
                    }
                    GradientStop {
                        position: root.fadeAmount
                        color: Qt.alpha(Colours.palette.m3onSurface, 1)
                    }
                    GradientStop {
                        position: 1 - root.fadeAmount
                        color: Qt.alpha(Colours.palette.m3onSurface, 1)
                    }
                    GradientStop {
                        position: 1
                        color: Qt.alpha(Colours.palette.m3onSurface, 0)
                    }
                }
            }
        }

        ListView {
            id: list
            anchors.fill: parent
            opacity: full.listShown ? 1 : 0
            visible: opacity > 0
            interactive: false
            model: full.listShown ? root.lines : []
            currentIndex: Math.max(0, root.current)
            highlightRangeMode: ListView.StrictlyEnforceRange
            highlightMoveDuration: root.reduced ? 0 : Appearance.anim.durations.large
            highlightMoveVelocity: -1
            preferredHighlightBegin: Math.max(0, height * root.anchorFraction - (currentItem?.height ?? 0) / 2)
            preferredHighlightEnd: preferredHighlightBegin + (currentItem?.height ?? 0)
            displayMarginBeginning: height
            displayMarginEnd: height
            onModelChanged: Qt.callLater(() => positionViewAtIndex(currentIndex, ListView.Center))

            Behavior on opacity {
                enabled: !root.reduced
                Anim {
                    duration: Appearance.anim.durations.normal
                }
            }

            delegate: Item {
                id: line
                required property var modelData
                required property int index
                readonly property bool isCurrent: index === root.current
                readonly property int distance: Math.abs(index - Math.max(0, root.current))
                readonly property bool interlude: String(modelData.text ?? "") === ""
                readonly property real baseOpacity: isCurrent
                    ? 1
                    : Math.max(root.minOpacity, root.neighbourOpacity - (distance - 1) * root.falloffStep)

                width: ListView.view.width
                height: label.implicitHeight + root.lineGap

                StyledText {
                    id: label
                    x: Appearance.padding.small
                    width: parent.width - Appearance.padding.small * 2
                    text: line.interlude ? "…" : String(line.modelData.text)
                    wrapMode: Text.WordWrap
                    color: line.isCurrent ? Colours.palette.m3onSurface : Colours.palette.m3onSurfaceVariant
                    font.pointSize: root.fontSize
                    font.weight: Font.Bold
                    transformOrigin: Item.Left
                    scale: line.isCurrent ? 1 : root.dimScale
                    opacity: tap.containsMouse && tap.enabled
                        ? Math.max(line.baseOpacity, root.hoverOpacity)
                        : line.baseOpacity * line.breath
                    Behavior on scale {
                        enabled: !root.reduced
                        Anim {
                            duration: Appearance.anim.durations.expressiveDefaultSpatial
                            easing.bezierCurve: Appearance.anim.curves.expressiveDefaultSpatial
                        }
                    }
                    Behavior on opacity {
                        enabled: !root.reduced && !(line.interlude && line.isCurrent)
                        Anim {
                            duration: Appearance.anim.durations.expressiveEffects
                            easing.bezierCurve: Appearance.anim.curves.expressiveEffects
                        }
                    }
                }

                // Interlude : le « … » respire seulement quand c'est la ligne en cours et que la musique joue.
                property real breath: 1
                SequentialAnimation on breath {
                    running: line.interlude && line.isCurrent && root.running && !root.reduced
                    loops: Animation.Infinite
                    onRunningChanged: if (!running)
                        line.breath = 1
                    NumberAnimation {
                        to: root.interludeLow
                        duration: root.interludeMs
                        easing.type: Easing.BezierSpline
                        easing.bezierCurve: Appearance.anim.curves.standard
                    }
                    NumberAnimation {
                        to: 1
                        duration: root.interludeMs
                        easing.type: Easing.BezierSpline
                        easing.bezierCurve: Appearance.anim.curves.standard
                    }
                }

                MouseArea {
                    id: tap
                    anchors.fill: parent
                    enabled: root.player?.canSeek ?? false
                    hoverEnabled: true
                    cursorShape: Qt.PointingHandCursor
                    onClicked: root.seekTo(Number(line.modelData.time))
                }
            }
        }

        // Paroles non synchronisées : texte statique défilant.
        Flickable {
            id: plainFlick
            anchors.fill: parent
            opacity: full.plainShown ? 1 : 0
            visible: opacity > 0
            contentWidth: width
            contentHeight: plainColumn.height + height * root.fadeAmount * 2
            boundsBehavior: Flickable.StopAtBounds
            clip: true
            Behavior on opacity {
                enabled: !root.reduced
                Anim {
                    duration: Appearance.anim.durations.normal
                }
            }
            Column {
                id: plainColumn
                y: plainFlick.height * root.fadeAmount
                x: Appearance.padding.small
                width: plainFlick.width - Appearance.padding.small * 2
                spacing: Appearance.spacing.small
                StyledText {
                    width: parent.width
                    text: qsTr("Paroles non synchronisées")
                    color: Colours.palette.m3outline
                    font.pointSize: Appearance.font.size.small
                }
                StyledText {
                    width: parent.width
                    text: String(root.lyr?.plain ?? "")
                    wrapMode: Text.WordWrap
                    color: Colours.palette.m3onSurfaceVariant
                    font.pointSize: root.fontSize
                    font.weight: Font.Medium
                }
            }
        }

        // Chargement, introuvables, instrumental : un message calme au centre.
        Column {
            id: message
            anchors.centerIn: parent
            width: parent.width - Appearance.padding.large * 2
            spacing: Appearance.spacing.small
            opacity: root.messageShown ? 1 : 0
            visible: opacity > 0
            Behavior on opacity {
                enabled: !root.reduced
                Anim {
                    duration: Appearance.anim.durations.normal
                }
            }
            MaterialIcon {
                id: msgIcon
                anchors.horizontalCenter: parent.horizontalCenter
                text: root.messageIcon()
                color: Colours.palette.m3outline
                font.pointSize: root.fontSize * root.iconScale
                opacity: pulse
                property real pulse: 1
                SequentialAnimation on pulse {
                    running: root.messageShown && root.phase === "loading" && !root.reduced
                    loops: Animation.Infinite
                    onRunningChanged: if (!running)
                        msgIcon.pulse = 1
                    NumberAnimation {
                        to: root.interludeLow
                        duration: root.interludeMs
                        easing.type: Easing.BezierSpline
                        easing.bezierCurve: Appearance.anim.curves.standard
                    }
                    NumberAnimation {
                        to: 1
                        duration: root.interludeMs
                        easing.type: Easing.BezierSpline
                        easing.bezierCurve: Appearance.anim.curves.standard
                    }
                }
            }
            StyledText {
                width: parent.width
                horizontalAlignment: Text.AlignHCenter
                wrapMode: Text.WordWrap
                text: root.messageText()
                color: Colours.palette.m3onSurfaceVariant
                font.pointSize: Appearance.font.size.small
            }
        }
    }
}
