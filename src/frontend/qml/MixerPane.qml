// Native Windows audio sessions, rendered with upstream Caelestia controls.
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.components.containers
import qs.services
import qs.config

Item {
    id:root
    property bool standalone:false
    signal dismiss()
    implicitHeight:Math.min(610,Bridge.screenHeight-100)
    ColumnLayout {
        anchors.fill:parent;spacing:18
        RowLayout {
            Layout.fillWidth:true;spacing:12
            MaterialIcon{text:qsTr("instant_mix");font.pointSize:Appearance.font.size.title;color:Colours.palette.m3primary}
            ColumnLayout {Layout.fillWidth:true;spacing:3
                StyledText{text:qsTr("Mixeur d’applications");font.pointSize:Appearance.font.size.large}
                StyledText{text:qsTr("Chaque application, à son niveau");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
            }
            IconButton{icon:root.standalone?"close":"chevron_left";type:IconButton.Text;onClicked:root.dismiss()}
        }
        StyledRect {
            Layout.fillWidth:true;implicitHeight:78;radius:Appearance.rounding.large;color:Colours.palette.m3secondaryContainer
            RowLayout {anchors.fill:parent;anchors.margins:14;spacing:10
                IconButton{icon:Bridge.controls.muted?"volume_off":"volume_up";type:IconButton.Text;onClicked:Bridge.action("masterMute",{muted:!Bridge.controls.muted})}
                ColumnLayout {Layout.fillWidth:true;spacing:2
                    StyledText{text:qsTr("Volume général");font.pointSize:Appearance.font.size.normal}
                    StyledSlider {id:master;Layout.fillWidth:true;implicitHeight:28;from:0;to:100;stepSize:1;onMoved:Bridge.action("volume",{value:value});Binding{target:master;property:"value";value:Bridge.controls.volume??0;when:!master.pressed}}
                }
                StyledText{text:Math.round(master.value)+" %";font.pointSize:Appearance.font.size.normal}
            }
        }
        StyledListView {
            id:list;objectName:"audioSessions";Layout.fillWidth:true;Layout.fillHeight:true;spacing:10;clip:true;reuseItems:false
            model:Bridge.audioApplications
            delegate:StyledRect {
                id:row;required property var modelData;width:list.width;implicitHeight:100;radius:Appearance.rounding.large;color:Colours.palette.m3surfaceContainer
                ColumnLayout {anchors.fill:parent;anchors.margins:14;spacing:5
                    RowLayout{Layout.fillWidth:true;spacing:10
                        Image{Layout.preferredWidth:28;Layout.preferredHeight:28;source:Bridge.iconUrl(row.modelData.id,Bridge.iconRevision);asynchronous:true;fillMode:Image.PreserveAspectFit;visible:status===Image.Ready}
                        MaterialIcon{text:row.modelData.pid===0?"notifications":"music_note";visible:!Bridge.iconUrl(row.modelData.id,Bridge.iconRevision);color:Colours.palette.m3primary}
                        StyledText{text:row.modelData.name;Layout.fillWidth:true;elide:Text.ElideRight;font.pointSize:Appearance.font.size.normal}
                        IconButton{icon:row.modelData.muted?"volume_off":"volume_up";type:IconButton.Text;onClicked:Bridge.action("sessionMute",{id:row.modelData.id,muted:!row.modelData.muted})}
                    }
                    RowLayout{Layout.fillWidth:true;spacing:14
                        StyledSlider{id:gain;Layout.fillWidth:true;implicitHeight:28;from:0;to:100;stepSize:1;onMoved:Bridge.action("sessionVolume",{id:row.modelData.id,value:value});Binding{target:gain;property:"value";value:row.modelData.volume;when:!gain.pressed}}
                        StyledText{text:Math.round(gain.value)+" %";Layout.preferredWidth:42;horizontalAlignment:Text.AlignRight;font.pointSize:Appearance.font.size.small}
                    }
                }
            }
            C.ScrollBar.vertical:StyledScrollBar{flickable:list}
            EmptyState{anchors.centerIn:parent;width:parent.width-20;visible:list.count===0;icon:"music_off";title:!Bridge.audioReady?"Recherche des sessions audio…":"Aucune application ne joue de son";body:Bridge.audioReady?"Lance une musique, une vidéo ou un jeu.
Ses commandes audio apparaîtront ici.":""}
        }
        StyledText{Layout.fillWidth:true;text:qsTr("Réglages Windows en direct · volume conservé par Windows");wrapMode:Text.Wrap;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
    }
}
