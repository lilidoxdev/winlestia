import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."

// Écran de verrouillage du shell : mot de passe, activation, verrouillage automatique, raccourci.
ColumnLayout {
    id: page

    property var host
    readonly property var cfg: Bridge.settings.lock ?? {}
    readonly property var state: Bridge.lock ?? {}
    readonly property bool hasPassword: cfg.hasPassword ?? false
    readonly property bool lockEnabled: cfg.enabled ?? false
    readonly property int idleSeconds: cfg.idleSeconds ?? 0
    readonly property int minIdle: 30
    readonly property int minPasswordLength: 4
    readonly property var shortcut: (Bridge.settings.shortcuts ?? []).find(s => s.id === "lock")
    // Entrée de l'utilisateur pas encore enregistrée (minutes/secondes), prioritaire sur la valeur du backend.
    property bool idleDirty: false
    property int minutesInput: Math.floor(idleSeconds / 60)
    property int secondsInput: idleSeconds % 60
    property string localError: ""
    property int shownSeq: -1

    readonly property int totalIdle: minutesInput * 60 + secondsInput
    readonly property string resultText: {
        const r = state.passwordResult ?? "";
        if (localError !== "")
            return localError;
        if (r === "ok")
            return qsTr("Mot de passe enregistré");
        if (r === "wrong")
            return qsTr("Mot de passe actuel incorrect");
        if (r === "invalid")
            return qsTr("4 caractères minimum");
        return "";
    }
    readonly property bool resultGood: localError === "" && (state.passwordResult ?? "") === "ok"

    function submitPassword(): void {
        localError = "";
        if (newField.text !== confirmField.text) {
            localError = qsTr("Les deux mots de passe ne correspondent pas");
            return;
        }
        if (newField.text.length < minPasswordLength) {
            localError = qsTr("4 caractères minimum");
            return;
        }
        Bridge.action("lockSetPassword", {
            current: currentField.text,
            password: newField.text
        });
        currentField.text = "";
        newField.text = "";
        confirmField.text = "";
    }

    function applyIdle(): void {
        idleDirty = false;
        const total = totalIdle;
        const clamped = total <= 0 ? minIdle : Math.max(minIdle, total);
        minutesInput = Math.floor(clamped / 60);
        secondsInput = clamped % 60;
        Bridge.action("lockSettings", {
            idleSeconds: clamped
        });
    }

    spacing: Appearance.spacing.larger

    // Une nouvelle réponse du backend remplace l'erreur locale.
    Connections {
        target: Bridge

        function onChanged(): void {
            const seq = page.state.passwordSeq ?? 0;
            if (seq !== page.shownSeq) {
                page.shownSeq = seq;
                page.localError = "";
            }
        }
    }

    StyledText {
        Layout.fillWidth: true
        Layout.leftMargin: Appearance.padding.larger
        wrapMode: Text.WordWrap
        text: qsTr("Verrouille le shell Winlestia (écran, panneaux). Ne remplace pas le verrouillage de session Windows.")
        font.pointSize: Appearance.font.size.small
        color: Colours.palette.m3outline
    }

    component PasswordField: StyledRect {
        id: box

        property alias text: input.text
        property string placeholder

        Layout.preferredWidth: 220
        implicitHeight: input.implicitHeight + Appearance.padding.small * 2
        color: Colours.layer(Colours.palette.m3surfaceContainer, 2)
        radius: Appearance.rounding.small
        border.width: 1
        border.color: input.activeFocus ? Colours.palette.m3primary : Qt.alpha(Colours.palette.m3outline, 0.3)

        StyledTextField {
            id: input

            anchors.centerIn: parent
            width: parent.width - Appearance.padding.normal
            echoMode: TextInput.Password
            placeholderText: box.placeholder
            onAccepted: page.submitPassword()
        }
    }

    SettingsSection {
        sectionId: "lockPassword"
        label: qsTr("Mot de passe")

        SettingsRow {
            rowId: "lockCurrent"
            title: qsTr("Mot de passe actuel")
            hint: ""
            shown: page.hasPassword

            PasswordField {
                id: currentField

                objectName: "lockCurrentField"
            }
        }

        SettingsRow {
            rowId: "lockNew"
            title: qsTr("Nouveau mot de passe")
            hint: ""

            PasswordField {
                id: newField

                objectName: "lockNewField"
            }
        }

        SettingsRow {
            rowId: "lockConfirm"
            title: qsTr("Confirmer")
            hint: page.resultText
            hintColor: page.resultGood ? Colours.palette.m3primary : Colours.palette.m3error

            PasswordField {
                id: confirmField

                objectName: "lockConfirmField"
            }

            TextButton {
                objectName: "lockSavePassword"
                text: qsTr("Enregistrer")
                enabled: newField.text.length > 0
                onClicked: page.submitPassword()
            }
        }
    }

    SettingsSection {
        sectionId: "lockActivation"
        label: qsTr("Verrouillage")

        SettingsRow {
            rowId: "lockEnabled"
            target: enabledSwitch
            hint: page.hasPassword ? SettingsIndex.desc("lockEnabled") : qsTr("Définis d'abord un mot de passe.")

            SettingsSwitch {
                id: enabledSwitch

                objectName: "lockEnabledToggle"
                enabled: page.hasPassword
                value: page.lockEnabled
                onToggled: Bridge.action("lockSettings", {
                    enabled: checked
                })
            }
        }

        SettingsRow {
            rowId: "lockIdle"
            target: idleSwitch
            hint: page.idleSeconds > 0 || page.idleDirty ? qsTr("30 secondes minimum") : SettingsIndex.desc("lockIdle")

            SettingsSwitch {
                id: idleSwitch

                objectName: "lockIdleToggle"
                value: page.idleSeconds > 0
                onToggled: {
                    if (checked) {
                        page.applyIdle();
                    } else {
                        page.idleDirty = false;
                        Bridge.action("lockSettings", {
                            idleSeconds: 0
                        });
                    }
                }
            }
        }

        SettingsRow {
            rowId: "lockIdleTime"
            title: qsTr("Délai d'inactivité")
            hint: ""
            sub: true
            shown: page.idleSeconds > 0

            StyledInputField {
                objectName: "lockMinutes"
                text: String(page.minutesInput)
                validator: IntValidator {
                    bottom: 0
                    top: 1440
                }
                onTextEdited: t => {
                    page.idleDirty = true;
                    page.minutesInput = parseInt(t) || 0;
                }
                onEditingFinished: page.applyIdle()
            }

            StyledText {
                text: qsTr("min")
                color: Colours.palette.m3outline
            }

            StyledInputField {
                objectName: "lockSeconds"
                text: String(page.secondsInput)
                validator: IntValidator {
                    bottom: 0
                    top: 59
                }
                onTextEdited: t => {
                    page.idleDirty = true;
                    page.secondsInput = parseInt(t) || 0;
                }
                onEditingFinished: page.applyIdle()
            }

            StyledText {
                text: qsTr("s")
                color: Colours.palette.m3outline
            }
        }

        SettingsRow {
            rowId: "lockShortcut"
            target: shortcutButton

            SettingsKeyCaps {
                combo: page.shortcut?.combo ?? ""
            }

            TextButton {
                id: shortcutButton

                objectName: "lockShortcutEdit"
                text: qsTr("Modifier")
                type: TextButton.Tonal
                onClicked: {
                    if (page.host)
                        page.host.openShortcutDialog("lock", qsTr("Verrouiller l'écran"));
                }
            }
        }

        SettingsRow {
            rowId: "lockNow"
            title: qsTr("Verrouiller maintenant")
            hint: ""

            TextButton {
                objectName: "lockNowButton"
                text: qsTr("Verrouiller maintenant")
                enabled: page.lockEnabled && page.hasPassword
                onClicked: Bridge.action("lockNow", {})
            }
        }
    }
}
