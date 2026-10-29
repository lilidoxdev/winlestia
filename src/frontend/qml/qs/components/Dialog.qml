import qs.services
import qs.config
import QtQuick
import QtQuick.Layouts

// Scrim + surfaceContainerHigh card. Anchors itself over its parent; Escape or a click on the scrim closes it.
Item {
    id: root

    property bool open
    property string title
    property real cardWidth: 400
    property bool dismissOnScrim: true
    default property alias content: body.data
    property alias actions: actionRow.data

    signal closed

    function close(): void {
        if (!open)
            return;
        open = false;
        closed();
    }

    anchors.fill: parent
    z: 100
    visible: opacity > 0
    opacity: open ? 1 : 0
    focus: open

    onOpenChanged: if (open)
        forceActiveFocus()

    // Window-level shortcut so it wins over (and is exclusive with) the host window's own Escape shortcut, which is gated on !open.
    Shortcut {
        sequence: "Escape"
        context: Qt.WindowShortcut
        enabled: root.open
        onActivated: root.close()
    }

    Behavior on opacity {
        Anim {
            duration: Appearance.anim.durations.expressiveEffects
        }
    }

    Rectangle {
        anchors.fill: parent
        color: Qt.alpha(Colours.palette.m3scrim, 0.45)

        MouseArea {
            anchors.fill: parent
            enabled: root.open
            onClicked: if (root.dismissOnScrim)
                root.close()
        }
    }

    StyledRect {
        anchors.centerIn: parent
        width: Math.min(root.cardWidth, root.width - Appearance.padding.large * 2)
        implicitHeight: column.implicitHeight + Appearance.padding.large * 2
        height: implicitHeight
        radius: Appearance.rounding.large
        color: Colours.palette.m3surfaceContainerHigh
        scale: root.open ? 1 : 0.94

        Behavior on scale {
            Anim {
                duration: Appearance.anim.durations.expressiveEffects
            }
        }

        // Swallow clicks so they never reach the scrim.
        MouseArea {
            anchors.fill: parent
        }

        ColumnLayout {
            id: column

            anchors.fill: parent
            anchors.margins: Appearance.padding.large + Appearance.padding.small
            spacing: Appearance.spacing.larger

            StyledText {
                Layout.fillWidth: true
                visible: root.title !== ""
                text: root.title
                font.pointSize: Appearance.font.size.large
                elide: Text.ElideRight
            }

            ColumnLayout {
                id: body

                Layout.fillWidth: true
                spacing: Appearance.spacing.normal
            }

            RowLayout {
                id: actionRow

                Layout.fillWidth: true
                Layout.alignment: Qt.AlignRight
                spacing: Appearance.spacing.small
            }
        }
    }
}
