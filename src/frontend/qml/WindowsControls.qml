// Windows service bindings composed from Caelestia's original controls.
import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config

ColumnLayout {
    property bool mixerOpen:false
    signal toggleMixer()
    spacing:Appearance.spacing.large
    RowLayout {
        Layout.fillWidth:true
        StyledText {text:qsTr("Commandes rapides");font.pointSize:Appearance.font.size.large;Layout.fillWidth:true}
        IconButton {icon:"close";type:IconButton.Text;onClicked:Bridge.closePanel()}
    }
    RowLayout {
        Layout.fillWidth:true;spacing:12
        IconTextButton {Layout.fillWidth:true;icon:"wifi";text:Bridge.controls.network || "Wi-Fi";toggle:true;checked:Bridge.controls.wifiOn??false;onClicked:Bridge.action("radio",{kind:"wifi"})}
        IconTextButton {Layout.fillWidth:true;icon:"bluetooth";text:qsTr("Bluetooth");toggle:true;checked:Bridge.controls.bluetoothOn??false;onClicked:Bridge.action("radio",{kind:"bluetooth"})}
    }
    ColumnLayout {
        Layout.fillWidth:true;spacing:4
        RowLayout {Layout.fillWidth:true;MaterialIcon{text:qsTr("volume_up")} StyledText{text:qsTr("Volume");Layout.fillWidth:true} StyledText{text:Math.round(volume.value)+" %"} IconButton{objectName:"mixerToggle";icon:"chevron_right";type:IconButton.Text;rotation:mixerOpen?180:0;onClicked:toggleMixer();Behavior on rotation{Anim{duration:Appearance.anim.durations.small}}}}
        StyledSlider {
            id:volume
            Layout.fillWidth:true;implicitHeight:32;from:0;to:100;stepSize:1
            onMoved:Bridge.action("volume",{value:value})
            Binding {target:volume;property:"value";value:Bridge.controls.volume??0;when:!volume.pressed}
        }
    }
    ColumnLayout {
        Layout.fillWidth:true;spacing:4
        RowLayout {Layout.fillWidth:true;MaterialIcon{text:qsTr("brightness_6")} StyledText{text:qsTr("Luminosité");Layout.fillWidth:true} StyledText{text:Bridge.controls.brightnessAvailable?Math.round(brightness.value)+" %":"—"}}
        StyledSlider {
            id:brightness
            Layout.fillWidth:true;implicitHeight:32;from:0;to:100;stepSize:1;enabled:Bridge.controls.brightnessAvailable??false
            onMoved:Bridge.action("brightness",{value:value})
            Binding {target:brightness;property:"value";value:Bridge.controls.brightness??0;when:!brightness.pressed}
        }
    }
    RowLayout {
        Layout.fillWidth:true
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Tonal;icon:"speaker";text:qsTr("Sortie audio");onClicked:Bridge.action("audio",{})}
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Tonal;icon:"mic";text:qsTr("Microphones");onClicked:Bridge.action("microphone",{})}
    }
    RowLayout {
        Layout.fillWidth:true
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Text;icon:"screenshot_monitor";text:qsTr("Capture");onClicked:Bridge.action("capture",{})}
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Text;icon:"content_paste";text:qsTr("Historique");onClicked:Bridge.action("clipboard",{})}
    }
    RowLayout {
        Layout.fillWidth:true
        IconButton {icon:"dark_mode";type:IconButton.Tonal;toggle:true;checked:Bridge.dark;onClicked:Bridge.action("theme",{dark:!Bridge.dark})}
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Text;icon:"settings";text:qsTr("Personnaliser");onClicked:Bridge.action("settings",{})}
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Text;icon:"construction";text:qsTr("Atelier");onClicked:Bridge.action("tools",{})}
    }
    RowLayout {
        Layout.fillWidth:true
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Text;icon:"desktop_windows";text:qsTr("Barre Windows");onClicked:Bridge.action("restoreTaskbar",{})}
        IconTextButton {Layout.fillWidth:true;type:IconTextButton.Text;icon:"power_settings_new";text:qsTr("Quitter Panneaux");onClicked:Bridge.action("quit",{})}
    }
}

