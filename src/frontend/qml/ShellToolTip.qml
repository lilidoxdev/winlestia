// Info-bulle aux couleurs de la palette (même rendu que qs/components/controls/Tooltip.qml). Fenêtre séparée pour sortir des limites de la
// fenêtre du dock.
import QtQuick
import QtQuick.Controls as C
import qs.components
import qs.config
import qs.services

C.ToolTip {
    id: root
    popupType: C.Popup.Window
    leftPadding: Appearance.padding.normal
    rightPadding: Appearance.padding.normal
    topPadding: Appearance.padding.smaller
    bottomPadding: Appearance.padding.smaller
    background: Rectangle {
        color: Qt.alpha(Colours.palette.m3surfaceContainerHighest,
            .96)   // la palette est en couches de verre (alpha <= .06) pour panneaux floutés DWM ; ici pas de flou
        radius: Appearance.rounding.small
        antialiasing: true
    }
    contentItem: StyledText {
        text: root.text
        color: Colours.palette.m3onSurface
        font.pointSize: Appearance.font.size.small
    }
}
