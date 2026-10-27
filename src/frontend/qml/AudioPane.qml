import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.components.containers
import qs.services
import qs.config

Item {
    id:root;property bool microphone:false
    implicitHeight:Math.min(640,Bridge.screenHeight-90)
    ColumnLayout{anchors.fill:parent;spacing:18
        RowLayout{Layout.fillWidth:true;spacing:12
            MaterialIcon{text:root.microphone?"mic":"speaker";font.pointSize:27;color:Colours.palette.m3primary}
            ColumnLayout{spacing:3
                StyledText{text:root.microphone?"Microphones":"Sortie audio";font.pointSize:22}
                StyledText{text:root.microphone?"Choisis ton entrée et règle sa sensibilité":"Choisis où écouter tes applications";font.pointSize:10;color:Colours.palette.m3outline}
            }
            Item{Layout.fillWidth:true}
            IconButton{icon:"close";type:IconButton.Text;onClicked:Bridge.closePanel()}
        }
        RowLayout{Layout.fillWidth:true
            IconTextButton{text:qsTr("Sorties");icon:"headphones";toggle:true;checked:!root.microphone;onClicked:Bridge.navigate("audio")}
            IconTextButton{text:qsTr("Microphones");icon:"mic";toggle:true;checked:root.microphone;onClicked:Bridge.navigate("microphone")}
            Item{Layout.fillWidth:true}
            IconTextButton{text:qsTr("Mixeur");icon:"instant_mix";type:IconTextButton.Text;onClicked:Bridge.navigate("mixer")}
        }
        StyledListView{
            id:list;Layout.fillWidth:true;Layout.fillHeight:true;clip:true;spacing:10;model:root.microphone?Bridge.audioInputs:Bridge.audioOutputs
            delegate:StyledRect{
                id:row;required property var modelData;width:list.width;implicitHeight:126;radius:24;color:row.modelData.selected?Colours.palette.m3secondaryContainer:Colours.palette.m3surfaceContainer
                ColumnLayout{anchors.fill:parent;anchors.margins:16;spacing:10
                    RowLayout{Layout.fillWidth:true;spacing:12
                        IconButton{icon:row.modelData.selected?"check_circle":root.microphone?"mic":"speaker";type:IconButton.Text;onClicked:Bridge.action("deviceSelect",{id:row.modelData.id})}
                        ColumnLayout{Layout.fillWidth:true;spacing:4
                            StyledText{text:row.modelData.name;Layout.fillWidth:true;elide:Text.ElideRight;font.pointSize:12}
                            StyledText{text:row.modelData.selected?"Périphérique par défaut":"Cliquer pour utiliser";font.pointSize:9;color:Colours.palette.m3outline}
                        }
                        IconTextButton{text:row.modelData.selected?"Actuel":"Choisir";type:IconTextButton.Text;onClicked:Bridge.action("deviceSelect",{id:row.modelData.id})}
                    }
                    RowLayout{Layout.fillWidth:true;spacing:12
                        IconButton{icon:row.modelData.muted?(root.microphone?"mic_off":"volume_off"):(root.microphone?"mic":"volume_up");type:IconButton.Text;onClicked:Bridge.action("deviceMute",{id:row.modelData.id,muted:!row.modelData.muted})}
                        StyledSlider{id:gain;Layout.fillWidth:true;implicitHeight:30;from:0;to:100;stepSize:1;onMoved:Bridge.action("deviceVolume",{id:row.modelData.id,value:value});Binding{target:gain;property:"value";value:row.modelData.volume;when:!gain.pressed}}
                        StyledText{text:Math.round(gain.value)+" %";Layout.preferredWidth:43;font.pointSize:10}
                    }
                }
            }
            C.ScrollBar.vertical:StyledScrollBar{flickable:list}
            StyledText{anchors.centerIn:parent;width:parent.width;horizontalAlignment:Text.AlignHCenter;wrapMode:Text.Wrap;visible:list.count===0;text:!Bridge.audioReady?"Recherche des périphériques…":root.microphone?"Aucun microphone connecté":"Aucune sortie audio connectée";color:Colours.palette.m3outline}
        }
        RowLayout{Layout.fillWidth:true
            StyledText{text:root.microphone?"Aucun enregistrement · réglage du niveau d’entrée uniquement":"Le changement s’applique aux nouvelles lectures Windows";Layout.fillWidth:true;wrapMode:Text.Wrap;font.pointSize:9;color:Colours.palette.m3outline}
            IconTextButton{text:qsTr("Retour");icon:"arrow_back";type:IconTextButton.Text;onClicked:Bridge.navigate("controls")}
        }
    }
}
