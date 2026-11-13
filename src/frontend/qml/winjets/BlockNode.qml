import QtQuick
import QtQuick.Layouts

// Un nœud de la composition. Le type du bloc choisit un fichier dans une liste fermée (jamais de chemin venant du paquet) ;
// les conteneurs réutilisent BlockNode pour leurs enfants, d'où le chargement par URL (pas de cycle de types).
Loader {
    id: node

    property var spec: null
    property var ctx: ({})
    property var host: null
    // Conteneur parent : "column", "row", "grid", "stack" ; sert à choisir l'axe principal pour grow/align.
    property string parentKind: ""
    property string parentAlign: ""

    readonly property var sources: ({
            column: "containers/ColumnBlock.qml",
            row: "containers/RowBlock.qml",
            stack: "containers/StackBlock.qml",
            grid: "containers/GridBlock.qml",
            text: "blocks/TextBlock.qml",
            image: "blocks/ImageBlock.qml",
            clock: "blocks/ClockBlock.qml",
            calendar: "blocks/CalendarBlock.qml",
            list: "blocks/ListBlock.qml",
            chart: "blocks/ChartBlock.qml",
            stat: "blocks/StatBlock.qml",
            progress: "blocks/ProgressBlock.qml",
            dots: "blocks/DotsBlock.qml",
            buttons: "blocks/ButtonsBlock.qml",
            spacer: "blocks/SpacerBlock.qml",
            divider: "blocks/DividerBlock.qml",
            media: "blocks/MediaBlock.qml",
            visualizer: "blocks/VisualizerBlock.qml"
        })
    readonly property string kind: spec && typeof spec.type === "string" ? spec.type : ""
    readonly property bool shown: !!spec && (!spec.visibleWhen || Bridge.winjets.condition(spec.visibleWhen, ctx))
    readonly property real weight: spec && spec.grow > 0 ? Number(spec.grow) : (kind === "spacer" ? 1 : 0)
    readonly property string align: spec && spec.align && kind !== "text" ? spec.align : parentAlign
    readonly property bool stretch: align === "" || align === "stretch"

    visible: shown
    source: shown && sources.hasOwnProperty(kind) ? Qt.resolvedUrl(sources[kind]) : ""

    // Axe principal : vertical dans une colonne, horizontal dans une ligne ; l'autre axe suit `align` (étirement par défaut).
    Layout.fillWidth: parentKind === "row" ? weight > 0 : (parentKind === "column" || parentKind === "grid" ? stretch : false)
    Layout.fillHeight: parentKind === "column" ? weight > 0 : (parentKind === "row" || parentKind === "grid" ? (parentKind === "row" ? stretch : weight > 0) : false)
    // Un enfant `grow` d'une colonne a une base proportionnelle à son poids (1 px par unité) : l'espace se partage selon `grow`.
    Layout.preferredHeight: parentKind === "column" && weight > 0 ? weight : -1
    Layout.horizontalStretchFactor: weight > 0 ? weight : 1
    Layout.verticalStretchFactor: weight > 0 ? weight : 1
    Layout.alignment: {
        if (stretch)
            return 0;
        const a = align;
        if (parentKind === "row")
            return a === "start" ? Qt.AlignTop : a === "end" ? Qt.AlignBottom : Qt.AlignVCenter;
        return a === "start" ? Qt.AlignLeft : a === "end" ? Qt.AlignRight : Qt.AlignHCenter;
    }

    Binding {
        target: node.item
        property: "spec"
        value: node.spec
        when: node.item !== null
    }
    Binding {
        target: node.item
        property: "ctx"
        value: node.ctx
        when: node.item !== null
    }
    Binding {
        target: node.item
        property: "host"
        value: node.host
        when: node.item !== null
    }
}
