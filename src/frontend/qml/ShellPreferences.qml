import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.config
import qs.services

Item {
 id:page
 implicitHeight:Math.min(730,Bridge.screenHeight-100)
 CustomThemeEditor{id:customEditor}
 ShortcutRecorder{id:recorder}
 DetectionGuide{id:detectionGuide}
 Connections{target:Bridge;function onShellEvent(event){if(Bridge.preview&&event==="verifyRecorder")recorder.openFor("panel");else if(Bridge.preview&&event==="verifyRecorderClose")recorder.finish()}}
 ColumnLayout{anchors.fill:parent;spacing:18
  RowLayout{Layout.fillWidth:true
   ColumnLayout{spacing:4;StyledText{text:qsTr("Personnaliser");font.pointSize:25}StyledText{text:qsTr("Un espace qui te ressemble");font.pointSize:10;color:Colours.palette.m3outline}}
   Item{Layout.fillWidth:true}
   IconButton{icon:"close";type:IconButton.Text;onClicked:Bridge.closePanel()}
  }
  Flickable{id:scroll;Layout.fillWidth:true;Layout.fillHeight:true;contentHeight:body.implicitHeight;clip:true;boundsBehavior:Flickable.StopAtBounds
   C.ScrollBar.vertical:StyledScrollBar{flickable:scroll}
   ColumnLayout{id:body;width:scroll.width;spacing:14
    StyledRect{Layout.fillWidth:true;implicitHeight:look.implicitHeight+36;radius:26;color:Qt.alpha(Colours.palette.m3surfaceContainer,.76)
     ColumnLayout{id:look;anchors.fill:parent;anchors.margins:18;spacing:16
      SectionTitle{title:qsTr("Apparence");subtitle:qsTr("Couleurs et transparence");symbol:"palette"}
      RowLayout{Layout.fillWidth:true;spacing:8
       Repeater{model:[{mode:"light",dark:false,label:qsTr("Porcelaine"),bg:"#fff8f6",fg:"#895143",icon:"light_mode"},{mode:"dark",dark:true,label:qsTr("Nuit"),bg:"#101518",fg:"#afd4f0",icon:"dark_mode"},{mode:"custom",label:qsTr("Personnalisé"),bg:Colours.surfaceBase,fg:Colours.palette.m3primary,icon:"palette"},{mode:"auto",label:qsTr("Automatique"),bg:Colours.palette.m3surfaceContainerHigh,fg:Colours.palette.m3primary,icon:"wallpaper"}]
        StyledRect{id:theme;required property var modelData;Layout.fillWidth:true;implicitHeight:103;radius:20;color:modelData.bg;border.width:Bridge.themeMode===modelData.mode?2:0;border.color:Colours.palette.m3primary
         StateLayer{function onClicked(){Bridge.action("theme",{mode:theme.modelData.mode,dark:theme.modelData.dark??false});if(theme.modelData.mode==="custom")customEditor.openEditor()}}
         Rectangle{anchors.horizontalCenter:parent.horizontalCenter;y:15;width:56;height:34;radius:8;color:Qt.alpha(theme.modelData.fg,.14)
          Rectangle{x:6;y:5;width:parent.width-12;height:4;radius:2;color:theme.modelData.fg}
          Rectangle{x:6;y:14;width:18;height:14;radius:4;color:Qt.alpha(theme.modelData.fg,.55)}
          Rectangle{x:28;y:14;width:22;height:14;radius:4;color:Qt.alpha(theme.modelData.fg,.25)}
         }
         Row{anchors.horizontalCenter:parent.horizontalCenter;y:65;spacing:6;MaterialIcon{text:theme.modelData.icon;color:theme.modelData.fg;font.pointSize:15}StyledText{text:theme.modelData.label;color:theme.modelData.fg;font.pointSize:10}}
        }
       }
      }
      IconTextButton{visible:Bridge.themeMode==="custom";text:qsTr("Modifier ma palette");icon:"palette";onClicked:customEditor.openEditor()}
      RowLayout{visible:Bridge.themeMode==="auto";Layout.fillWidth:true;spacing:7
       Repeater{model:Bridge.wallpaperInfo.colors??[];Rectangle{required property string modelData;width:16;height:16;radius:8;color:modelData}}
       StyledText{Layout.fillWidth:true;text:qsTr("Palette du fond d’écran");font.pointSize:9;color:Colours.palette.m3outline}
      }
      ColumnLayout{visible:Bridge.themeMode==="auto";Layout.fillWidth:true;spacing:7
       ValueLabel{title:qsTr("Transparence du verre");value:Math.round(glass.value)+" %"}
       StyledSlider{id:glass;objectName:"glassTransparency";Layout.fillWidth:true;implicitHeight:25;from:5;to:98;stepSize:1;onMoved:if(pressed)Bridge.action("preference",{key:"glassOpacity",value:1-value/100});Binding{target:glass;property:"value";value:(1-Bridge.glassOpacity)*100;when:!glass.pressed}}
      }
      SettingRow{title:qsTr("Effets allégés");hint:qsTr("Un rendu plus léger pour les petites configurations");symbol:"speed";StyledSwitch{checked:Bridge.reducedEffects;onClicked:Bridge.action("preference",{key:"reducedEffects",enabled:checked})}}
     }
    }
    StyledRect{Layout.fillWidth:true;implicitHeight:desktop.implicitHeight+36;radius:26;color:Qt.alpha(Colours.palette.m3surfaceContainer,.76)
     ColumnLayout{id:desktop;anchors.fill:parent;anchors.margins:18;spacing:16
      SectionTitle{title:qsTr("Bureau");subtitle:qsTr("Cadre, dock et accès rapide");symbol:"desktop_windows"}
      SettingRow{title:qsTr("Organisation automatique");hint:qsTr("Les fenêtres se partagent chaque écran");symbol:"grid_view";StyledSwitch{objectName:"autoTileToggle";checked:Bridge.settings.autoTile??false;onClicked:Bridge.action("preference",{key:"autoTile",enabled:checked})}}
      SettingRow{title:qsTr("Logo Winlestia");hint:qsTr("Un repère discret dans le coin de l’écran");symbol:"branding_watermark";StyledSwitch{objectName:"desktopLogoToggle";checked:Bridge.settings.desktopLogo??false;onClicked:Bridge.action("preference",{key:"desktopLogo",enabled:checked})}}
      SettingRow{title:qsTr("Cadre de l’écran");symbol:"select_all";StyledSwitch{checked:Bridge.frameEnabled;onClicked:Bridge.action("preference",{key:"screenFrame",enabled:checked})}}
      ColumnLayout{visible:Bridge.frameEnabled;Layout.fillWidth:true;spacing:7
       ValueLabel{title:qsTr("Épaisseur des contours");value:Math.round(frameSize.value)+" px"}
       StyledSlider{id:frameSize;objectName:"frameThicknessSlider";Layout.fillWidth:true;implicitHeight:25;from:4;to:40;stepSize:1;onMoved:Bridge.action("preference",{key:"frameThickness",value:value});Binding{target:frameSize;property:"value";value:Bridge.frameThickness;when:!frameSize.pressed}}
      }
      StyledRect{Layout.fillWidth:true;implicitHeight:80;radius:18;color:Qt.alpha(Colours.palette.m3primary,.09)
       RowLayout{anchors.fill:parent;anchors.margins:14;spacing:12
        MaterialIcon{text:"vertical_align_top";font.pointSize:24;color:Colours.palette.m3primary}
        ColumnLayout{Layout.fillWidth:true;spacing:3;StyledText{text:qsTr("Zone de détection");font.pointSize:11}StyledText{text:(Bridge.settings.topActivationMargin??2)+" px · "+qsTr("Ajuster avec le repère à l’écran");font.pointSize:9;color:Colours.palette.m3outline}}
        IconTextButton{objectName:"calibrateButton";text:qsTr("Calibrer");icon:"straighten";onClicked:detectionGuide.visible=true}
       }
      }
      SettingRow{title:qsTr("Barre des tâches Panneaux");symbol:"dock_to_bottom";StyledSwitch{checked:Bridge.taskbarEnabled;onClicked:Bridge.action("preference",{key:"taskbar",enabled:checked})}}
      ValueLabel{title:qsTr("Taille des icônes");value:Math.round(iconSize.value)+" px"}
      StyledSlider{id:iconSize;Layout.fillWidth:true;implicitHeight:25;from:40;to:80;stepSize:2;onMoved:Bridge.action("preference",{key:"iconSize",value:value});Binding{target:iconSize;property:"value";value:Bridge.settings.iconSize??56;when:!iconSize.pressed}}
     }
    }
    StyledRect{Layout.fillWidth:true;implicitHeight:daily.implicitHeight+36;radius:26;color:Qt.alpha(Colours.palette.m3surfaceContainer,.76)
     ColumnLayout{id:daily;anchors.fill:parent;anchors.margins:18;spacing:16
      SectionTitle{title:qsTr("Au quotidien");subtitle:qsTr("Langue et raccourcis");symbol:"tune"}
      RowLayout{Layout.fillWidth:true;StyledText{text:qsTr("Langue");Layout.fillWidth:true}
       Row{spacing:4;Repeater{model:[{mode:"auto",label:qsTr("Langue de Windows"),flag:"windows"},{mode:"fr",label:"Français",flag:"fr"},{mode:"en",label:"English",flag:"en"},{mode:"es",label:"Español",flag:"es"}]
        StyledRect{id:languageCard;required property var modelData;objectName:"language-"+modelData.mode;width:48;height:38;radius:12;color:Bridge.languageMode===modelData.mode?Colours.palette.m3secondaryContainer:"transparent"
         Image{anchors.centerIn:parent;width:25;height:17;source:Bridge.localUrl(Bridge.assetRoot+"/flags/"+languageCard.modelData.flag+".svg");fillMode:Image.PreserveAspectFit}
         MouseArea{id:languageMouse;anchors.fill:parent;hoverEnabled:true;onClicked:Bridge.setLanguage(languageCard.modelData.mode)}
         C.ToolTip.visible:languageMouse.containsMouse;C.ToolTip.text:modelData.label;C.ToolTip.delay:300
        }
       }}
      }
      SettingRow{title:qsTr("Ouvrir le panneau");symbol:"dashboard";IconTextButton{text:Bridge.settings.shortcut||"Ctrl + F1";icon:"keyboard";type:IconTextButton.Tonal;onClicked:recorder.openFor("panel")}}
      SettingRow{title:qsTr("Commande rapide");symbol:"search";IconTextButton{text:Bridge.settings.paletteShortcut||"Ctrl + F2";icon:"keyboard";type:IconTextButton.Tonal;onClicked:recorder.openFor("palette")}}
      SettingRow{title:qsTr("Démarrer avec Windows");symbol:"power_settings_new";StyledSwitch{checked:Bridge.settings.autostart??false;onClicked:Bridge.action("preference",{key:"autostart",enabled:checked})}}
      SettingRow{title:qsTr("Historique du presse-papiers");symbol:"content_paste";StyledSwitch{checked:Bridge.settings.clipboardHistory??false;onClicked:Bridge.action("preference",{key:"clipboardHistory",enabled:checked})}}
      RowLayout{Layout.fillWidth:true;IconTextButton{text:qsTr("Choisir ma ville météo");icon:"partly_cloudy_day";type:IconTextButton.Text;onClicked:Bridge.chooseCity()}Item{Layout.fillWidth:true}IconButton{icon:"refresh";type:IconButton.Text;onClicked:Bridge.action("refreshCatalogue",{})}}
     }
    }
   }
  }
  RowLayout{Layout.fillWidth:true;IconTextButton{text:qsTr("Barre Windows");icon:"desktop_windows";type:IconTextButton.Text;onClicked:Bridge.action("restoreTaskbar",{})}Item{Layout.fillWidth:true}IconTextButton{text:qsTr("Tableau de bord");icon:"dashboard";type:IconTextButton.Text;onClicked:Bridge.navigate("dashboard")}}
 }
 component SectionTitle:RowLayout{property string title;property string subtitle;property string symbol;Layout.fillWidth:true;spacing:10;MaterialIcon{text:parent.symbol;font.pointSize:23;color:Colours.palette.m3primary}ColumnLayout{Layout.fillWidth:true;spacing:2;StyledText{text:parent.parent.title;font.pointSize:15}StyledText{text:parent.parent.subtitle;font.pointSize:9;color:Colours.palette.m3outline}}}
 component ValueLabel:RowLayout{property string title;property string value;Layout.fillWidth:true;StyledText{Layout.fillWidth:true;text:parent.title;font.pointSize:10}StyledText{text:parent.value;color:Colours.palette.m3primary;font.pointSize:10}}
 component SettingRow:RowLayout{property string title;property string hint:"";property string symbol;Layout.fillWidth:true;spacing:10;MaterialIcon{text:parent.symbol;color:Colours.palette.m3primary;font.pointSize:19}ColumnLayout{Layout.fillWidth:true;spacing:2;StyledText{text:parent.parent.title;font.pointSize:10}StyledText{visible:parent.parent.hint!=="";text:parent.parent.hint;font.pointSize:8;color:Colours.palette.m3outline}}}
}
