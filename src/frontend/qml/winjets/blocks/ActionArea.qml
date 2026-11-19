import QtQuick

// Zone cliquable d'un bloc portant un champ `action` (setState, refresh, openUrl, openSettings). Inactive pendant l'édition.
MouseArea {
    id: area

    property var spec: null
    property var host: null

    readonly property bool wired: !!spec && !!spec.action && typeof spec.action === "object"

    anchors.fill: parent
    enabled: wired && !(host && host.editing)
    visible: wired
    cursorShape: Qt.PointingHandCursor
    onClicked: if (host)
        host.runAction(spec.action)
}
