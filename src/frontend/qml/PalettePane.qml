import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.config
import qs.services
import qs.modules.launcher as Launcher
import Quickshell
Column {
    id:root
    spacing:14
    PersistentProperties {id:vis;property bool launcher:true;property bool session:false}
    RowLayout {
        id:heading;width:parent.width
        MaterialIcon {text:qsTr("search");font.pointSize:22;color:Colours.palette.m3primary}
        StyledText {text:qsTr("Commande rapide");font.pointSize:18;Layout.fillWidth:true}
        StyledText {text:Bridge.settings.paletteShortcut||"Ctrl + F2";font.pointSize:10;color:Colours.palette.m3outline}
        IconButton {icon:"close";type:IconButton.Text;onClicked:Bridge.closePanel()}
    }
    Launcher.Content {id:launcher;width:parent.width;visibilities:vis;panels:({});maxHeight:Math.min(610,Bridge.screenHeight-210)}
    RowLayout {
        id:footer;width:parent.width
        StyledText {text:qsTr("↑ ↓ naviguer · Entrée ouvrir · Échap fermer");font.pointSize:10;color:Colours.palette.m3outline;Layout.fillWidth:true}
        StyledText {text:qsTr("> commandes · 24 × 7 calculer");font.pointSize:10;color:Colours.palette.m3outline}
    }
    function reset(){vis.launcher=true;Bridge.searchPalette("");Qt.callLater(()=>Bridge.focusPalette())}
}
