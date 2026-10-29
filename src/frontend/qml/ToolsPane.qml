import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.components.containers
import qs.services
import qs.config

Item {
    id:root;implicitHeight:Math.min(680,Bridge.screenHeight-90)
    property string filter:"Tout"
    readonly property var cards:[
        {category:"Écran",icon:"screenshot_monitor",name:"Capturer et annoter",description:"Sélection, annotation et export",target:"capture",nav:true},
        {category:"Écran",icon:"document_scanner",name:"Texte d’une image",description:"Reconnaissance de texte Windows",target:"ocr"},
        {category:"Écran",icon:"colorize",name:"Pipette",description:"Copier une couleur de l’écran",target:"pipette"},
        {category:"Écran",icon:"straighten",name:"Règle pixel",description:"Mesurer une distance à l’écran",target:"ruler"},
        {category:"Audio",icon:"speaker",name:"Sortie audio",description:"Casque, enceintes et périphériques",target:"audio",nav:true},
        {category:"Audio",icon:"mic",name:"Microphones",description:"Entrée, niveau et bouton muet",target:"microphone",nav:true},
        {category:"Audio",icon:"instant_mix",name:"Mixeur d’applications",description:"Un volume pour chaque application",target:"mixer",nav:true},
        {category:"Confort",icon:"content_paste",name:"Historique",description:"Retrouver les éléments copiés",target:"clipboard",nav:true},
        {category:"Confort",icon:"keep",name:"Toujours au premier plan",description:"Garder la fenêtre active devant",target:"pin"},
        {category:"Confort",icon:"sticky_note_2",name:"Papier pense-bête",description:"Écrire et épingler sur le bureau",target:"memo"},
        {category:"Confort",icon:"notifications",name:"Notifications",description:"Activer ou couper les notifications",target:"notifications"},
        {category:"Fichiers",icon:"hard_drive",name:"Gros fichiers",description:"Comprendre ce qui remplit le disque",target:"largefiles"},
        {category:"Fichiers",icon:"workspaces",name:"Profils d’espace",description:"Retrouver une disposition de travail",target:"profiles"},
        {category:"Confort",icon:"keyboard",name:"Aide et raccourcis",description:"Les gestes et touches de Panneaux",target:"help"}
    ]
    ColumnLayout{anchors.fill:parent;spacing:18
        RowLayout{Layout.fillWidth:true;spacing:12
            MaterialIcon{text:qsTr("construction");font.pointSize:Appearance.font.size.extraLarge;color:Colours.palette.m3primary}
            ColumnLayout{spacing:3;StyledText{text:qsTr("L’atelier");font.pointSize:Appearance.font.size.title} StyledText{text:qsTr("Des outils à portée de main");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}}
            Item{Layout.fillWidth:true}
            IconButton{icon:"close";type:IconButton.Text;onClicked:Bridge.closePanel()}
        }
        RowLayout{spacing:6;Repeater{model:["Tout","Écran","Audio","Fichiers","Confort"];IconTextButton{required property string modelData;text:modelData;toggle:true;checked:root.filter===modelData;type:IconTextButton.Tonal;onClicked:root.filter=modelData}} Item{Layout.fillWidth:true}}
        StyledFlickable{id:scroll;Layout.fillWidth:true;Layout.fillHeight:true;clip:true;contentWidth:width;contentHeight:cardsGrid.implicitHeight;flickableDirection:Flickable.VerticalFlick
            C.ScrollBar.vertical:StyledScrollBar{flickable:scroll}
            GridLayout{id:cardsGrid;width:scroll.width-10;columns:2;columnSpacing:12;rowSpacing:12
                Repeater{model:root.cards.filter(c=>root.filter==="Tout"||c.category===root.filter)
                    StyledRect{id:card;required property var modelData;Layout.fillWidth:true;implicitHeight:112;radius:Appearance.rounding.large;color:Colours.palette.m3surfaceContainer
                        StateLayer{function onClicked(){if(card.modelData.nav)Bridge.navigate(card.modelData.target);else Bridge.action("toolRun",{id:card.modelData.target})}}
                        RowLayout{anchors.fill:parent;anchors.margins:18;spacing:14
                            MaterialIcon{text:card.modelData.icon;font.pointSize:Appearance.font.size.extraLarge;color:Colours.palette.m3primary}
                            ColumnLayout{Layout.fillWidth:true;spacing:6;StyledText{Layout.fillWidth:true;text:card.modelData.name;font.pointSize:Appearance.font.size.normal;elide:Text.ElideRight} StyledText{Layout.fillWidth:true;text:card.modelData.description;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline;wrapMode:Text.Wrap}}
                            MaterialIcon{text:qsTr("chevron_right");font.pointSize:Appearance.font.size.larger;color:Colours.palette.m3outline}
                        }
                    }
                }
            }
        }
    }
}
