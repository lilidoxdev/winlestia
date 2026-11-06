// Shortcut capture. A focused window of its own: the dashboard is hidden while it owns the keyboard, like the former recorder.
// Live keys are drawn as keycaps; the combination is sent to the backend (Bridge.action("shortcut", {id, combo})),
// which answers with Bridge.settings.shortcutStatus. Escape cancels.
import QtQuick
import QtQuick.Window
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

Window {
    id: dlg

    // Asks the page to scroll to the row of an action that already uses the combination.
    signal gotoShortcut(string id)

    property string shortcutId
    property string shortcutLabel
    property string draft
    property bool failed: false
    property string message
    property string conflictId
    property string conflictLabel
    property int baseNonce: 0
    property bool held: false
    property int activeMods: 0
    readonly property var status: Bridge.settings.shortcutStatus ?? null
    readonly property bool reduced: Appearance.reducedEffects

    function openFor(id: string, label: string): void {
        shortcutId = id;
        shortcutLabel = label;
        draft = "";
        failed = false;
        message = "";
        conflictId = "";
        activeMods = 0;
        baseNonce = (Bridge.settings.shortcutStatus ?? {}).nonce ?? 0;
        Bridge.recordShortcut(true, id);
        if (Bridge.window)
            Bridge.window.hide();
        visible = true;
        requestActivate();
        input.forceActiveFocus();
    }

    function finish(): void {
        if (!visible)
            return;
        visible = false;
        Bridge.recordShortcut(false, shortcutId);
        if (Bridge.window)
            Bridge.window.show();
        Bridge.navigate("settings");
    }

    function keyName(key: int): string {
        if ((key >= 0x30 && key <= 0x39) || (key >= 0x41 && key <= 0x5A))
            return String.fromCharCode(key);
        if (key >= Qt.Key_F1 && key <= Qt.Key_F24)
            return "F" + (key - Qt.Key_F1 + 1);
        if (key === Qt.Key_Space)
            return "Espace";
        return Bridge.shortcutText(key, 0);
    }

    // Backend order: Win, Ctrl, Alt, Maj.
    function modNames(m: int): var {
        const out = [];
        if (m & Qt.MetaModifier)
            out.push("Win");
        if (m & Qt.ControlModifier)
            out.push("Ctrl");
        if (m & Qt.AltModifier)
            out.push("Alt");
        if (m & Qt.ShiftModifier)
            out.push("Maj");
        return out;
    }

    function labelOf(id: string): string {
        const list = Bridge.settings.shortcuts ?? [];
        for (const s of list)
            if (s.id === id)
                return s.label;
        return id;
    }

    onStatusChanged: {
        if (!visible || !status || (status.nonce ?? 0) <= baseNonce || status.id !== shortcutId)
            return;
        baseNonce = status.nonce;
        if (status.ok) {
            finish();
        } else {
            failed = true;
            message = status.message ?? "";
            conflictId = status.conflictId ?? "";
            conflictLabel = conflictId !== "" ? labelOf(conflictId) : "";
        }
    }

    objectName: "shortcutRecorder"
    title: "Winlestia · Raccourci"
    transientParent: null
    color: "transparent"
    flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint
    width: 520
    height: card.implicitHeight + 24
    x: Bridge.screenX + (Bridge.screenWidth - width) / 2
    y: Bridge.screenY + (Bridge.screenHeight - height) / 2
    onClosing: event => {
        event.accepted = false;
        finish();
    }

    Connections {
        target: Bridge

        function onShellEvent(event: string): void {
            if (event === "shortcut-saved:" + dlg.shortcutId)
                dlg.finish();
        }
    }

    StyledRect {
        id: card

        anchors.fill: parent
        anchors.margins: 12
        implicitHeight: col.implicitHeight + Appearance.padding.larger * 2
        radius: Appearance.rounding.large
        color: Qt.alpha(Colours.surfaceBase, 0.98)
        border.width: 1
        border.color: Qt.alpha(Colours.palette.m3primary, 0.5)
        opacity: dlg.visible ? 1 : 0
        scale: dlg.visible || dlg.reduced ? 1 : 0.94

        Behavior on opacity {
            enabled: !dlg.reduced

            Anim {
                duration: Appearance.anim.durations.small
            }
        }

        Behavior on scale {
            enabled: !dlg.reduced

            Anim {
                duration: Appearance.anim.durations.small
            }
        }

        ColumnLayout {
            id: col

            anchors.fill: parent
            anchors.margins: Appearance.padding.larger
            spacing: Appearance.spacing.normal

            RowLayout {
                Layout.fillWidth: true
                spacing: Appearance.spacing.normal

                MaterialIcon {
                    text: "keyboard"
                    font.pointSize: Appearance.font.size.extraLarge
                    color: Colours.palette.m3primary
                }

                ColumnLayout {
                    Layout.fillWidth: true
                    spacing: 1

                    StyledText {
                        Layout.fillWidth: true
                        text: qsTr("Appuie sur le nouveau raccourci")
                        font.pointSize: Appearance.font.size.larger
                    }

                    StyledText {
                        Layout.fillWidth: true
                        elide: Text.ElideRight
                        text: dlg.shortcutLabel
                        font.pointSize: Appearance.font.size.small
                        color: Colours.palette.m3outline
                    }
                }

                IconButton {
                    icon: "close"
                    type: IconButton.Text
                    onClicked: dlg.finish()
                }
            }

            StyledRect {
                id: input

                objectName: "shortcutInput"
                Layout.fillWidth: true
                implicitHeight: 72
                radius: Appearance.rounding.normal
                color: Colours.palette.m3surfaceContainerHigh
                border.width: dlg.failed ? 2 : 0
                border.color: Colours.palette.m3error
                focus: true

                Keys.onPressed: event => {
                    event.accepted = true;
                    if (event.key === Qt.Key_Escape) {
                        dlg.finish();
                        return;
                    }
                    dlg.activeMods = event.modifiers;
                    if (event.key >= Qt.Key_Shift && event.key <= Qt.Key_Meta || event.key === Qt.Key_AltGr)
                        return;
                    const combo = dlg.modNames(event.modifiers).concat([dlg.keyName(event.key)]).join("+");
                    dlg.draft = combo;
                    dlg.failed = false;
                    dlg.message = "";
                    Bridge.action("shortcut", {id: dlg.shortcutId, combo: combo});
                }
                Keys.onReleased: event => {
                    event.accepted = true;
                    dlg.activeMods = event.modifiers;
                }

                SettingsKeyCaps {
                    anchors.centerIn: parent
                    width: Math.min(implicitWidth, parent.width - 24)
                    combo: dlg.draft !== "" ? dlg.draft : dlg.modNames(dlg.activeMods).join("+")
                    emptyText: qsTr("Ctrl + une touche")
                }
            }

            RowLayout {
                visible: dlg.failed
                Layout.fillWidth: true
                spacing: Appearance.spacing.small

                MaterialIcon {
                    Layout.alignment: Qt.AlignTop
                    text: "error"
                    color: Colours.palette.m3error
                }

                ColumnLayout {
                    Layout.fillWidth: true
                    spacing: Appearance.spacing.smaller

                    StyledText {
                        objectName: "shortcutMessage"
                        Layout.fillWidth: true
                        wrapMode: Text.WordWrap
                        text: qsTr(dlg.message)
                        font.pointSize: Appearance.font.size.small
                        color: Colours.palette.m3error
                    }

                    TextButton {
                        visible: dlg.conflictId !== ""
                        text: qsTr("Aller à « %1 »").arg(dlg.conflictLabel)
                        type: TextButton.Text
                        onClicked: {
                            const id = dlg.conflictId;
                            dlg.finish();
                            dlg.gotoShortcut(id);
                        }
                    }
                }
            }

            StyledText {
                Layout.fillWidth: true
                wrapMode: Text.WordWrap
                text: qsTr("Le raccourci est enregistré dès que la combinaison est valide.")
                font.pointSize: Appearance.font.size.small
                color: Colours.palette.m3onSurfaceVariant
            }

            StyledText {
                text: qsTr("Échap pour annuler")
                font.pointSize: Appearance.font.size.smaller
                color: Colours.palette.m3outline
            }
        }
    }
}
