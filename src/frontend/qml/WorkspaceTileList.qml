// Liste virtualisée des tuiles (sections, lignes, tuiles) de l'espace.
// Extrait de WorkspaceBrowser.qml (pur découpage) : les ids du parent sont reçus en propriétés requises.
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.services
import qs.config

ListView {
    id: grid
    required property var browser
    required property var context
    required property var query
    required property var rows
    objectName: "workspaceGrid"
    Layout.fillWidth: true
    Layout.fillHeight: true
    clip: true
    cacheBuffer: 260
    property real reveal: 1
    readonly property int columns: browser.listMode ? 1 : Math.max(3, Math.floor(width / 150))
    readonly property real iconSize: Bridge.settings.iconSize ?? 56
    readonly property real cellWidth: Math.floor(width / columns)
    readonly property real cellHeight: browser.listMode ? 74 : Math.round(iconSize + 84)
    onColumnsChanged: browser.syncModel()
    model: rows
    reuseItems: true
    currentIndex: -1
    boundsBehavior: Flickable.StopAtBounds
    opacity: browser.pBody
    onMovementStarted: {
        browser.hoverTip(null);
        context.close();
    }
    onContentYChanged: if (context.opened)
        context.close()
    function step(d) {
        if (!browser.flat.length)
            return;
        const n = Math.max(0, Math.min(browser.flat.length - 1, browser.cur < 0 ? 0 : browser.cur + d));
        browser.cur = n;
        positionViewAtIndex(browser.lineOfTile(n), ListView.Contain);
    }
    Keys.onReturnPressed: {
        if (browser.cur >= 0 && browser.cur < browser.flat.length)
            Bridge.action("launch", {
                id: browser.flat[browser.cur].id
            });
    }
    Keys.onLeftPressed: step(-1)
    Keys.onRightPressed: step(1)
    Keys.onDownPressed: step(columns)
    Keys.onUpPressed: {
        if (browser.cur < columns) {
            query.forceActiveFocus();
            browser.cur = -1;
        } else
            step(-columns);
    }
    Keys.onPressed: event => {
        if (event.text.length > 0 && !(event.modifiers & (Qt.ControlModifier | Qt.AltModifier))
            && event.text.charCodeAt(0) > 32) {
            query.forceActiveFocus();
            query.text += event.text;
            query.cursorPosition = query.text.length;
            event.accepted = true;
        }
    }
    section.property: "sec"
    section.criteria: ViewSection.FullString
    section.labelPositioning: ViewSection.InlineLabels | ViewSection.CurrentLabelAtStart
    section.delegate: Item {
        id: head
        required property string section
        readonly property var info: browser.secInfo[section] ?? ({
                label: "",
                count: 0,
                closed: false,
                icon: ""
            })
        width: grid.width
        height: 42
        Rectangle {
            anchors.fill: parent
            color: Qt.alpha(Colours.palette.m3surface, .94)
        }
        Rectangle {
            anchors.fill: parent
            anchors.margins: 2
            radius: Appearance.rounding.small
            color: Colours.palette.m3onSurface
            opacity: headHit.pressed ? .12 : headHit.containsMouse ? .07 : 0
        }
        RowLayout {
            anchors.fill: parent
            anchors.leftMargin: Appearance.padding.normal
            anchors.rightMargin: Appearance.padding.normal
            spacing: Appearance.spacing.small
            MaterialIcon {
                text: "expand_more"
                visible: !browser.flatMode
                rotation: head.info.closed ? -90 : 0
                color: Colours.palette.m3onSurfaceVariant
                Behavior on rotation {
                    WorkspaceSpring {}
                }
            }
            MaterialIcon {
                text: head.info.icon
                visible: text !== ""
                fill: 1
                color: Colours.palette.m3primary
                font.pointSize: Appearance.font.size.large
            }
            StyledText {
                text: head.info.label
                font.pointSize: Appearance.font.size.normal
                font.weight: Font.Medium
                color: Colours.palette.m3onSurface
            }
            Rectangle {
                implicitWidth: Math.max(24, cnt.implicitWidth + 14)
                implicitHeight: 22
                radius: Appearance.rounding.full
                color: Colours.palette.m3surfaceContainerHighest
                StyledText {
                    id: cnt
                    anchors.centerIn: parent
                    text: head.info.count
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3onSurfaceVariant
                }
            }
            Item {
                Layout.fillWidth: true
            }
        }
        Rectangle {
            anchors.bottom: parent.bottom
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.leftMargin: Appearance.padding.normal
            anchors.rightMargin: Appearance.padding.normal
            height: 1
            color: Colours.palette.m3outlineVariant
            opacity: .6
        }
        MouseArea {
            id: headHit
            objectName: "sectionHeader"
            anchors.fill: parent
            hoverEnabled: true
            enabled: !browser.flatMode && !query.text
            cursorShape: enabled ? Qt.PointingHandCursor : Qt.ArrowCursor
            onClicked: browser.toggleSection(head.section)
        }
    }
    delegate: Item {
        id: line
        required property int index
        required property string sec
        required property int first
        required property int n
        width: grid.width
        height: n > 0 ? grid.cellHeight : 0
        visible: n > 0
        property bool live: true
        ListView.onPooled: {
            live = false;
            if (browser.tipTarget)
                browser.hoverTip(null);
        }
        ListView.onReused: live = true
        Repeater {
            model: line.n
            delegate: Item {
                id: tile
                required property int index
                readonly property int flatIndex: line.first + index
                readonly property var e: browser.flat[flatIndex] ?? ({
                        id: "",
                        name: "",
                        category: "",
                        kind: "app",
                        favorite: false
                    })
                readonly property string eid: e.id
                readonly property string name: e.name
                readonly property string category: e.category || ""
                readonly property bool folder: e.kind === "folder"
                readonly property bool favorite: !!e.favorite
                x: index * grid.cellWidth
                width: grid.cellWidth
                height: grid.cellHeight
                // Entrance stagger: only the first rows ever have a non-final value, and only while grid.reveal runs.
                readonly property real rt: (grid.reveal * 880 - Math.min(line.index, 6) * 55) / 340
                readonly property real t: rt <= 0 ? 0 : rt >= 1 ? 1 : rt
                readonly property bool current: browser.cur === flatIndex && grid.activeFocus
                function backOut(x) {
                    const u = x - 1;
                    return 1 + 2.6 * u * u * u + 1.6 * u * u;
                }
                readonly property bool hov: line.live && hit.containsMouse
                readonly property bool prs: line.live && hit.pressed
                Item {
                    id: card
                    anchors.fill: parent
                    anchors.margins: Appearance.padding.small * .8
                    opacity: tile.t
                    transform: Translate {
                        y: browser.fx ? (1 - tile.t) * 18 : 0
                    }
                    scale: browser.fx ? .88 + .12 * tile.backOut(tile.t) : 1
                    Item {
                        id: body
                        anchors.fill: parent
                        // Hover lift with overshoot, press squash.
                        scale: tile.prs ? .96 : tile.hov && browser.fx ? 1.04 : 1
                        Behavior on scale {
                            WorkspaceSpring {}
                        }
                        Rectangle {
                            anchors.fill: parent
                            radius: Appearance.rounding.normal
                            color: Colours.palette.m3onSurface
                            opacity: tile.prs ? .12 : tile.hov ? .07 : 0
                            Behavior on opacity {
                                enabled: !Appearance.reducedEffects
                                Anim {
                                    duration: Appearance.anim.durations.small
                                }
                            }
                            border.width: tile.current ? 2 : 0
                            border.color: Colours.palette.m3primary
                        }
                        Rectangle {
                            id: iconWell
                            width: browser.listMode ? 52 : iconBox.width + 24
                            height: width
                            radius: tile.folder ? Appearance.rounding.large : Appearance.rounding.normal
                            x: browser.listMode ? Appearance.padding.large + Appearance.padding.small
                                : (parent.width - width) / 2
                            y: browser.listMode ? (parent.height - height) / 2 : Appearance.padding.normal
                            color: tile.folder ? (tile.hov ? Colours.palette.m3secondaryContainer
                                : Qt.alpha(Colours.palette.m3secondaryContainer,
                                .7)) : tile.hov ? Colours.palette.m3surfaceContainerHighest
                                : Colours.palette.m3surfaceContainerHigh
                            Item {
                                id: iconBox
                                width: browser.listMode ? 40 : grid.iconSize
                                height: width
                                anchors.centerIn: parent
                                Image {
                                    id: logo
                                    anchors.fill: parent
                                    source: tile.folder || !tile.eid ? "" : Bridge.iconUrl(tile.eid, Bridge.iconRevision)
                                    asynchronous: true
                                    cache: true
                                    fillMode: Image.PreserveAspectFit
                                    sourceSize: Qt.size(width * 2, height * 2)
                                    opacity: status === Image.Ready ? 1 : 0
                                    Behavior on opacity {
                                        enabled: !Appearance.reducedEffects
                                        Anim {
                                            duration: Appearance.anim.durations.small
                                        }
                                    }
                                }
                                MaterialIcon {
                                    anchors.centerIn: parent
                                    text: tile.folder ? "folder" : "apps"
                                    fill: tile.folder ? 1 : 0
                                    font.pointSize: tile.folder ? Appearance.font.size.large
                                        : Appearance.font.size.extraLarge
                                    color: tile.folder ? Colours.palette.m3onSecondaryContainer : Colours.palette.m3primary
                                    visible: tile.folder || logo.status !== Image.Ready
                                }
                            }
                        }
                        MaterialIcon {
                            x: iconWell.x + iconWell.width - width + 4
                            y: iconWell.y - 4
                            text: qsTr("star")
                            fill: 1
                            font.pointSize: Appearance.font.size.small
                            color: Colours.palette.m3tertiary
                            visible: tile.favorite
                        }
                        StyledText {
                            id: nameText
                            x: browser.listMode ? 84 : Appearance.padding.normal
                            y: browser.listMode ? 14 : iconWell.y + iconWell.height + 8
                            width: browser.listMode ? parent.width - 104 : parent.width - Appearance.padding.normal * 2
                            text: tile.name
                            horizontalAlignment: browser.listMode ? Text.AlignLeft : Text.AlignHCenter
                            font.pointSize: Appearance.font.size.small
                            font.weight: Font.Medium
                            elide: Text.ElideRight
                        }
                        StyledText {
                            id: catText
                            x: browser.listMode ? 84 : Appearance.padding.normal
                            y: browser.listMode ? 36 : iconWell.y + iconWell.height + 27
                            width: browser.listMode ? parent.width - 104 : parent.width - Appearance.padding.normal * 2
                            text: tile.category
                            horizontalAlignment: browser.listMode ? Text.AlignLeft : Text.AlignHCenter
                            font.pointSize: Appearance.font.size.small
                            color: Colours.palette.m3outline
                            elide: Text.ElideRight
                            opacity: .85
                        }
                        MouseArea {
                            id: hit
                            anchors.fill: parent
                            hoverEnabled: true
                            cursorShape: Qt.PointingHandCursor
                            acceptedButtons: Qt.LeftButton | Qt.RightButton
                            // Shared tooltip carries the full name and category (either may be elided).
                            onEntered: browser.hoverTip(tile,
                                tile.name + (tile.category && (catText.truncated || nameText.truncated)
                                ? " · " + tile.category : ""))
                            onExited: if (browser.tipTarget === tile)
                                browser.hoverTip(null)
                            onClicked: event => {
                                browser.hoverTip(null);
                                if (event.button === Qt.RightButton) {
                                    browser.selectedId = tile.eid;
                                    browser.selectedName = tile.name;
                                    browser.selectedCategory = tile.category;
                                    browser.selectedFavorite = tile.favorite;
                                    browser.selectedKind = tile.e.kind || "app";
                                    const p = hit.mapToItem(browser, event.x, event.y);
                                    context.openAt(p.x, p.y);
                                } else
                                    Bridge.action("launch", {
                                        id: tile.eid
                                    });
                            }
                        }
                    }
                }
            }
        }
    }
    C.ScrollBar.vertical: StyledScrollBar {
        flickable: grid
    }
    EmptyState {
        id: empty
        anchors.centerIn: parent
        visible: opacity > .01 && browser.tileCount === 0
        opacity: browser.tileCount === 0 ? 1 : 0
        scale: browser.tileCount === 0 || !browser.fx ? 1 : .85
        Behavior on opacity {
            enabled: !Appearance.reducedEffects
            Anim {
                duration: Appearance.anim.durations.small
            }
        }
        Behavior on scale {
            WorkspaceSpring {}
        }
        icon: query.text ? "search_off" : "inbox"
        title: query.text ? qsTr("Aucun accès ne correspond à cette recherche")
            : qsTr("Cet espace est prêt à accueillir tes accès")
        body: query.text ? "" : qsTr("Ajoute une application, un dossier ou un document pour le retrouver ici.")
        actionText: query.text ? qsTr("Effacer la recherche") : qsTr("Ajouter un accès")
        actionIcon: query.text ? "close" : "add"
        onActionTriggered: {
            if (query.text)
                query.clear();
            else
                Bridge.action("addAccess", {
                    board: browser.scope
                });
        }
    }
}
