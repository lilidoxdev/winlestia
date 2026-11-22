import qs.components
import qs.components.effects
import qs.services
import qs.config
import QtQuick
import QtQuick.Layouts

ColumnLayout {
    id: root

    required property var deviceDetails

    spacing: Appearance.spacing.small / 2

    StyledText {
        text: qsTr("Adresse IP")
    }

    StyledText {
        text: root.deviceDetails?.ipAddress || qsTr("Non disponible")
        color: Colours.palette.m3outline
        font.pointSize: Appearance.font.size.small
    }

    StyledText {
        Layout.topMargin: Appearance.spacing.normal
        text: qsTr("Masque de sous-réseau")
    }

    StyledText {
        text: root.deviceDetails?.subnet || qsTr("Non disponible")
        color: Colours.palette.m3outline
        font.pointSize: Appearance.font.size.small
    }

    StyledText {
        Layout.topMargin: Appearance.spacing.normal
        text: qsTr("Passerelle")
    }

    StyledText {
        text: root.deviceDetails?.gateway || qsTr("Non disponible")
        color: Colours.palette.m3outline
        font.pointSize: Appearance.font.size.small
    }

    StyledText {
        Layout.topMargin: Appearance.spacing.normal
        text: qsTr("Serveurs DNS")
    }

    StyledText {
        text: (root.deviceDetails && root.deviceDetails.dns && root.deviceDetails.dns.length > 0) ? root.deviceDetails.dns.join(", ")
            : qsTr("Non disponible")
        color: Colours.palette.m3outline
        font.pointSize: Appearance.font.size.small
        wrapMode: Text.Wrap
        Layout.maximumWidth: parent.width
    }
}
