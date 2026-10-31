import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.config
import qs.services

// "Personnaliser": one wide panel, four category pages (tabs) that each fit without scrolling.
// A page only scrolls (inside itself) when the screen is too small for it.
Item {
 id:page
 objectName:"shellPreferences"
 property int tab:0
 readonly property var tabs:[{label:qsTr("Apparence"),icon:"palette"},{label:qsTr("Bureau et dock"),icon:"desktop_windows"},{label:qsTr("Explorateur et fichiers"),icon:"folder_open"},{label:qsTr("Au quotidien"),icon:"tune"},{label:qsTr("Réseaux sociaux"),icon:"forum"},{label:qsTr("Favoris"),icon:"bookmarks"}]
 readonly property real motion:Bridge.settings.motionScale??1
 property real maxPageHeight:0
 property bool measuring:false
 property int measured:0
 function endMeasure(){let m=0;for(let i=0;i<5;i++){const it=measurer.itemAt(i)?.item;if(it)m=Math.max(m,it.implicitHeight)}maxPageHeight=m;measuring=false}
 onMeasuredChanged:if(measured>=6)Qt.callLater(endMeasure)
 onVisibleChanged:{if(visible){maxPageHeight=0;measured=0;measuring=true}else{searchField.text="";glowOff()}}
 onTabChanged:{searchField.text="";glowOff()}

 // ---- Search: no hand-written index. The six pages are instantiated by `indexer` only while a query is typed,
 // then their SettingRow/SliderRow/Card objects (searchKind) are read; clearing the field frees them again.
 property bool searching:false
 property var results:[]
 property var pending:null
 property Item pendingItem:null
 property Item glowing:null
 function fold(s){return String(s??"").normalize("NFD").replace(/[\u0300-\u036f]/g,"").replace(/[’‘]/g,"'").toLowerCase()}
 function collect(root,out){const c=root?root.children:[];for(let i=0;i<c.length;i++){if(c[i].searchKind)out.push(c[i]);collect(c[i],out)}}
 function runSearch(){
  const q=fold(searchField.text).trim();searching=q!=="";
  if(!searching){results=[];return}
  glowOff();
  const words=q.split(/\s+/),out=[];
  for(let p=0;p<6;p++){const found=[];collect(indexer.itemAt(p)?.item,found)
   for(const o of found){const hint=o.searchKind==="card"?o.subtitle:o.hint;if(words.every(w=>fold(o.title+" "+hint).includes(w)))out.push({title:o.title,hint:hint,page:p,kind:o.searchKind})}}
  results=out}
 function glowOff(){glowing?.hideGlow();glowing=null;pendingItem=null;pending=null}
 function openResult(i){const r=results[i];if(!r)return;searchField.text="";tab=r.page;pending=r;Qt.callLater(settle)}
 // Finds the live row of the opened page; waits (through pendingItem) until the layout has placed it, then scrolls and lights it.
 function settle(){
  const r=pending;if(!r||tab!==r.page)return;
  const found=[];collect(pageLoader.item,found);
  const o=found.find(f=>f.searchKind===r.kind&&f.title===r.title);
  if(!o){pending=null;return}
  if(o.width<=0||o.height<=0){pendingItem=o;return}
  scroll.contentY=Math.max(0,Math.min(o.mapToItem(pageLoader,0,0).y-Appearance.padding.large,scroll.contentHeight-scroll.height));
  pending=null;pendingItem=null;glowing=o;o.showGlow()}
 Connections{target:page.pendingItem;function onWidthChanged(){Qt.callLater(page.settle)}function onHeightChanged(){Qt.callLater(page.settle)}function onYChanged(){Qt.callLater(page.settle)}}
 implicitHeight:Math.min(640,Bridge.screenHeight-100,mainCol.implicitHeight)
 CustomThemeEditor{id:customEditor}
 function openCustomEditor(){customEditor.openEditor()}
 ShortcutRecorder{id:recorder}
 // Created on first use (rare, owns two native windows).
 Component{id:detectionComp;DetectionGuide{}}
 property var detectionGuide:null
 function openDetectionGuide(){if(!detectionGuide)detectionGuide=detectionComp.createObject(page);detectionGuide.visible=true}
 Connections{target:Bridge;function onShellEvent(event){if(Bridge.preview&&event==="verifyRecorder")recorder.openFor("panel");else if(Bridge.preview&&event==="verifyRecorderClose")recorder.finish()}}
 // Hidden measuring pass (once per opening): the panel height follows the tallest page so it does not jump between tabs.
 Repeater{id:measurer;model:page.measuring?6:0
  Loader{required property int index;visible:false;width:page.width;active:true;sourceComponent:index===0?appearancePage:index===1?deskPage:index===2?filesPage:index===3?dailyPage:index===4?socialPage:bookmarksPage
   onLoaded:page.measured++}
 }
 Repeater{id:indexer;model:page.searching?6:0
  Loader{required property int index;visible:false;width:page.width;active:true;sourceComponent:index===0?appearancePage:index===1?deskPage:index===2?filesPage:index===3?dailyPage:index===4?socialPage:bookmarksPage}
  onItemAdded:Qt.callLater(page.runSearch)
 }
 ColumnLayout{id:mainCol;anchors.fill:parent;spacing:14
  RowLayout{Layout.fillWidth:true
   ColumnLayout{spacing:2;StyledText{text:qsTr("Personnaliser");font.pointSize:Appearance.font.size.title}StyledText{text:qsTr("Un espace qui te ressemble");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}}
   Item{Layout.fillWidth:true}
   IconButton{icon:"close";type:IconButton.Text;onClicked:Bridge.closePanel()}
  }
  StyledRect{Layout.fillWidth:true;implicitHeight:44;radius:Appearance.rounding.full;color:Colours.palette.m3surfaceContainer
   MaterialIcon{id:searchIcon;anchors.left:parent.left;anchors.leftMargin:16;anchors.verticalCenter:parent.verticalCenter;text:"search";color:Colours.palette.m3outline;font.pointSize:Appearance.font.size.large}
   StyledTextField{id:searchField;objectName:"prefsSearch";anchors.fill:parent;anchors.leftMargin:48;anchors.rightMargin:18;placeholderText:qsTr("Rechercher un réglage")
    onTextChanged:page.runSearch()
    Keys.onShortcutOverride:event=>{if(event.key===Qt.Key_Escape&&text.length>0)event.accepted=true}
    Keys.onEscapePressed:event=>{if(text!==""){text="";event.accepted=true}else event.accepted=false}
    Keys.onDownPressed:resultList.currentIndex=Math.min(resultList.count-1,resultList.currentIndex+1)
    Keys.onUpPressed:resultList.currentIndex=Math.max(0,resultList.currentIndex-1)
    Keys.onReturnPressed:page.openResult(resultList.currentIndex)
    Keys.onEnterPressed:page.openResult(resultList.currentIndex)
   }
  }
  Flickable{id:tabStrip;objectName:"prefsTabStrip";Layout.fillWidth:true;implicitHeight:44;contentWidth:tabRow.implicitWidth;contentHeight:height;flickableDirection:Flickable.HorizontalFlick;clip:true;boundsBehavior:Flickable.StopAtBounds
   // Keep the selected pill visible; wheel scrolls the strip sideways.
   function reveal(it){if(!it)return;const l=it.x,r=it.x+it.width;if(l<contentX)contentX=Math.max(0,l-Appearance.padding.normal);else if(r>contentX+width)contentX=Math.min(contentWidth-width,r-width+Appearance.padding.normal)}
   Connections{target:page;function onTabChanged(){tabStrip.reveal(tabRepeater.itemAt(page.tab))}}
   WheelHandler{acceptedDevices:PointerDevice.Mouse|PointerDevice.TouchPad;onWheel:event=>{const d=Math.abs(event.angleDelta.x)>Math.abs(event.angleDelta.y)?event.angleDelta.x:event.angleDelta.y;tabStrip.contentX=Math.max(0,Math.min(Math.max(0,tabStrip.contentWidth-tabStrip.width),tabStrip.contentX-d))}}
   Row{id:tabRow;x:Math.max(0,(tabStrip.width-implicitWidth)/2);y:2;spacing:Appearance.spacing.small
    Repeater{id:tabRepeater;model:page.tabs
     StyledRect{id:tabItem;required property var modelData;required property int index;objectName:"prefsTab"+index
      readonly property bool current:page.tab===index
      implicitWidth:tabContent.implicitWidth+2*Appearance.padding.large;implicitHeight:40;radius:Appearance.rounding.full
      color:current?Colours.palette.m3primary:Qt.alpha(Colours.palette.m3surfaceContainer,.76)
      activeFocusOnTab:true
      Keys.onPressed:event=>{if(event.key===Qt.Key_Return||event.key===Qt.Key_Enter||event.key===Qt.Key_Space){page.tab=index;event.accepted=true}else if(event.key===Qt.Key_Right&&index<page.tabs.length-1){page.tab=index+1;event.accepted=true}else if(event.key===Qt.Key_Left&&index>0){page.tab=index-1;event.accepted=true}}
      StateLayer{color:tabItem.current?Colours.palette.m3onPrimary:Colours.palette.m3onSurface;function onClicked(){page.tab=tabItem.index;tabItem.forceActiveFocus()}}
      FocusRing{target:tabItem}
      Row{id:tabContent;anchors.centerIn:parent;spacing:Appearance.spacing.small
       MaterialIcon{anchors.verticalCenter:parent.verticalCenter;text:tabItem.modelData.icon;font.pointSize:Appearance.font.size.large;color:tabItem.current?Colours.palette.m3onPrimary:Colours.palette.m3primary}
       StyledText{anchors.verticalCenter:parent.verticalCenter;text:tabItem.modelData.label;font.pointSize:Appearance.font.size.small;color:tabItem.current?Colours.palette.m3onPrimary:Colours.palette.m3onSurface}
      }
     }
    }
   }
  }
  Item{id:stage;Layout.fillWidth:true;Layout.fillHeight:true;Layout.preferredHeight:page.maxPageHeight
  Flickable{id:scroll;anchors.fill:parent;visible:!page.searching;onMovementStarted:page.glowOff();contentHeight:pageLoader.height;clip:true;boundsBehavior:Flickable.StopAtBounds
   C.ScrollBar.vertical:StyledScrollBar{flickable:scroll}
   Loader{id:pageLoader;width:scroll.width;height:item?item.implicitHeight:0;active:page.visible
    sourceComponent:page.tab===0?appearancePage:page.tab===1?deskPage:page.tab===2?filesPage:page.tab===3?dailyPage:page.tab===4?socialPage:bookmarksPage
   }
  }
   ListView{id:resultList;objectName:"prefsResults";anchors.fill:parent;visible:page.searching&&page.results.length>0;clip:true;spacing:6;model:page.results;boundsBehavior:Flickable.StopAtBounds
    C.ScrollBar.vertical:StyledScrollBar{flickable:resultList}
    Connections{target:page;function onResultsChanged(){resultList.currentIndex=0}}
    delegate:StyledRect{id:hit;required property var modelData;required property int index;objectName:"prefsResult"+index
     width:ListView.view.width;implicitHeight:hitCol.implicitHeight+2*Appearance.padding.normal;radius:Appearance.rounding.normal
     color:ListView.isCurrentItem?Qt.alpha(Colours.palette.m3primary,.14):Qt.alpha(Colours.palette.m3surfaceContainer,.76);activeFocusOnTab:true
     Keys.onPressed:event=>{if(event.key===Qt.Key_Return||event.key===Qt.Key_Enter||event.key===Qt.Key_Space){page.openResult(index);event.accepted=true}}
     StateLayer{color:Colours.palette.m3onSurface;function onClicked(){page.openResult(hit.index)}}
     FocusRing{target:hit}
     Column{id:hitCol;anchors.left:parent.left;anchors.right:parent.right;anchors.verticalCenter:parent.verticalCenter;anchors.leftMargin:Appearance.padding.large;anchors.rightMargin:Appearance.padding.large;spacing:1
      StyledText{width:parent.width;elide:Text.ElideRight;text:hit.modelData.title;font.pointSize:Appearance.font.size.normal}
      StyledText{width:parent.width;elide:Text.ElideRight;text:page.tabs[hit.modelData.page].label;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
     }
    }
   }
   EmptyState{anchors.centerIn:parent;visible:page.searching&&indexer.count===6&&page.results.length===0;title:qsTr("Aucun réglage trouvé");body:qsTr("Essaie un autre mot")}
  }
  RowLayout{Layout.fillWidth:true
   IconTextButton{text:qsTr("Rétablir la barre Windows");icon:"desktop_windows";type:IconTextButton.Text;onClicked:Bridge.action("restoreTaskbar",{})}
   Item{Layout.fillWidth:true}
   IconTextButton{text:qsTr("Tableau de bord");icon:"dashboard";type:IconTextButton.Text;onClicked:Bridge.navigate("dashboard")}
  }
 }

 // ---- Page 1: Apparence --------------------------------------------------
 Component{id:appearancePage
  GridLayout{columns:2;columnSpacing:14;rowSpacing:14;uniformCellWidths:false
   Card{Layout.preferredWidth:12;title:qsTr("Thème");subtitle:qsTr("Les couleurs de tout le shell");symbol:"palette"
    RowLayout{Layout.fillWidth:true;spacing:8
     Repeater{model:[{mode:"light",dark:false,label:qsTr("Porcelaine"),bg:"#fff8f6",fg:"#895143"},{mode:"dark",dark:true,label:qsTr("Nuit"),bg:"#101518",fg:"#afd4f0"},{mode:"custom",label:qsTr("Personnalisé"),bg:Colours.surfaceBase,fg:Colours.palette.m3primary},{mode:"auto",label:qsTr("Automatique"),bg:Colours.palette.m3surfaceContainerHigh,fg:Colours.palette.m3primary}]
      StyledRect{id:theme;required property var modelData;objectName:"theme-"+modelData.mode
       readonly property bool current:Bridge.themeMode===modelData.mode
       Layout.fillWidth:true;Layout.preferredWidth:1;implicitHeight:92;radius:Appearance.rounding.normal;color:modelData.bg
       border.width:current?2:0;border.color:Colours.palette.m3primary
       activeFocusOnTab:true
       // The action recolours the model above, which destroys this delegate: read everything before calling it.
       function choose(){const mode=modelData.mode,dark=modelData.dark??false,host=page;Bridge.action("theme",{mode:mode,dark:dark});if(mode==="custom")host.openCustomEditor()}
       Keys.onPressed:event=>{if(event.key===Qt.Key_Return||event.key===Qt.Key_Enter||event.key===Qt.Key_Space){choose();event.accepted=true}}
       StateLayer{color:theme.modelData.fg;function onClicked(){theme.forceActiveFocus();theme.choose()}}
       FocusRing{target:theme}
       Rectangle{anchors.horizontalCenter:parent.horizontalCenter;y:12;width:52;height:32;radius:Appearance.rounding.smaller;color:Qt.alpha(theme.modelData.fg,.14)
        Rectangle{x:6;y:5;width:parent.width-12;height:4;radius:Appearance.rounding.tiny/2;color:theme.modelData.fg}
        Rectangle{x:6;y:13;width:17;height:13;radius:Appearance.rounding.tiny;color:Qt.alpha(theme.modelData.fg,.55)}
        Rectangle{x:27;y:13;width:19;height:13;radius:Appearance.rounding.tiny;color:Qt.alpha(theme.modelData.fg,.25)}
       }
       StyledText{anchors.horizontalCenter:parent.horizontalCenter;y:56;width:parent.width-2;horizontalAlignment:Text.AlignHCenter;elide:Text.ElideRight;text:theme.modelData.label;color:theme.modelData.fg;font.pointSize:Appearance.font.size.smaller}
       MaterialIcon{visible:theme.current;anchors.right:parent.right;anchors.top:parent.top;anchors.margins:5;text:"check_circle";color:theme.modelData.fg;font.pointSize:Appearance.font.size.normal}
      }
     }
    }
    RowLayout{visible:Bridge.themeMode==="auto";Layout.fillWidth:true;spacing:7
     Repeater{model:Bridge.wallpaperInfo.colors??[];Rectangle{required property string modelData;width:16;height:16;radius:Appearance.rounding.smaller;color:modelData}}
     StyledText{Layout.fillWidth:true;text:qsTr("Couleurs tirées de ton fond d’écran");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
    }
    RowLayout{Layout.fillWidth:true;spacing:10
     StyledText{Layout.fillWidth:true;wrapMode:Text.WordWrap;text:Bridge.themeMode==="light"?qsTr("Porcelaine : thème clair et chaleureux, fixe, indépendant du fond d’écran."):Bridge.themeMode==="dark"?qsTr("Nuit : thème sombre et reposant, fixe, indépendant du fond d’écran."):Bridge.themeMode==="custom"?qsTr("Personnalisé : tes propres couleurs, avec option de verre transparent."):qsTr("Automatique : les couleurs suivent ton fond d’écran, la transparence du verre est réglable.");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
     IconTextButton{visible:Bridge.themeMode==="custom";text:qsTr("Modifier ma palette");icon:"palette";onClicked:page.openCustomEditor()}
    }
    SettingRow{title:qsTr("Liseré sur les applications");hint:qsTr("La couleur du thème autour des fenêtres du Bureau");symbol:"border_outer";StyledSwitch{checked:Bridge.settings.accentBorders??true;onClicked:Bridge.action("preference",{key:"accentBorders",enabled:checked})}}
    SettingRow{enabled:Bridge.settings.accentBorders??true;opacity:enabled?1:.5;title:qsTr("Seulement la fenêtre active");hint:qsTr("Le liseré suit la fenêtre au premier plan");symbol:"select_window_2";StyledSwitch{checked:Bridge.settings.activeBorderOnly??false;onClicked:Bridge.action("preference",{key:"activeBorderOnly",enabled:checked})}}
   }
   Card{Layout.preferredWidth:8;title:qsTr("Rendu");subtitle:qsTr("Transparence, animations et liseré");symbol:"blur_on"
    SliderRow{title:qsTr("Transparence du verre");hint:Bridge.themeMode==="auto"?qsTr("Plus la valeur est haute, plus le fond transparaît"):qsTr("Réglable avec le thème Automatique");value:Math.round(glass.value)+" %";dim:Bridge.themeMode!=="auto"
     StyledSlider{id:glass;objectName:"glassTransparency";Layout.fillWidth:true;implicitHeight:25;from:5;to:98;stepSize:1;enabled:Bridge.themeMode==="auto";onMoved:if(pressed)Bridge.action("preference",{key:"glassOpacity",value:1-value/100});Binding{target:glass;property:"value";value:(1-Bridge.glassOpacity)*100;when:!glass.pressed}}
    }
    SliderRow{title:qsTr("Animations");hint:qsTr("Vitesse des transitions du shell");value:""
     Row{spacing:4
      Repeater{model:[{label:qsTr("Sans"),value:0},{label:qsTr("Rapides"),value:.5},{label:"Caelestia",value:1}]
       Chip{required property var modelData;objectName:"motion-"+modelData.value;text:modelData.label;selected:(page.motion<=.01?0:page.motion<=.75?.5:1)===modelData.value;onActivated:Bridge.action("preference",{key:"motion",value:modelData.value})}
      }
     }
    }
    SettingRow{title:qsTr("Effets allégés");hint:qsTr("Rendu plus léger pour les petites configurations");symbol:"speed";StyledSwitch{checked:Bridge.reducedEffects;onClicked:Bridge.action("preference",{key:"reducedEffects",enabled:checked})}}
   }
  }
 }

 // ---- Page 2: Bureau et dock ---------------------------------------------
 Component{id:deskPage
  GridLayout{columns:2;columnSpacing:14;rowSpacing:14;uniformCellWidths:true
   Card{title:qsTr("Fenêtres et écran");subtitle:qsTr("Organisation et cadre autour du Bureau");symbol:"desktop_windows"
    SettingRow{title:qsTr("Organisation automatique");hint:qsTr("Les fenêtres se partagent chaque écran en tuiles, dans un ordre stable, avec des raccourcis clavier");symbol:"grid_view";StyledSwitch{objectName:"autoTileToggle";checked:Bridge.settings.autoTile??false;onClicked:Bridge.action("preference",{key:"autoTile",enabled:checked})}}
    // Tiling shortcuts and per-application exclusions; dimmed while tiling is off (the keys are then not registered).
    ColumnLayout{Layout.fillWidth:true;spacing:3;opacity:(Bridge.settings.autoTile??false)?1:.55
     StyledText{Layout.fillWidth:true;wrapMode:Text.WordWrap;text:qsTr("Raccourcis du tuilage");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3primary}
     StyledText{Layout.fillWidth:true;wrapMode:Text.WordWrap;text:qsTr("Actifs seulement quand l’organisation automatique est activée");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
     Repeater{model:[{keys:"Ctrl + Alt + ← ↑ → ↓",label:qsTr("Passer à la tuile voisine")},{keys:"Ctrl + Alt + Maj + ← ↑ → ↓",label:qsTr("Échanger avec la tuile voisine, y compris sur l’écran d’à côté")},{keys:"Ctrl + Alt + Maj + F",label:qsTr("Sortir la fenêtre du tuilage, ou l’y remettre")},{keys:"Ctrl + Alt + Maj + + / −",label:qsTr("Agrandir ou réduire la tuile active (20 à 80 %)")},{keys:"Ctrl + Alt + Maj + X",label:qsTr("Exclure l’application de la fenêtre active, ou la réintégrer")},{keys:qsTr("Souris"),label:qsTr("Lâcher une fenêtre sur une autre tuile les échange")}]
      RowLayout{required property var modelData;Layout.fillWidth:true;spacing:10
       StyledText{Layout.preferredWidth:190;text:modelData.keys;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3onSurface}
       StyledText{Layout.fillWidth:true;wrapMode:Text.WordWrap;text:modelData.label;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
      }
     }
    }
    ColumnLayout{Layout.fillWidth:true;spacing:2
     StyledText{Layout.fillWidth:true;wrapMode:Text.WordWrap;text:qsTr("Applications exclues");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3primary}
     StyledText{Layout.fillWidth:true;visible:(Bridge.settings.tileExclusions??[]).length===0;text:qsTr("Aucune application exclue");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
     Repeater{model:Bridge.settings.tileExclusions??[]
      RowLayout{required property var modelData;Layout.fillWidth:true;spacing:8
       StyledText{Layout.fillWidth:true;elide:Text.ElideRight;text:modelData;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3onSurface}
       IconButton{icon:"close";type:IconButton.Text;onClicked:Bridge.action("preference",{key:"tileExclusionRemove",value:modelData})}
      }
     }
    }
    SettingRow{title:qsTr("Logo Winlestia");hint:qsTr("Un repère discret dans le coin de l’écran");symbol:"branding_watermark";StyledSwitch{objectName:"desktopLogoToggle";checked:Bridge.settings.desktopLogo??false;onClicked:Bridge.action("preference",{key:"desktopLogo",enabled:checked})}}
    SettingRow{title:qsTr("Afficher l’OSD");hint:qsTr("Indicateur de volume, luminosité et micro à l’écran");symbol:"volume_up";StyledSwitch{objectName:"osdToggle";checked:Bridge.settings.osd??true;onClicked:Bridge.action("preference",{key:"osd",enabled:checked})}}
    SettingRow{title:qsTr("Masquer le dock et l’OSD en plein écran");hint:qsTr("L’OSD, le visualiseur et les animations sont coupés sur tous les écrans dès qu’un écran est en plein écran");symbol:"fullscreen";StyledSwitch{objectName:"quietFullscreenToggle";checked:Bridge.settings.quietFullscreen??true;onClicked:Bridge.action("preference",{key:"quietFullscreen",enabled:checked})}}
    SettingRow{title:qsTr("Indicateur de bureaux virtuels");hint:qsTr("Une pastille par bureau Windows dans le dock");symbol:"view_carousel";StyledSwitch{objectName:"desktopIndicatorToggle";checked:Bridge.settings.desktopIndicator??true;onClicked:Bridge.action("preference",{key:"desktopIndicator",enabled:checked})}}
    SettingRow{title:qsTr("Cadre de l’écran");hint:qsTr("Un contour arrondi coloré autour du Bureau");symbol:"select_all";StyledSwitch{checked:Bridge.frameEnabled;onClicked:Bridge.action("preference",{key:"screenFrame",enabled:checked})}}
    SliderRow{title:qsTr("Épaisseur des contours");hint:qsTr("Largeur du cadre de l’écran");value:Math.round(frameSize.value)+" px";dim:!Bridge.frameEnabled
     StyledSlider{id:frameSize;objectName:"frameThicknessSlider";Layout.fillWidth:true;implicitHeight:25;from:4;to:40;stepSize:1;enabled:Bridge.frameEnabled;onMoved:Bridge.action("preference",{key:"frameThickness",value:value});Binding{target:frameSize;property:"value";value:Bridge.frameThickness;when:!frameSize.pressed}}
    }
   }
   Card{title:qsTr("Dock et accès rapide");subtitle:qsTr("Barre des tâches et zone du haut de l’écran");symbol:"dock_to_bottom"
    SettingRow{title:qsTr("Barre des tâches Winlestia");hint:qsTr("Remplace la barre Windows par le dock");symbol:"dock_to_bottom";StyledSwitch{checked:Bridge.taskbarEnabled;onClicked:Bridge.action("preference",{key:"taskbar",enabled:checked})}}
    SliderRow{title:qsTr("Taille des icônes");hint:qsTr("Icônes du dock et de la barre des tâches");value:Math.round(iconSize.value)+" px"
     StyledSlider{id:iconSize;Layout.fillWidth:true;implicitHeight:25;from:40;to:80;stepSize:2;onMoved:Bridge.action("preference",{key:"iconSize",value:value});Binding{target:iconSize;property:"value";value:Bridge.settings.iconSize??56;when:!iconSize.pressed}}
    }
    SettingRow{title:qsTr("Zone de détection");hint:(Bridge.settings.topActivationMargin??2)+" px · "+qsTr("bande en haut de l’écran qui ouvre le panneau");symbol:"vertical_align_top";IconTextButton{objectName:"calibrateButton";text:qsTr("Calibrer");icon:"straighten";type:IconTextButton.Tonal;onClicked:page.openDetectionGuide()}}
   }
  }
 }

 // ---- Page 3: Explorateur et fichiers ------------------------------------
 Component{id:filesPage
  GridLayout{columns:2;columnSpacing:14;rowSpacing:14;uniformCellWidths:true
   Card{title:qsTr("Explorateur Windows");subtitle:qsTr("Les dossiers prennent tes couleurs");symbol:"folder_open"
    RowLayout{Layout.fillWidth:true;spacing:8
     Repeater{model:["desktop_windows","download","description","image","music_note","movie"];Rectangle{required property string modelData;Layout.fillWidth:true;Layout.preferredWidth:1;implicitHeight:40;radius:Appearance.rounding.normal;color:Qt.alpha(Colours.palette.m3primary,.1);MaterialIcon{anchors.centerIn:parent;text:parent.modelData;color:Colours.palette.m3primary;font.pointSize:Appearance.font.size.larger}}}
    }
    SettingRow{title:qsTr("Icônes et contours assortis");hint:qsTr("Dossiers personnels et sous-dossiers à tes couleurs (aperçu ci-dessus)");symbol:"palette";StyledSwitch{objectName:"explorerThemeToggle";checked:Bridge.settings.explorerTheme??false;onClicked:Bridge.action("preference",{key:"explorerTheme",enabled:checked})}}
    StyledText{Layout.fillWidth:true;wrapMode:Text.WordWrap;text:qsTr("Icônes locales adaptatives. Le contenu et les menus restent ceux de Windows ; leur transparence ne change pas.");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3onSurfaceVariant}
    StyledText{Layout.fillWidth:true;wrapMode:Text.WordWrap;text:Bridge.settings.explorerStatus??qsTr("Prêt à personnaliser");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3primary}
    Flow{Layout.fillWidth:true;spacing:8
     IconTextButton{text:qsTr("Ajouter un dossier");icon:"create_new_folder";onClicked:Bridge.action("explorerFolder",{})}
     IconTextButton{text:qsTr("Ouvrir l’Explorateur");icon:"open_in_new";onClicked:Bridge.action("explorerOpen",{})}
     IconTextButton{text:qsTr("Restaurer");icon:"settings_backup_restore";onClicked:Bridge.action("explorerRestore",{})}
    }
   }
   Card{title:qsTr("Winlestia Fichiers");subtitle:qsTr("Le gestionnaire de fichiers du shell");symbol:"folder_special"
    SettingRow{title:qsTr("Ouvrir les dossiers avec Winlestia Fichiers");hint:qsTr("Explorer reste disponible ; désactivable à tout moment");symbol:"folder_open";StyledSwitch{objectName:"filesDefaultHandlerToggle";checked:Bridge.settings.filesDefaultHandler??false;onClicked:Bridge.action("preference",{key:"filesDefaultHandler",enabled:checked})}}
    SettingRow{title:qsTr("Précharger Winlestia Fichiers");hint:qsTr("Garde une instance cachée (~70 Mo de mémoire) pour ouvrir les dossiers plus vite ; l’arrêt prend effet au prochain démarrage");symbol:"bolt";StyledSwitch{objectName:"filesPreloadToggle";checked:Bridge.settings.filesPreload??false;onClicked:Bridge.action("preference",{key:"filesPreload",enabled:checked})}}
   }
  }
 }

 // ---- Page 4: Au quotidien -----------------------------------------------
 Component{id:dailyPage
  GridLayout{columns:2;columnSpacing:14;rowSpacing:14;uniformCellWidths:true
   Card{title:qsTr("Raccourcis");subtitle:qsTr("Clique sur une touche pour la changer");symbol:"keyboard"
    SettingRow{title:qsTr("Ouvrir le panneau");hint:qsTr("Affiche ou masque le tableau de bord");symbol:"dashboard";IconTextButton{text:Bridge.settings.shortcut||"Ctrl + F1";icon:"keyboard";type:IconTextButton.Tonal;onClicked:recorder.openFor("panel")}}
    SettingRow{title:qsTr("Commande rapide");hint:qsTr("Recherche et lance tout depuis un champ");symbol:"search";IconTextButton{text:Bridge.settings.paletteShortcut||"Ctrl + F2";icon:"keyboard";type:IconTextButton.Tonal;onClicked:recorder.openFor("palette")}}
    SettingRow{title:qsTr("Sélecteur de fonds d’écran");hint:qsTr("Choisis un fond en plein écran avec aperçu");symbol:"wallpaper";IconTextButton{text:Bridge.settings.wallpaperShortcut||"Win + Alt + W";icon:"keyboard";type:IconTextButton.Tonal;onClicked:recorder.openFor("wallpaper")}}
   }
   Card{title:qsTr("Dictée vocale");subtitle:qsTr("Appuyer pour parler, le texte est saisi là où tu écris");symbol:"mic"
    SettingRow{title:qsTr("Activer la dictée");hint:qsTr("Transcription locale, sans envoyer ta voix sur Internet");symbol:"record_voice_over";StyledSwitch{objectName:"dictationToggle";checked:Bridge.settings.dictationEnabled??false;onClicked:Bridge.action("preference",{key:"dictationEnabled",enabled:checked})}}
    SettingRow{title:qsTr("Touche de dictée");hint:qsTr("Maintenez la touche pour dicter (français, anglais, espagnol, détection auto)");symbol:"keyboard_keys";enabled:Bridge.settings.dictationEnabled??false;DictationKeyButton{objectName:"dictationKey"}}
   }
   Card{title:qsTr("Démarrage et données");subtitle:qsTr("Lancement et historique");symbol:"power_settings_new"
    SettingRow{title:qsTr("Démarrer avec Windows");hint:qsTr("Winlestia s’ouvre à ta connexion");symbol:"power_settings_new";StyledSwitch{checked:Bridge.settings.autostart??false;onClicked:Bridge.action("preference",{key:"autostart",enabled:checked})}}
    SettingRow{title:qsTr("Historique du presse-papiers");hint:qsTr("Garde les éléments copiés pour les recoller");symbol:"content_paste";StyledSwitch{checked:Bridge.settings.clipboardHistory??false;onClicked:Bridge.action("preference",{key:"clipboardHistory",enabled:checked})}}
   }
   Card{Layout.columnSpan:2;title:qsTr("Langue et services");subtitle:qsTr("Langue de l’interface, météo et applications");symbol:"language"
    SettingRow{title:qsTr("Langue");hint:qsTr("Langue de l’interface de Winlestia");symbol:"translate"
     Row{spacing:4
      Repeater{model:[{mode:"auto",label:qsTr("Langue de Windows"),flag:"windows"},{mode:"fr",label:"Français",flag:"fr"},{mode:"en",label:"English",flag:"en"},{mode:"es",label:"Español",flag:"es"}]
       Chip{id:languageCard;required property var modelData;objectName:"language-"+modelData.mode;text:modelData.label;selected:Bridge.languageMode===modelData.mode;onActivated:Bridge.setLanguage(modelData.mode)
        Image{Layout.preferredWidth:21;Layout.preferredHeight:14;sourceSize.width:42;source:Bridge.localUrl(Bridge.assetRoot+"/flags/"+languageCard.modelData.flag+".svg");fillMode:Image.PreserveAspectFit}
       }
      }
     }
    }
    RowLayout{Layout.fillWidth:true;spacing:14
     SettingRow{title:qsTr("Ville de la météo");hint:qsTr("Pour la météo du tableau de bord");symbol:"partly_cloudy_day";IconTextButton{text:qsTr("Choisir");icon:"location_on";type:IconTextButton.Tonal;onClicked:Bridge.chooseCity()}}
     SettingRow{title:qsTr("Liste des applications");hint:qsTr("Relit les applications installées");symbol:"refresh";IconTextButton{text:qsTr("Actualiser");icon:"refresh";type:IconTextButton.Tonal;onClicked:Bridge.action("refreshCatalogue",{})}}
    }
   }
  }
 }

 // ---- Page 5: Réseaux sociaux --------------------------------------------
 Component{id:socialPage
  GridLayout{objectName:"socialSettings";columns:1;columnSpacing:14;rowSpacing:14
   Card{title:qsTr("Réseaux sociaux");subtitle:qsTr("Le panneau caché au bord droit de l’écran");symbol:"forum"
    SettingRow{title:qsTr("Garder les messageries actives en arrière-plan (compteurs de messages, plus de mémoire)");symbol:"memory";StyledSwitch{objectName:"socialBackgroundToggle";checked:Bridge.settings.socialBackground??false;onClicked:Bridge.action("preference",{key:"socialBackground",enabled:checked})}}
    Repeater{model:Bridge.social.networks??[]
     RowLayout{id:net;required property var modelData;objectName:"socialRow-"+modelData.id;Layout.fillWidth:true;spacing:12
      readonly property bool connected:modelData.connected??false
      readonly property bool enabledNet:modelData.enabled??false
      Image{Layout.preferredWidth:32;Layout.preferredHeight:32;sourceSize.width:64;sourceSize.height:64;fillMode:Image.PreserveAspectFit;source:Bridge.localUrl(Bridge.assetRoot+"/social/"+net.modelData.id+".svg")}
      ColumnLayout{Layout.fillWidth:true;spacing:1
       StyledText{text:net.modelData.name;font.pointSize:Appearance.font.size.normal}
       StyledText{objectName:"socialStatus-"+net.modelData.id;text:net.modelData.busy?qsTr("Chargement…"):net.connected?qsTr("Connecté"):qsTr("Non connecté");font.pointSize:Appearance.font.size.small;color:net.connected?Colours.palette.m3primary:Colours.palette.m3outline}
      }
      StyledText{text:qsTr("Afficher");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
      StyledSwitch{objectName:"socialEnable-"+net.modelData.id;checked:net.enabledNet;onClicked:Bridge.action("socialEnable",{id:net.modelData.id,enabled:checked})}
      TextButton{visible:net.enabledNet&&!net.connected;text:qsTr("Se connecter");type:TextButton.Tonal;onClicked:Bridge.action("socialLogin",{id:net.modelData.id})}
      TextButton{visible:net.connected;text:qsTr("Ouvrir");type:TextButton.Tonal;onClicked:Bridge.action("socialOpen",{id:net.modelData.id})}
      StyledText{text:qsTr("Muet");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
      StyledSwitch{checked:net.modelData.muted??false;onClicked:Bridge.action("socialMute",{id:net.modelData.id,muted:checked})}
      TextButton{text:qsTr("Déconnecter");type:TextButton.Text;onClicked:Bridge.action("socialLogout",{id:net.modelData.id})}
     }
    }
    StyledText{Layout.fillWidth:true;wrapMode:Text.WordWrap;text:qsTr("La connexion se fait toi-même dans la fenêtre du service. Snapchat Web peut être limité.");font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
   }
   Card{title:qsTr("Notifications");subtitle:qsTr("Son et sourdine");symbol:"notifications"
    SettingRow{title:qsTr("Couper toutes les notifications");hint:qsTr("Plus de son ; les compteurs continuent");symbol:"notifications_off";StyledSwitch{objectName:"socialGlobalMute";checked:Bridge.social.muted??false;onClicked:Bridge.action("socialGlobalMute",{muted:checked})}}
    SettingRow{title:qsTr("Son de notification");hint:qsTr("Joue un son à chaque nouveau message");symbol:"volume_up";StyledSwitch{objectName:"socialSound";checked:Bridge.social.sound??true;onClicked:Bridge.action("socialSound",{enabled:checked})}}
    SliderRow{title:qsTr("Volume");hint:qsTr("Volume du son de notification");value:Math.round(socialVol.value)+" %";dim:!(Bridge.social.sound??true)
     RowLayout{Layout.fillWidth:true;spacing:12
      StyledSlider{id:socialVol;objectName:"socialVolume";Layout.fillWidth:true;implicitHeight:25;from:0;to:100;stepSize:1;onPressedChanged:if(!pressed)Bridge.action("socialVolume",{value:Math.round(value)});Binding{target:socialVol;property:"value";value:Bridge.social.volume??70;when:!socialVol.pressed}}
      TextButton{text:qsTr("Tester");type:TextButton.Tonal;onClicked:Bridge.action("socialTestSound",{})}
     }
    }
   }
  }
 }

 Component{id:bookmarksPage
  GridLayout{objectName:"bookmarksSettings";columns:1;columnSpacing:14;rowSpacing:14
   Card{title:qsTr("Favoris");subtitle:qsTr("Le panneau caché au bord gauche de l’écran");symbol:"bookmarks"
    Repeater{model:(Bridge.bookmarks.browsers??[]).filter(b=>b.installed)
     RowLayout{id:br;required property var modelData;objectName:"bookmarksBrowser-"+modelData.id;Layout.fillWidth:true;spacing:12
      readonly property bool chosen:modelData.id===Bridge.bookmarks.browser
      StyledText{Layout.fillWidth:true;text:br.modelData.name+(br.modelData.isDefault?qsTr(" (navigateur par défaut)"):"");font.pointSize:Appearance.font.size.normal}
      TextButton{objectName:"bookmarksPick-"+br.modelData.id;text:br.chosen?qsTr("Utilisé"):qsTr("Utiliser");type:br.chosen?TextButton.Filled:TextButton.Tonal;onClicked:if(!br.chosen)Bridge.action("bookmarksBrowser",{id:br.modelData.id})}
     }
    }
    StyledText{objectName:"bookmarksNoBrowser";visible:(Bridge.bookmarks.browsers??[]).filter(b=>b.installed).length===0;text:qsTr("Aucun navigateur pris en charge n’a été détecté.");color:Colours.palette.m3onSurfaceVariant;font.pointSize:Appearance.font.size.small}
    SettingRow{title:qsTr("Réafficher les favoris retirés (%1)").arg(Bridge.bookmarks.hiddenCount??0);hint:qsTr("Ceux retirés de Winlestia uniquement, pas du navigateur");symbol:"visibility"
     TextButton{objectName:"bookmarksUnhideAll";enabled:(Bridge.bookmarks.hiddenCount??0)>0;text:qsTr("Réafficher");type:TextButton.Tonal;onClicked:Bridge.action("bookmarksUnhideAll",{})}
    }
   }
  }
 }

 // ---- Building blocks ----------------------------------------------------
 // Keyboard focus outline drawn over a focusable item.
 component FocusRing:Rectangle{property Item target;anchors.fill:target;anchors.margins:-2;radius:(target?.radius??0)+2;color:"transparent";border.width:2;border.color:Colours.palette.m3primary;visible:target?.activeFocus??false}
 // Rounded section card: icon + title + one-line subtitle, then its settings.
 // Short highlight used by the search: fades after a moment, or stays lit (no animation) with reduced effects until the next scroll.
 component HitGlow:Rectangle{id:g;anchors.fill:parent;anchors.margins:-6;radius:Appearance.rounding.normal;color:Qt.alpha(Colours.palette.m3primary,.16);border.width:2;border.color:Colours.palette.m3primary;opacity:0;visible:opacity>0
  function show(){fade.stop();opacity=1;if(!Bridge.reducedEffects)fade.restart()}
  function hide(){fade.stop();opacity=0}
  SequentialAnimation{id:fade;PauseAnimation{duration:1200}NumberAnimation{target:g;property:"opacity";to:0;duration:Appearance.anim.durations.normal}}
 }
 component Card:StyledRect{id:card;property string title;property string subtitle;property string symbol;default property alias content:body.data
  readonly property string searchKind:"card"
  function showGlow(){glow.show()}
  function hideGlow(){glow.hide()}
  HitGlow{id:glow}
  Layout.fillWidth:true;Layout.fillHeight:true;Layout.preferredWidth:1;implicitHeight:inner.implicitHeight+36;radius:Appearance.rounding.large;color:Qt.alpha(Colours.palette.m3surfaceContainer,.76)
  ColumnLayout{id:inner;anchors.fill:parent;anchors.margins:18;spacing:0
   RowLayout{Layout.fillWidth:true;Layout.bottomMargin:14;spacing:12
    StyledRect{implicitWidth:40;implicitHeight:40;radius:Appearance.rounding.normal;color:Qt.alpha(Colours.palette.m3primary,.14);MaterialIcon{anchors.centerIn:parent;text:card.symbol;font.pointSize:Appearance.font.size.title;color:Colours.palette.m3primary}}
    ColumnLayout{Layout.fillWidth:true;spacing:1;StyledText{text:card.title;font.pointSize:Appearance.font.size.larger}StyledText{Layout.fillWidth:true;elide:Text.ElideRight;text:card.subtitle;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}}
   }
   ColumnLayout{id:body;Layout.fillWidth:true;spacing:14}
   Item{Layout.fillHeight:true}
  }
 }
 // One setting: icon, label, short explanation, control on the right. The control gets a focus outline when tabbed to.
 component SettingRow:FocusScope{id:row;property string title;property string hint:"";property string symbol;default property alias control:ctl.data
  readonly property string searchKind:"row"
  function showGlow(){glow.show()}
  function hideGlow(){glow.hide()}
  HitGlow{id:glow}
  Layout.fillWidth:true;implicitHeight:Math.max(40,rl.implicitHeight)
  Rectangle{anchors.fill:parent;anchors.margins:-4;radius:Appearance.rounding.normal;color:"transparent";border.width:2;border.color:Colours.palette.m3primary;visible:row.activeFocus}
  RowLayout{id:rl;anchors.fill:parent;spacing:12
   MaterialIcon{text:row.symbol;color:Colours.palette.m3primary;font.pointSize:Appearance.font.size.large;Layout.preferredWidth:24;horizontalAlignment:Text.AlignHCenter}
   ColumnLayout{Layout.fillWidth:true;spacing:1;StyledText{Layout.fillWidth:true;wrapMode:Text.WordWrap;text:row.title;font.pointSize:Appearance.font.size.normal}StyledText{Layout.fillWidth:true;visible:row.hint!=="";wrapMode:Text.WordWrap;text:row.hint;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}}
   RowLayout{id:ctl;Layout.alignment:Qt.AlignVCenter}
  }
 }
 // Slider with label, live value and explanation; dimmed when the option it drives is off.
 component SliderRow:FocusScope{id:sr;property string title;property string hint;property string value;property bool dim:false;default property alias control:box.data
  readonly property string searchKind:"row"
  function showGlow(){glow.show()}
  function hideGlow(){glow.hide()}
  HitGlow{id:glow}
  Layout.fillWidth:true;implicitHeight:col.implicitHeight;opacity:dim?.5:1
  ColumnLayout{id:col;anchors.fill:parent;spacing:5
   RowLayout{Layout.fillWidth:true;StyledText{Layout.fillWidth:true;text:sr.title;font.pointSize:Appearance.font.size.normal}StyledText{text:sr.value;color:Colours.palette.m3primary;font.pointSize:Appearance.font.size.small}}
   ColumnLayout{id:box;Layout.fillWidth:true}
   StyledText{Layout.fillWidth:true;wrapMode:Text.WordWrap;text:sr.hint;font.pointSize:Appearance.font.size.small;color:Colours.palette.m3outline}
  }
 }
 // Segmented option (animations, language): selected = primary fill.
 component Chip:StyledRect{id:chip;property string text;property bool selected;default property alias extra:slotRow.data;signal activated
  implicitHeight:34;implicitWidth:slotRow.implicitWidth+(lbl.visible?lbl.implicitWidth+8:0)+24;radius:Appearance.rounding.full
  color:selected?Colours.palette.m3primary:Colours.palette.m3secondaryContainer;activeFocusOnTab:true
  Keys.onPressed:event=>{if(event.key===Qt.Key_Return||event.key===Qt.Key_Enter||event.key===Qt.Key_Space){chip.activated();event.accepted=true}}
  StateLayer{color:chip.selected?Colours.palette.m3onPrimary:Colours.palette.m3onSecondaryContainer;function onClicked(){chip.activated();chip.forceActiveFocus()}}
  FocusRing{target:chip}
  RowLayout{anchors.centerIn:parent;spacing:8;RowLayout{id:slotRow;spacing:0}StyledText{id:lbl;text:chip.text;font.pointSize:Appearance.font.size.small;color:chip.selected?Colours.palette.m3onPrimary:Colours.palette.m3onSecondaryContainer}}
 }
}
