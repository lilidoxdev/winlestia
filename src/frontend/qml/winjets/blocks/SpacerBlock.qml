import QtQuick

// Bloc `spacer` : espace élastique (BlockNode lui donne un poids de 1 par défaut).
Item {
    property var spec: null
    property var ctx: ({})
    property var host: null

    implicitWidth: 0
    implicitHeight: 0
}
