import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "."
import "IdleTime.js" as IdleTime

// Écran de verrouillage du shell : mot de passe, activation, verrouillage automatique, raccourci.
ColumnLayout {
    id: page

    property var host
    readonly property var cfg: Bridge.settings.lock ?? {}
    readonly property var state: Bridge.lock ?? {}
    readonly property bool hasPassword: cfg.hasPassword ?? false
    readonly property bool lockEnabled: cfg.enabled ?? false
    readonly property int idleSeconds: cfg.idleSeconds ?? 0
    readonly property int minPasswordLength: 4
    readonly property var shortcut: (Bridge.settings.shortcuts ?? []).find(s => s.id === "lock")
    // Dernier délai connu : réaffiché quand on réactive l'option après l'avoir coupée.
    property int lastIdle: IdleTime.DEFAULT_SECONDS
    readonly property int shownIdle: idleSeconds > 0 ? idleSeconds : lastIdle
    readonly property bool lockReady: hasPassword && lockEnabled
    property bool unitMinutes: true
    readonly property int idleFieldWidth: 64
    property bool firstPasswordPending: false
    property string localError: ""
    property int shownSeq: -1

    readonly property string idleWords: {
        const m = Math.floor(shownIdle / 60);
        const sec = shownIdle % 60;
        const parts = [];
        if (m > 0)
            parts.push(qsTr("%1 min").arg(m));
        if (sec > 0 || m === 0)
            parts.push(qsTr("%1 s").arg(sec));
        return qsTr("Verrouillage après %1 d'inactivité").arg(parts.join(" "));
    }
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
        firstPasswordPending = !hasPassword;
        Bridge.action("lockSetPassword", {
            current: currentField.text,
            password: newField.text
        });
        currentField.text = "";
        newField.text = "";
        confirmField.text = "";
    }

    // Affiche un total en secondes : en minutes s'il en est un multiple entier, sinon en secondes.
    function showIdle(total: int): void {
        unitMinutes = total % 60 === 0;
        idleField.text = String(unitMinutes ? total / 60 : total);
    }

    // Validation (Entrée, perte de focus, changement d'unité) : on borne puis on réaffiche dans l'unité courante.
    function commitIdle(): void {
        const n = IdleTime.normalize(0, (parseInt(idleField.text) || 0) * (unitMinutes ? 60 : 1));
        lastIdle = n.total;
        if (unitMinutes && n.total % 60 !== 0)
            unitMinutes = false;
        idleField.text = String(unitMinutes ? n.total / 60 : n.total);
        Bridge.action("lockSettings", {
            idleSeconds: n.total
        });
    }

    function focusPassword(): void {
        newField.focusInput();
        for (let p = newField; p; p = p.parent) {
            if (p.contentY !== undefined && p.contentItem) {
                const y = newField.mapToItem(p.contentItem, 0, 0).y;
                p.contentY = Math.max(0, Math.min(y - Appearance.padding.large * 3, p.contentHeight - p.height));
                break;
            }
        }
    }

    onIdleSecondsChanged: {
        if (idleSeconds > 0)
            lastIdle = idleSeconds;
        if (!idleField.editing)
            showIdle(shownIdle);
    }
    Component.onCompleted: showIdle(shownIdle)

    spacing: Appearance.spacing.larger

    // Une nouvelle réponse du backend remplace l'erreur locale.
    Connections {
        target: Bridge

        function onChanged(): void {
            const seq = page.state.passwordSeq ?? 0;
            if (seq !== page.shownSeq) {
                page.shownSeq = seq;
                page.localError = "";
                // Premier mot de passe enregistré : on active le verrouillage pour ne pas laisser l'utilisateur dans le doute.
                if (page.firstPasswordPending && (page.state.passwordResult ?? "") === "ok")
                    Bridge.action("lockSettings", {
                        enabled: true
                    });
                page.firstPasswordPending = false;
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

        function focusInput(): void {
            input.forceActiveFocus();
        }

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

    // Champ numérique (chiffres seuls) : le texte est posé par showIdle(), jamais lié, car le champ le réécrit à la frappe.
    component IdleField: StyledRect {
        id: box

        property alias text: input.text
        readonly property bool editing: input.activeFocus

        signal commit

        implicitWidth: page.idleFieldWidth
        implicitHeight: input.implicitHeight + Appearance.padding.small * 2
        color: Colours.layer(Colours.palette.m3surfaceContainer, 2)
        radius: Appearance.rounding.small
        border.width: 1
        border.color: input.activeFocus ? Colours.palette.m3primary : Qt.alpha(Colours.palette.m3outline, 0.3)

        StyledTextField {
            id: input

            anchors.centerIn: parent
            width: parent.width - Appearance.padding.normal
            horizontalAlignment: TextInput.AlignRight
            inputMethodHints: Qt.ImhDigitsOnly
            validator: IntValidator {
                bottom: 0
                top: IdleTime.MAX_SECONDS
            }
            onActiveFocusChanged: if (activeFocus)
                selectAll()
            onAccepted: box.commit()
            onEditingFinished: box.commit()
        }
    }

    StyledRect {
        Layout.fillWidth: true
        visible: !page.hasPassword
        implicitHeight: notice.implicitHeight + Appearance.padding.large * 2
        radius: Appearance.rounding.normal
        color: Colours.palette.m3secondaryContainer

        RowLayout {
            id: notice

            anchors.left: parent.left
            anchors.right: parent.right
            anchors.verticalCenter: parent.verticalCenter
            anchors.margins: Appearance.padding.large
            spacing: Appearance.spacing.normal

            MaterialIcon {
                text: "info"
                color: Colours.palette.m3onSecondaryContainer
            }
            StyledText {
                Layout.fillWidth: true
                wrapMode: Text.WordWrap
                text: qsTr("Définissez d'abord un mot de passe pour activer le verrouillage")
                color: Colours.palette.m3onSecondaryContainer
            }
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
            hint: page.hasPassword ? SettingsIndex.desc("lockEnabled") : qsTr("Mot de passe requis")
            onBlockedTapped: page.focusPassword()

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
            hint: page.lockReady ? SettingsIndex.desc("lockIdle") : page.hasPassword ? qsTr("Activez d'abord le verrouillage") : qsTr("Mot de passe requis")
            onBlockedTapped: if (!page.hasPassword)
                page.focusPassword()

            SettingsSwitch {
                id: idleSwitch

                objectName: "lockIdleToggle"
                enabled: page.lockReady
                value: page.idleSeconds > 0
                onToggled: Bridge.action("lockSettings", {
                    idleSeconds: checked ? Math.max(IdleTime.MIN_SECONDS, page.lastIdle) : 0
                })
            }
        }

        SettingsRow {
            rowId: "lockIdleTime"
            title: qsTr("Délai d'inactivité")
            hint: page.idleWords
            sub: true
            shown: page.idleSeconds > 0

            IdleField {
                id: idleField

                objectName: "lockIdleValue"
                onCommit: page.commitIdle()
            }

            SettingsChips {
                namePrefix: "lockIdleUnit-"
                model: [
                    {
                        label: qsTr("min"),
                        value: "min"
                    },
                    {
                        label: qsTr("s"),
                        value: "s"
                    }
                ]
                current: page.unitMinutes ? "min" : "s"
                onChosen: value => {
                    page.unitMinutes = value === "min";
                    page.commitIdle();
                }
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
            hint: page.lockReady ? "" : page.hasPassword ? qsTr("Activez d'abord le verrouillage") : qsTr("Mot de passe requis")

            TextButton {
                objectName: "lockNowButton"
                text: qsTr("Verrouiller maintenant")
                enabled: page.lockReady
                onClicked: Bridge.action("lockNow", {})
            }
        }
    }
}
