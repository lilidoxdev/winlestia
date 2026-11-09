import QtQuick
import qs.components
import qs.config
import qs.services
import ".."

// Bloc `buttons` : options [{ value, label }] liées à `bind` ("state.<clé>"). Un clic écrit l'état d'instance ; l'actif prend l'accent.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null

    readonly property string key: spec && typeof spec.bind === "string" && spec.bind.indexOf("state.") === 0 ? spec.bind.substring(6) : ""
    readonly property var current: key !== "" && ctx && ctx.state ? ctx.state[key] : undefined
    readonly property var options: spec && Array.isArray(spec.options) ? spec.options : []

    implicitWidth: flow.implicitWidth
    implicitHeight: flow.implicitHeight

    Flow {
        id: flow

        anchors.fill: parent
        spacing: 6

        Repeater {
            model: block.options

            Rectangle {
                id: pill

                required property var modelData
                readonly property bool active: block.current === modelData.value

                width: text.implicitWidth + 24
                height: 30
                radius: height / 2
                color: active ? WinjetTheme.accent : Qt.alpha(WinjetTheme.textSecondary, 0.16)

                StyledText {
                    id: text

                    anchors.centerIn: parent
                    text: Bridge.winjets.renderText(String(pill.modelData.label ?? ""), block.ctx)
                    color: pill.active ? Colours.palette.m3onPrimary : WinjetTheme.textPrimary
                    font.pointSize: Appearance.font.size.small
                }

                MouseArea {
                    anchors.fill: parent
                    enabled: block.key !== "" && !(block.host && block.host.editing)
                    cursorShape: Qt.PointingHandCursor
                    onClicked: if (block.host)
                        block.host.setState(block.key, pill.modelData.value)
                }
            }
        }
    }
}
