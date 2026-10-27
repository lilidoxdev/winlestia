pragma Singleton
import QtQuick
QtObject {
 readonly property var active: Bridge.player.available ? Bridge.player : null
 readonly property var list: Bridge.player.available ? [Bridge.player] : []
 property var manualActive: null
 function getIdentity(player) {return player.identity}
}
