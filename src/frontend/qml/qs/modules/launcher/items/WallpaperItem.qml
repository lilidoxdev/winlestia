import qs.components
import qs.services
import qs.config
import QtQuick

Item {
    id: root

    required property var modelData
    required property var visibilities

    scale: 0.5
    opacity: 0
    z: PathView.z ?? 0

    Component.onCompleted: {
        scale = Qt.binding(() => PathView.isCurrentItem ? 1 : PathView.onPath ? 0.8 : 0);
        opacity = Qt.binding(() => PathView.onPath ? 1 : 0);
    }

    implicitWidth: image.width + Appearance.padding.normal * 2
    implicitHeight: image.height + label.height + Appearance.spacing.small + Appearance.padding.large + Appearance.padding.normal

    StateLayer {
        radius: Appearance.rounding.normal

        function onClicked(): void {
            Bridge.executeResult(root.modelData);
            root.visibilities.launcher = false;
        }
    }

    StyledClippingRect {
        id: image

        anchors.horizontalCenter: parent.horizontalCenter
        y: Appearance.padding.large
        color: Colours.layer(Colours.palette.m3surfaceContainer, 2)
        radius: Appearance.rounding.normal

        implicitWidth: Config.launcher.sizes.wallpaperWidth * 0.8
        implicitHeight: implicitWidth / 16 * 9

        MaterialIcon {
            anchors.centerIn: parent
            text: "image"
            color: Colours.palette.m3outline
            font.pointSize: Appearance.font.size.extraLarge
        }

        Image {
            anchors.fill: parent
            source: root.modelData.url
            asynchronous: true
            cache: false
            fillMode: Image.PreserveAspectCrop
            sourceSize.width: Config.launcher.sizes.wallpaperWidth
            sourceSize.height: Math.round(Config.launcher.sizes.wallpaperWidth / 16 * 9)
        }
    }

    StyledText {
        id: label

        anchors.top: image.bottom
        anchors.topMargin: Appearance.spacing.small
        anchors.horizontalCenter: parent.horizontalCenter

        width: image.width - Appearance.padding.normal * 2
        horizontalAlignment: Text.AlignHCenter
        elide: Text.ElideRight
        text: root.modelData.name
        font.pointSize: Appearance.font.size.smaller
    }

    Behavior on scale {
        enabled: !Appearance.reducedEffects

        Anim {}
    }

    Behavior on opacity {
        enabled: !Appearance.reducedEffects

        Anim {}
    }
}
