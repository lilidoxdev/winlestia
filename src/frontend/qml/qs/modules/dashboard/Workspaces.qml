// Workspaces tab: greeting header, 4 category cards (blob shapes + icon preview), favourites, recents, quick actions.
// Mouvement : entrée en cascade quand l'onglet devient visible (courbe spatiale expressive, léger dépassement),
// survol/appui façon ressort. Tout dépend de `shown` (onglet actif ET tableau de bord visible) ; aucun Timer, aucune animation en boucle.
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

    // Défini par le tableau de bord (onglet courant == Workspaces). Vaut true par défaut en usage autonome.
    property bool tabActive: true
    readonly property bool shown: tabActive && visible
    readonly property bool fx: !Appearance.reducedEffects
    property int hour: new Date().getHours()
    onShownChanged: if (shown)
        hour = new Date().getHours()

    readonly property var boards: [
        {
            id: "apps",
            name: qsTr("Applications"),
            icon: "apps",
            lobes: 8,
            amp: 0.09
        },
        {
            id: "games",
            name: qsTr("Jeux"),
            icon: "sports_esports",
            lobes: 4,
            amp: 0.17
        },
        {
            id: "projects",
            name: qsTr("Projets"),
            icon: "code_blocks",
            lobes: 6,
            amp: 0.13
        },
        {
            id: "documents",
            name: qsTr("Documents"),
            icon: "folder_open",
            lobes: 5,
            amp: 0.12
        }
    ]
    readonly property int favCount: Math.min(6, (Bridge.favorites ?? []).length)
    readonly property int recentCount: Math.min(3, (Bridge.recent ?? []).length)

    // Un seul passage sur Bridge.apps (réévalué seulement si le catalogue change) : compteur + 3 icônes d'aperçu par tableau.
    readonly property var boardInfo: {
        const info = {};
        for (const b of boards)
            info[b.id] = {
                count: 0,
                top: []
            };
        const list = Bridge.apps ?? [];
        for (let i = 0; i < list.length; i++) {
            const e = list[i], x = info[e.board];
            if (!x)
                continue;
            x.count++;
            if (x.top.some(t => t.id === e.id))
                continue;
            const rank = e.recent >= 0 ? e.recent : (e.favorite ? 500 + i : 1000 + i);
            let at = x.top.findIndex(t => t.rank > rank);
            if (at < 0)
                at = x.top.length;
            if (at < 3) {
                x.top.splice(at, 0, {
                    id: e.id,
                    name: e.name,
                    rank: rank
                });
                if (x.top.length > 3)
                    x.top.pop();
            }
        }
        return info;
    }

    function tone(id: string): color {
        const p = Colours.palette;
        switch (id) {
        case "games":
            return p.m3secondaryContainer;
        case "projects":
            return Qt.tint(p.m3surfaceContainerHigh, Qt.alpha(p.m3tertiary, 0.30));
        case "documents":
            return Qt.tint(p.m3surfaceContainerHigh, Qt.alpha(p.m3error, 0.24));
        default:
            return p.m3primaryContainer;
        }
    }
    function onTone(id: string): color {
        const p = Colours.palette;
        switch (id) {
        case "games":
            return p.m3onSecondaryContainer;
        case "projects":
            return p.m3tertiary;
        case "documents":
            return p.m3error;
        default:
            return p.m3onPrimaryContainer;
        }
    }






    ColumnLayout {
        id: col
        anchors.fill: parent
        spacing: Appearance.spacing.normal

        // En-tête : salutation selon l'heure
        WorkspacesHeader {
            id: header
            root: root
        }

        // Category cards
        WorkspacesCards {
            root: root
        }

        // Favourites + recents
        RowLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            spacing: Appearance.spacing.normal

            WorkspacesFavorites {
                id: favCard
                root: root
            }

            WorkspacesRecents {
                id: recentCard
                root: root
            }
        }

        // Quick actions
        WorkspacesQuick {
            root: root
        }
    }
}
