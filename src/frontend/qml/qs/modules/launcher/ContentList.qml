// Windows model adapter; original Caelestia Content, AppList, AppItem and controls render the launcher.
import QtQuick
import qs.components
import qs.components.controls
import qs.services
import qs.config
Item {
    id:root
    required property var content
    required property var visibilities
    required property var panels
    required property real maxHeight
    required property StyledTextField search
    required property int padding
    required property int rounding
    readonly property bool showWallpapers:search.text.startsWith(`${Config.launcher.actionPrefix}wallpaper `)
    readonly property bool showEmoji:search.text.startsWith(`${Config.launcher.actionPrefix}emoji `)
    readonly property var currentList:showWallpapers?wallpaperLoader.item:showEmoji?emojiLoader.item:list
    readonly property real wallpaperWidth:Math.min(Math.max(Config.launcher.sizes.itemWidth*1.2,wallpaperLoader.item?wallpaperLoader.item.implicitWidth:0),Math.max(0,content.width-padding*2))
    anchors.horizontalCenter:parent.horizontalCenter
    anchors.bottom:parent.bottom
    implicitWidth:showWallpapers?wallpaperWidth:Config.launcher.sizes.itemWidth
    implicitHeight:showWallpapers?Config.launcher.sizes.wallpaperHeight:showEmoji?Math.min(maxHeight,300):Math.min(maxHeight,Math.max(70,list.implicitHeight))
    clip:true
    AppList {id:list;anchors.fill:parent;visible:!root.showWallpapers&&!root.showEmoji;search:parent.search;visibilities:parent.visibilities}
    Loader {
        id:wallpaperLoader
        asynchronous:true
        active:root.showWallpapers
        anchors.top:parent.top
        anchors.bottom:parent.bottom
        anchors.horizontalCenter:parent.horizontalCenter
        sourceComponent:WallpaperList {search:root.search;visibilities:root.visibilities;availableWidth:Math.max(0,Math.min(1240,Bridge.screenWidth-220))}
    }
    Loader {
        id:emojiLoader
        active:root.showEmoji
        anchors.fill:parent
        sourceComponent:EmojiList {search:root.search;visibilities:root.visibilities}
    }
    Row {
        anchors.centerIn:parent
        spacing:Appearance.spacing.normal
        visible:root.showEmoji&&!!emojiLoader.item&&emojiLoader.item.count===0
        MaterialIcon {anchors.verticalCenter:parent.verticalCenter;text:"mood_bad";color:Colours.palette.m3onSurfaceVariant;font.pointSize:Appearance.font.size.extraLarge}
        StyledText {anchors.verticalCenter:parent.verticalCenter;text:qsTr("Aucun emoji trouvé");color:Colours.palette.m3onSurfaceVariant;font.pointSize:Appearance.font.size.larger;font.weight:Font.Medium}
    }
    Row {
        anchors.centerIn:parent
        spacing:Appearance.spacing.normal
        visible:root.showWallpapers&&!!wallpaperLoader.item&&wallpaperLoader.item.count===0
        MaterialIcon {anchors.verticalCenter:parent.verticalCenter;text:"wallpaper_slideshow";color:Colours.palette.m3onSurfaceVariant;font.pointSize:Appearance.font.size.extraLarge}
        Column {
            anchors.verticalCenter:parent.verticalCenter
            StyledText {text:qsTr("Aucun fond d’écran trouvé");color:Colours.palette.m3onSurfaceVariant;font.pointSize:Appearance.font.size.larger;font.weight:Font.Medium}
            StyledText {text:qsTr("Ajoute des images dans ton dossier Images");color:Colours.palette.m3onSurfaceVariant;font.pointSize:Appearance.font.size.small}
        }
    }
    Behavior on implicitWidth {enabled:!Appearance.reducedEffects&&root.visibilities.launcher;Anim {}}
    Behavior on implicitHeight {enabled:!Appearance.reducedEffects&&root.visibilities.launcher;Anim {}}
}
