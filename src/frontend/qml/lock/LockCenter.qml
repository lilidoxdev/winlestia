import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config

ColumnLayout {
    id: root

    required property real centerScale

    function focusInput(): void {
        password.focusInput();
    }

    spacing: Appearance.spacing.large

    LockClock {
        Layout.alignment: Qt.AlignHCenter
        Layout.topMargin: Appearance.padding.large
        centerScale: root.centerScale
    }

    StyledText {
        Layout.alignment: Qt.AlignHCenter
        text: Time.format("dddd • d MMM").toUpperCase()
        color: Colours.palette.m3onSurface
        font.pointSize: Appearance.font.size.large
        font.weight: Font.DemiBold
    }

    LockAvatar {
        Layout.alignment: Qt.AlignHCenter
        Layout.topMargin: Appearance.spacing.large * 2 * root.centerScale
        Layout.bottomMargin: Appearance.spacing.large * root.centerScale
        side: 300 * root.centerScale
    }

    LockPassword {
        id: password

        Layout.alignment: Qt.AlignHCenter
        Layout.preferredWidth: root.width * 0.85
        centerScale: Math.max(0.8, root.centerScale)
        blocked: message.waiting
    }

    LockMessage {
        id: message

        Layout.fillWidth: true
    }
}
