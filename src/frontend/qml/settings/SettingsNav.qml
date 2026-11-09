import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

// Colonne de gauche : champ de recherche, puis liste des pages (pastille d'icône + titre + sous-titre). Pendant une recherche, la
// liste est remplacée par les lignes correspondantes. Étroit : icônes seules ; la recherche étend la colonne sur la page.
Item {
    id: nav

    property int current: 0
    property bool compact: false
    readonly property int ringInset: 3
    readonly property bool searching: field.text.trim() !== ""
    readonly property bool wide: !compact || field.activeFocus || searching
    property int focusIndex: 0
    property var results: []
    property alias searchField: field

    signal pageChosen(int index)
    signal resultChosen(var entry)
    signal dashboardRequested

    function fold(s: var): string {
        return String(s ?? "").normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[’‘]/g, "'").toLowerCase();
    }

    // Lignes statiques + lignes qui n'existent qu'à l'exécution (une par raccourci, une par réseau social).
    function allEntries(): var {
        const out = SettingsIndex.entries.slice();
        for (const sc of (Bridge.settings.shortcuts ?? []))
            out.push({
                id: "sc:" + sc.id,
                page: 3,
                section: qsTr(sc.group ?? ""),
                label: qsTr(sc.label ?? ""),
                desc: sc.combo ?? "",
                kw: "raccourci shortcut clavier keyboard touche " + (sc.id ?? "")
            });
        for (const net of (Bridge.social.networks ?? []))
            out.push({
                id: "net:" + net.id,
                page: 5,
                section: qsTr("Réseaux sociaux"),
                label: net.name ?? net.id,
                desc: "",
                kw: "reseau social network compte",
                anchor: "socialNetworks"
            });
        return out;
    }

    function runSearch(): void {
        const q = fold(field.text).trim();
        if (q === "") {
            results = [];
            return;
        }
        const words = q.split(/\s+/);
        const scored = [];
        for (const e of allEntries()) {
            const head = fold(e.label);
            const hay = head + " " + fold(e.desc) + " " + fold(e.section) + " " + fold(SettingsIndex.pages[e.page].title) + " "
                + fold(e.kw);
            if (!words.every(w => hay.includes(w)))
                continue;
            scored.push({
                entry: e,
                score: words.every(w => head.includes(w)) ? 0 : 1
            });
        }
        scored.sort((a, b) => a.score - b.score);
        results = scored.map(s => s.entry);
    }

    function openResult(i: int): void {
        const entry = results[i];
        if (!entry)
            return;
        field.text = "";
        resultChosen(entry);
    }

    function clearSearch(): void {
        field.text = "";
    }

    function chooseFocused(): void {
        pageChosen(focusIndex);
    }

    onCurrentChanged: focusIndex = current

    Rectangle {
        // Au-dessus de la page quand la colonne compacte est étendue par la recherche.
        anchors.fill: parent
        anchors.margins: -Appearance.padding.small
        visible: nav.compact && nav.wide
        radius: Appearance.rounding.large
        color: Qt.alpha(Colours.surfaceBase, 0.97)
        border.width: 1
        border.color: Qt.alpha(Colours.palette.m3outlineVariant, 0.6)
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: Appearance.spacing.normal

        StyledRect {
            id: searchBox

            Layout.fillWidth: true
            implicitHeight: 44
            radius: Appearance.rounding.full
            color: Qt.alpha(Colours.palette.m3surfaceContainer, field.activeFocus ? 1 : 0.8)
            border.width: field.activeFocus ? 2 : 1
            border.color: field.activeFocus ? Colours.palette.m3primary : Qt.alpha(Colours.palette.m3outlineVariant, 0.6)

            MaterialIcon {
                id: searchIcon

                x: nav.wide ? Appearance.padding.large : (parent.width - width) / 2
                anchors.verticalCenter: parent.verticalCenter
                text: "search"
                color: field.activeFocus ? Colours.palette.m3primary : Colours.palette.m3outline
                font.pointSize: Appearance.font.size.large
            }

            StyledTextField {
                id: field

                objectName: "prefsSearch"
                anchors.fill: parent
                anchors.leftMargin: 48
                anchors.rightMargin: clearButton.visible ? 40 : 14
                visible: nav.wide
                placeholderText: qsTr("Rechercher un réglage")
                verticalAlignment: TextInput.AlignVCenter
                onTextChanged: {
                    nav.runSearch();
                    resultList.currentIndex = 0;
                }
                Keys.onShortcutOverride: event => {
                    if (event.key === Qt.Key_Escape && text.length > 0)
                        event.accepted = true;
                }
                Keys.onEscapePressed: event => {
                    if (text !== "") {
                        text = "";
                        event.accepted = true;
                    } else {
                        event.accepted = false;
                    }
                }
                Keys.onDownPressed: event => {
                    if (nav.searching)
                        resultList.currentIndex = Math.min(resultList.count - 1, resultList.currentIndex + 1);
                    else
                        navList.forceActiveFocus();
                    event.accepted = true;
                }
                Keys.onUpPressed: event => {
                    resultList.currentIndex = Math.max(0, resultList.currentIndex - 1);
                    event.accepted = true;
                }
                Keys.onReturnPressed: nav.openResult(resultList.currentIndex)
                Keys.onEnterPressed: nav.openResult(resultList.currentIndex)
            }

            // Colonne repliée (compacte) : la loupe ouvre le champ.
            MouseArea {
                anchors.fill: parent
                visible: !nav.wide
                cursorShape: Qt.PointingHandCursor
                onClicked: field.forceActiveFocus()
            }

            IconButton {
                id: clearButton

                anchors.right: parent.right
                anchors.rightMargin: Appearance.padding.small
                anchors.verticalCenter: parent.verticalCenter
                visible: nav.searching
                icon: "close"
                type: IconButton.Text
                onClicked: {
                    nav.clearSearch();
                    field.forceActiveFocus();
                }
            }
        }

        // ---- page list ----
        Flickable {
            id: navFlick

            Layout.fillWidth: true
            Layout.fillHeight: true
            visible: !nav.searching
            clip: true
            contentHeight: navList.height
            boundsBehavior: Flickable.StopAtBounds
            interactive: contentHeight > height

            FocusScope {
                id: navList

                objectName: "prefsNav"
                width: navFlick.width
                // Marge pour que l'anneau de focus (dessiné 2 px hors de chaque pastille) ne soit jamais coupé par le rognage du flickable.
                height: navColumn.implicitHeight + nav.ringInset * 2
                activeFocusOnTab: true

                onActiveFocusChanged: if (activeFocus)
                    nav.focusIndex = nav.current

                Keys.onPressed: event => {
                    const last = SettingsIndex.pages.length - 1;
                    if (event.key === Qt.Key_Down) {
                        nav.focusIndex = Math.min(last, nav.focusIndex + 1);
                    } else if (event.key === Qt.Key_Up) {
                        nav.focusIndex = Math.max(0, nav.focusIndex - 1);
                    } else if (event.key === Qt.Key_Home) {
                        nav.focusIndex = 0;
                    } else if (event.key === Qt.Key_End) {
                        nav.focusIndex = last;
                    } else if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter || event.key === Qt.Key_Space) {
                        nav.chooseFocused();
                    } else {
                        return;
                    }
                    event.accepted = true;
                    const it = navRepeater.itemAt(nav.focusIndex);
                    if (it)
                        navFlick.contentY = Math.max(0, Math.min(Math.max(0, navFlick.contentHeight - navFlick.height),
                            Math.min(Math.max(navFlick.contentY, it.y + it.height - navFlick.height), it.y)));
                }

                Column {
                    id: navColumn

                    x: nav.ringInset
                    y: nav.ringInset
                    width: parent.width - nav.ringInset * 2
                    spacing: 2

                    Repeater {
                        id: navRepeater

                        model: SettingsIndex.pages

                        Column {
                            id: slot

                            required property var modelData
                            required property int index

                            width: navColumn.width
                            spacing: 2

                            // Séparateur de groupe : un espace avec un fin trait.
                            Item {
                                visible: slot.index > 0 && SettingsIndex.pages[slot.index - 1].group !== slot.modelData.group
                                width: parent.width
                                height: visible ? Appearance.spacing.normal * 2 + 1 : 0

                                Rectangle {
                                    anchors.centerIn: parent
                                    width: nav.compact && !nav.wide ? 28 : parent.width - Appearance.padding.large * 2
                                    height: 1
                                    color: Qt.alpha(Colours.palette.m3outlineVariant, 0.7)
                                }
                            }

                            SettingsNavItem {
                                objectName: "prefsPage" + slot.index
                                width: parent.width
                                icon: slot.modelData.icon
                                title: slot.modelData.title
                                subtitle: slot.modelData.subtitle
                                compact: !nav.wide
                                selected: nav.current === slot.index
                                focused: navList.activeFocus && nav.focusIndex === slot.index
                                onActivated: {
                                    navList.forceActiveFocus();
                                    nav.focusIndex = slot.index;
                                    nav.pageChosen(slot.index);
                                }
                            }
                        }
                    }
                }
            }
        }

        // ---- search results (replace the page list) ----
        ListView {
            id: resultList

            objectName: "prefsResults"
            Layout.fillWidth: true
            Layout.fillHeight: true
            visible: nav.searching && nav.results.length > 0
            clip: true
            spacing: 4
            model: nav.results
            boundsBehavior: Flickable.StopAtBounds
            currentIndex: 0
            C.ScrollBar.vertical: StyledScrollBar {
                flickable: resultList
            }

            delegate: StyledRect {
                id: hit

                required property var modelData
                required property int index

                objectName: "prefsResult" + index
                width: ListView.view.width
                implicitHeight: 56
                radius: Appearance.rounding.full
                color: ListView.isCurrentItem ? Qt.alpha(Colours.palette.m3secondaryContainer, 1) : "transparent"

                StateLayer {
                    color: Colours.palette.m3onSurface

                    function onClicked(): void {
                        nav.openResult(hit.index);
                    }
                }

                StyledRect {
                    id: hitPill

                    anchors.left: parent.left
                    anchors.leftMargin: Appearance.padding.normal
                    anchors.verticalCenter: parent.verticalCenter
                    implicitWidth: 36
                    implicitHeight: 36
                    radius: Appearance.rounding.full
                    color: Qt.alpha(Colours.palette.m3primary, 0.14)

                    MaterialIcon {
                        anchors.centerIn: parent
                        text: SettingsIndex.pages[hit.modelData.page].icon
                        color: Colours.palette.m3primary
                    }
                }

                ColumnLayout {
                    anchors.left: hitPill.right
                    anchors.right: parent.right
                    anchors.verticalCenter: parent.verticalCenter
                    anchors.leftMargin: Appearance.spacing.normal
                    anchors.rightMargin: Appearance.padding.large
                    spacing: 0

                    StyledText {
                        Layout.fillWidth: true
                        elide: Text.ElideRight
                        text: hit.modelData.label
                        font.pointSize: Appearance.font.size.normal
                        color: hit.ListView.isCurrentItem ? Colours.palette.m3onSecondaryContainer : Colours.palette.m3onSurface
                    }

                    StyledText {
                        Layout.fillWidth: true
                        elide: Text.ElideRight
                        text: SettingsIndex.pages[hit.modelData.page].title + "  ›  " + hit.modelData.section
                        font.pointSize: Appearance.font.size.small
                        color: Colours.palette.m3outline
                    }
                }
            }
        }

        EmptyState {
            Layout.fillWidth: true
            Layout.fillHeight: true
            visible: nav.searching && nav.results.length === 0
            title: qsTr("Aucun réglage trouvé")
            body: qsTr("Essaie un autre mot")
        }

        StyledRect {
            id: dashButton

            Layout.fillWidth: true
            implicitHeight: 44
            radius: Appearance.rounding.full
            color: "transparent"
            activeFocusOnTab: true
            visible: !nav.searching

            Keys.onPressed: event => {
                if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter || event.key === Qt.Key_Space) {
                    nav.dashboardRequested();
                    event.accepted = true;
                }
            }

            StateLayer {
                color: Colours.palette.m3onSurface

                function onClicked(): void {
                    nav.dashboardRequested();
                }
            }

            SettingsFocusRing {
                target: dashButton
            }

            Row {
                x: nav.wide ? Appearance.padding.large : (parent.width - width) / 2
                anchors.verticalCenter: parent.verticalCenter
                spacing: Appearance.spacing.normal

                MaterialIcon {
                    anchors.verticalCenter: parent.verticalCenter
                    text: "dashboard"
                    color: Colours.palette.m3primary
                }

                StyledText {
                    visible: nav.wide
                    anchors.verticalCenter: parent.verticalCenter
                    text: qsTr("Tableau de bord")
                    font.pointSize: Appearance.font.size.small
                }
            }
        }
    }
}
