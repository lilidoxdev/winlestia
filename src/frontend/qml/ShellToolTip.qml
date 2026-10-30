// Palette-styled tooltip (same look as qs/components/controls/Tooltip.qml). Separate window so it escapes the dock window bounds.
import QtQuick
import QtQuick.Controls as C
import qs.components
import qs.config
import qs.services

C.ToolTip {
    id:root
    popupType:C.Popup.Window
    leftPadding:Appearance.padding.normal;rightPadding:Appearance.padding.normal
    topPadding:Appearance.padding.smaller;bottomPadding:Appearance.padding.smaller
    background:Rectangle {
        color:Qt.alpha(Colours.palette.m3surfaceContainerHighest,.96)   // palette values are glass layers (alpha <= .06) meant for DWM-blurred panels; this window has no blur
        radius:Appearance.rounding.small
        antialiasing:true
    }
    contentItem:StyledText {
        text:root.text
        color:Colours.palette.m3onSurface
        font.pointSize:Appearance.font.size.small
    }
}
