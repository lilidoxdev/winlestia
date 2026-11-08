// Windows service bindings composed from Caelestia's original controls.
import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config

ColumnLayout {
    property bool mixerOpen:false
    property bool wifiOpen:false
    signal toggleMixer()
    signal toggleWifi()
    spacing:Appearance.spacing.large
    RowLayout {
        Layout.fillWidth:true
        StyledText {text:qsTr("Commandes rapides");font.pointSize:Appearance.font.size.large;Layout.fillWidth:true}
        IconButton {icon:"close";type:IconButton.Text;onClicked:Bridge.closePanel()}
    }
    RowLayout {
        Layout.fillWidth:true;spacing:12
        IconTextButton {Layout.fillWidth:true;icon:"wifi";text:Bridge.controls.network || "Wi-Fi";toggle:true;
            checked:Bridge.controls.wifiOn??false;onClicked:Bridge.action("radio",{kind:"wifi"})}
        IconButton{objectName:"wifiToggle";icon:"chevron_right";type:IconButton.Text;rotation:wifiOpen?180:0;onClicked:toggleWifi();
            Behavior on rotation{Anim{duration:Appearance.anim.durations.small}}}
        IconTextButton {Layout.fillWidth:true;icon:"bluetooth";text:qsTr("Bluetooth");toggle:true;
            checked:Bridge.controls.bluetoothOn??false;onClicked:Bridge.action("radio",{kind:"bluetooth"})}
    }
    ColumnLayout {
        Layout.fillWidth:true;spacing:4
        RowLayout {Layout.fillWidth:true;MaterialIcon{text:qsTr("volume_up")} StyledText{text:qsTr("Volume");Layout.fillWidth:true
            } StyledText{text:Math.round(volume.value)+" %"} IconButton{objectName:"mixerToggle";icon:"chevron_right";type:IconButton.Text;
            rotation:mixerOpen?180:0;onClicked:toggleMixer();Behavior on rotation{Anim{duration:Appearance.anim.durations.small}}}}
        StyledSlider {
            id:volume
            Layout.fillWidth:true;implicitHeight:32;from:0;to:100;stepSize:1
            onMoved:Bridge.action("volume",{value:value})
            Binding {target:volume;property:"value";value:Bridge.controls.volume??0;when:!volume.pressed}
        }
    }
    ColumnLayout {
        id:screens
        readonly property var list:Bridge.controls.displays??[]
        readonly property bool usable:list.some(d=>d.available)
        Layout.fillWidth:true;spacing:Appearance.spacing.large
        // Modèle stable : les lignes ne sont ajoutées/retirées que si la liste d'ids change ;
        // sinon les propriétés sont mises à jour en place (aucun délégué détruit pendant un glisser).
        ListModel {id:screenModel}
        function syncModel(){
            const src=screens.usable?screens.list:[]
            let same=src.length===screenModel.count
            for(let i=0;same&&i<src.length;i++)same=screenModel.get(i).screenId===src[i].id
            if(!same){
                screenModel.clear()
                for(const d of src)screenModel.append({screenId:d.id,name:d.name||"",value:d.value??0,available:!!d.available})
                return
            }
            for(let i=0;i<src.length;i++){
                screenModel.setProperty(i,"name",src[i].name||"")
                screenModel.setProperty(i,"value",src[i].value??0)
                screenModel.setProperty(i,"available",!!src[i].available)
            }
        }
        onListChanged:syncModel()
        Component.onCompleted:syncModel()
        Repeater {
            model:screenModel
            ColumnLayout {
                id:screenRow
                required property int screenId
                required property string name
                required property real value
                required property bool available
                Layout.fillWidth:true;spacing:4
                RowLayout {
                    Layout.fillWidth:true;opacity:screenRow.available?1:.5
                    MaterialIcon{text:qsTr("brightness_6")}
                    StyledText{text:screenModel.count>1?(screenRow.name||qsTr("Écran")+" "+screenRow.screenId):qsTr("Luminosité");
                        Layout.fillWidth:true;elide:Text.ElideRight}
                    StyledText{text:screenRow.available?Math.round(screenSlider.value)+" %":"—"}
                }
                StyledSlider {
                    id:screenSlider
                    Layout.fillWidth:true;implicitHeight:32;from:0;to:100;stepSize:1;enabled:screenRow.available
                    onMoved:Bridge.action("brightness",{id:screenRow.screenId,value:value})
                    Binding {target:screenSlider;property:"value";value:screenRow.value;when:!screenSlider.pressed}
                }
                StyledText{visible:!screenRow.available;text:qsTr("Non pris en charge par cet écran");Layout.fillWidth:true;
                    wrapMode:Text.Wrap;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
            }
        }
        ColumnLayout {
            visible:!screens.usable
            Layout.fillWidth:true;spacing:4
            RowLayout {Layout.fillWidth:true;MaterialIcon{text:qsTr("brightness_6")} StyledText{text:qsTr("Luminosité");
                    Layout.fillWidth:true} StyledText{text:"—"}}
            StyledSlider {
                Layout.fillWidth:true;implicitHeight:32;from:0;to:100;stepSize:1;enabled:false
            }
        }
    }
    RowLayout {
        Layout.fillWidth:true
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Tonal;icon:"speaker";text:qsTr("Sortie audio");
            onClicked:Bridge.action("audio",{})}
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Tonal;icon:"mic";text:qsTr("Microphones");
            onClicked:Bridge.action("microphone",{})}
    }
    RowLayout {
        Layout.fillWidth:true
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Text;icon:"screenshot_monitor";text:qsTr("Capture");
            onClicked:Bridge.action("capture",{})}
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Text;icon:"content_paste";text:qsTr("Historique");
            onClicked:Bridge.action("clipboard",{})}
    }
    RowLayout {
        Layout.fillWidth:true
        MaterialIcon{text:"speed"} StyledText{text:qsTr("Effets allégés");Layout.fillWidth:true} StyledSwitch{
            objectName:"reducedEffectsToggle";checked:Bridge.reducedEffects;onClicked:Bridge.action("preference",{
                key:"reducedEffects",enabled:checked})}
    }
    RowLayout {
        Layout.fillWidth:true
        IconButton {icon:"dark_mode";type:IconButton.Tonal;toggle:true;checked:Bridge.dark;onClicked:Bridge.action("theme",{
                    dark:!Bridge.dark})}
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Text;icon:"settings";text:qsTr("Personnaliser");
            onClicked:Bridge.action("settings",{})}
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Text;icon:"construction";text:qsTr("Atelier");
            onClicked:Bridge.action("tools",{})}
    }
    RowLayout {
        Layout.fillWidth:true
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Text;icon:"desktop_windows";text:qsTr("Barre Windows");
            onClicked:Bridge.action("restoreTaskbar",{})}
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Text;icon:"power_settings_new";text:qsTr("Quitter Panneaux");
            onClicked:Bridge.action("quit",{})}
    }
}

