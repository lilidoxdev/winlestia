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

    // Transparence du fond de la carte (0 % = fond plein, 100 % = sans fond) ; le contenu reste opaque. Le backend stocke l'opacité
    // du fond (100 - transparence). Sans valeur propre, le curseur affiche la transparence réellement appliquée par le shell
    // (WinjetTheme.defaultTransparency : automatique, personnalisé ou opaque). `draft` : valeur locale pendant et juste après le geste,
    // pour que la poignée ne dépende jamais d'un aller-retour Bridge (sinon elle retombe sur l'ancienne valeur au relâchement).
    property var draft: null
    readonly property var stored: inst ? inst.opacity : null
    readonly property bool opacitySet: stored !== null && stored !== undefined
    readonly property real transparencyShown: draft !== null ? draft : (opacitySet ? 100 - stored : WinjetTheme.defaultTransparency)
    // Le brouillon s'efface à l'écho du backend (même clampé ou rejeté) ; sinon un minuteur à usage unique, armé après l'écriture.
    onStoredChanged: {
        draftTimer.stop();
        draft = null;
    }

    Timer {
        id: draftTimer

        interval: 1500
        onTriggered: form.draft = null
    }

    function commitOpacity(v: real): void {
        const t = Math.round(v);
        draft = t;
        Bridge.winjets.setOpacity(inst.id, 100 - t);
        if (stored !== null && stored !== undefined && stored === 100 - t)
            draft = null;
        else
            draftTimer.restart();
    }

    SettingsSliderRow {
        id: opacitySlider

        Layout.fillWidth: true
        rowId: "winjets.opacity"
        title: qsTr("Transparence")
        hint: qsTr("Transparence du fond de la carte. 0 % : fond plein ; 100 % : sans fond.")
        from: 0
        to: 100
        stepSize: 1
        commitOnRelease: true
        value: form.transparencyShown
        valueText: {
            const pct = Math.round(slider.value) + " %";
            if (form.opacitySet || form.draft !== null || slider.pressed)
                return pct;
            return qsTr("Par défaut") + " (" + pct + ")";
        }
        slider.objectName: "winjetOpacity"
        onPreviewed: v => form.draft = Math.round(v)
        onMoved: v => form.commitOpacity(v)

        TextButton {
            objectName: "winjetOpacityReset"
            visible: form.opacitySet || form.draft !== null
            text: qsTr("Par défaut")
            type: TextButton.Text
            onClicked: {
                draftTimer.stop();
                form.draft = null;
                Bridge.winjets.setOpacity(form.inst.id, null);
            }
        }
    }

    Repeater {
        model: form.spec

        SettingsRow {
            id: row

            required property var modelData
            readonly property var current: form.inst.settings ? form.inst.settings[modelData.id] : undefined

            Layout.fillWidth: true
            rowId: "winjets.setting." + modelData.id
            title: qsTr(modelData.label ?? modelData.id)
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
            model: parent && parent.entry && Array.isArray(parent.entry.options) ? parent.entry.options.map(o => ({
                    label: qsTr(String(o.label ?? o.value)),
                    value: o.value
                })) : []
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
