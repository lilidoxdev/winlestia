import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config
import "../settings"

// Formulaire de réglages d'une instance, généré depuis les réglages déclarés (toggle, text, choice, number).
// Aucun code du paquet : seuls l'identifiant, le type, le libellé et les bornes sont lus.
ColumnLayout {
    id: form

    // Instantané Bridge.winjets.instance(id).
    property var inst: ({})
    readonly property var spec: inst && inst.settingsSpec ? inst.settingsSpec : []

    spacing: 0

    function change(key: string, value): void {
        const next = Object.assign({}, inst.settings ?? {});
        next[key] = value;
        Bridge.winjets.setSettings(inst.id, next);
    }

    Repeater {
        model: form.spec

        SettingsRow {
            id: row

            required property var modelData
            readonly property var current: form.inst.settings ? form.inst.settings[modelData.id] : undefined

            Layout.fillWidth: true
            rowId: "winjets.setting." + modelData.id
            title: modelData.label ?? modelData.id
            hint: ""

            Loader {
                sourceComponent: {
                    switch (row.modelData.type) {
                    case "toggle":
                        return toggleField;
                    case "choice":
                        return choiceField;
                    case "number":
                        return numberField;
                    default:
                        return textField;
                    }
                }
                property var entry: row.modelData
                property var value: row.current
            }
        }
    }

    Component {
        id: toggleField

        SettingsSwitch {
            value: parent && parent.value === true
            onToggled: form.change(parent.entry.id, checked)
        }
    }

    Component {
        id: choiceField

        SettingsStepper {
            model: parent && parent.entry && Array.isArray(parent.entry.options) ? parent.entry.options : []
            current: parent ? parent.value : undefined
            onChosen: v => form.change(parent.entry.id, v)
        }
    }

    Component {
        id: numberField

        StyledTextField {
            readonly property var entry: parent ? parent.entry : ({})
            implicitWidth: 96
            text: parent && parent.value !== undefined && parent.value !== null ? String(parent.value) : ""
            horizontalAlignment: TextInput.AlignRight
            inputMethodHints: Qt.ImhFormattedNumbersOnly
            validator: DoubleValidator {
                bottom: entry.min ?? -1e9
                top: entry.max ?? 1e9
                notation: DoubleValidator.StandardNotation
            }
            onEditingFinished: {
                let v = Number(text.replace(",", "."));
                if (!isFinite(v))
                    return;
                v = Math.max(entry.min ?? -1e9, Math.min(entry.max ?? 1e9, v));
                form.change(entry.id, v);
            }
        }
    }

    Component {
        id: textField

        StyledTextField {
            readonly property var entry: parent ? parent.entry : ({})
            implicitWidth: 220
            text: parent && parent.value !== undefined && parent.value !== null ? String(parent.value) : ""
            maximumLength: entry.maxLength ?? 120
            onEditingFinished: form.change(entry.id, text)
        }
    }
}
