// En-tête d'une sous-page : retour rond, titre, interrupteur facultatif de la radio.
import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config

RowLayout {
    id: root
    property string title: ""
    property bool hasSwitch: false
    property bool checked: false
    signal back
    signal switched
    spacing: Appearance.spacing.normal
    CcIconButton {
        objectName: "ccBack"
        icon: "arrow_back"
        tip: qsTr("Retour")
        onClicked: root.back()
    }
    StyledText {
        Layout.fillWidth: true
        text: root.title
        elide: Text.ElideRight
        font.pointSize: Appearance.font.size.large
    }
    StyledSwitch {
        objectName: "ccSwitch"
        visible: root.hasSwitch
        checked: root.checked
        onClicked: root.switched()
    }
}
