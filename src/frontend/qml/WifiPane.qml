// Réseaux Wi-Fi des contrôles rapides : liste, sélection, saisie du mot de passe, connexion, oubli.
// Les mots de passe restent dans le champ ci-dessous et ne transitent que vers l'action « wifiConnect ».
pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.components.containers
import qs.services
import qs.config

Item {
    id: root
    signal dismiss
    readonly property var wifi: Bridge.controls.wifi ?? ({})
    readonly property string listState: wifi.state ?? "loading"
    readonly property bool radioOn: Bridge.controls.wifiOn ?? false
    readonly property bool busy: wifi.connecting ?? false
    readonly property int resultSeq: wifi.result ? wifi.result.seq : 0
    property string selected: ""
    property string notice: ""
    implicitHeight: Math.min(610, Bridge.screenHeight - 100)

    function stateText() {
        if (!radioOn)
            return qsTr("Le Wi-Fi est désactivé.");
        switch (listState) {
        case "noInterface":
            return qsTr("Aucune carte Wi-Fi détectée sur cet ordinateur.");
        case "serviceStopped":
            return qsTr("Le service Wi-Fi de Windows est arrêté.");
        case "location":
            return qsTr("Windows demande l’autorisation de localisation pour afficher les réseaux Wi-Fi.");
        case "empty":
            return qsTr("Aucun réseau trouvé pour le moment.");
        case "failed":
            return qsTr("La liste des réseaux est indisponible pour le moment.");
        default:
            return qsTr("Recherche des réseaux…");
        }
    }
    function noticeText(key) {
        switch (key) {
        case "badPassword":
            return qsTr("Connexion impossible. Vérifiez le mot de passe.");
        case "timeout":
            return qsTr("Le réseau ne répond pas. Réessayez.");
        case "notFound":
            return qsTr("Ce réseau n’est plus à portée.");
        case "denied":
            return qsTr("Windows a refusé l’opération.");
        case "serviceStopped":
            return qsTr("Le service Wi-Fi de Windows est arrêté.");
        case "noInterface":
            return qsTr("Aucune carte Wi-Fi détectée sur cet ordinateur.");
        case "unsupported":
            return qsTr("Ce type de réseau n’est pas pris en charge ici.");
        case "short":
            return qsTr("Le mot de passe doit contenir au moins 8 caractères.");
        case "long":
            return qsTr("Le mot de passe doit contenir 63 caractères au plus.");
        case "chars":
            return qsTr("Le mot de passe contient des caractères non pris en charge.");
        default:
            return qsTr("La connexion a échoué.");
        }
    }
    onResultSeqChanged: notice = wifi.result && wifi.result.key !== "" ? noticeText(wifi.result.key) : ""
    onSelectedChanged: {
        notice = "";
        if (selected !== "")
            Bridge.activate();
    }
    function localCheck(security, pw) {
        if (security === "open")
            return "";
        if (pw.length === 0)
            return "short";
        for (let i = 0; i < pw.length; i++) {
            const c = pw.charCodeAt(i);
            if (c < 32 || c > 126)
                return "chars";
        }
        if (pw.length === 64 && /^[0-9a-fA-F]+$/.test(pw) && (security === "wpa2" || security === "wpa"))
            return "";
        if (pw.length < 8)
            return "short";
        return pw.length > 63 ? "long" : "";
    }
    onVisibleChanged: if (!visible)
        selected = ""
    Component.onDestruction: selected = ""

    ListModel {
        id: networks
    }
    function syncModel() {
        const src = (listState === "ok" && radioOn) ? (wifi.networks ?? []) : [];
        const keep = {};
        for (const n of src)
            keep[n.ssid] = true;
        for (let i = networks.count - 1; i >= 0; i--)
            if (!keep[networks.get(i).ssid])
                networks.remove(i);
        for (let i = 0; i < src.length; i++) {
            const n = src[i];
            const row = {
                ssid: n.ssid,
                signal: n.signal,
                bars: n.bars,
                security: n.security,
                secure: n.secure,
                connected: n.connected,
                saved: n.saved,
                supported: n.supported
            };
            let at = -1;
            for (let k = 0; k < networks.count; k++)
                if (networks.get(k).ssid === n.ssid) {
                    at = k;
                    break;
                }
            if (at < 0) {
                if (selected === "" && i <= networks.count)
                    networks.insert(i, row);
                else
                    networks.append(row);
                continue;
            }
            if (selected === "" && at !== i && i < networks.count)
                networks.move(at, i, 1);
            const target = selected === "" ? i : at;
            for (const key in row)
                if (networks.get(target)[key] !== row[key])
                    networks.setProperty(target, key, row[key]);
        }
    }
    Connections {
        target: root
        function onWifiChanged() {
            root.syncModel();
        }
        function onRadioOnChanged() {
            root.syncModel();
        }
    }
    Component.onCompleted: syncModel()

    ColumnLayout {
        anchors.fill: parent
        spacing: Appearance.spacing.large
        RowLayout {
            Layout.fillWidth: true
            spacing: Appearance.spacing.normal
            MaterialIcon {
                text: qsTr("wifi")
                font.pointSize: Appearance.font.size.extraLarge
                color: Colours.palette.m3primary
            }
            ColumnLayout {
                Layout.fillWidth: true
                spacing: 3
                StyledText {
                    text: qsTr("Réseaux Wi-Fi")
                    font.pointSize: Appearance.font.size.title
                }
                StyledText {
                    text: qsTr("Choisissez un réseau pour vous connecter")
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3outline
                }
            }
            IconButton {
                objectName: "wifiBack"
                icon: "chevron_left"
                type: IconButton.Text
                onClicked: root.dismiss()
            }
        }
        StyledRect {
            visible: root.notice !== ""
            Layout.fillWidth: true
            implicitHeight: noticeLabel.implicitHeight + Appearance.padding.large * 2
            radius: Appearance.rounding.large
            color: Colours.palette.m3surfaceContainer
            StyledText {
                id: noticeLabel
                objectName: "wifiNotice"
                anchors.fill: parent
                anchors.margins: Appearance.padding.large
                text: root.notice
                wrapMode: Text.Wrap
                color: Colours.palette.m3error
                font.pointSize: Appearance.font.size.small
            }
        }
        StyledRect {
            visible: root.wifi.limited === true && root.listState === "ok"
            Layout.fillWidth: true
            implicitHeight: portalRow.implicitHeight + Appearance.padding.large * 2
            radius: Appearance.rounding.large
            color: Colours.palette.m3secondaryContainer
            RowLayout {
                id: portalRow
                anchors.fill: parent
                anchors.margins: Appearance.padding.large
                spacing: Appearance.spacing.normal
                StyledText {
                    Layout.fillWidth: true
                    text: qsTr("Ce réseau demande une page de connexion.")
                    wrapMode: Text.Wrap
                    font.pointSize: Appearance.font.size.small
                }
                TextButton {
                    objectName: "wifiPortal"
                    text: qsTr("Ouvrir la page")
                    onClicked: Bridge.action("wifiPortal", {})
                }
            }
        }
        ColumnLayout {
            visible: networks.count === 0
            Layout.fillWidth: true
            Layout.fillHeight: true
            spacing: Appearance.spacing.large
            Item {
                Layout.fillHeight: true
            }
            MaterialIcon {
                Layout.alignment: Qt.AlignHCenter
                text: qsTr("wifi_off")
                font.pointSize: Appearance.font.size.extraLarge
                color: Colours.palette.m3outline
            }
            StyledText {
                objectName: "wifiState"
                Layout.fillWidth: true
                horizontalAlignment: Text.AlignHCenter
                wrapMode: Text.Wrap
                text: root.stateText()
                color: Colours.palette.m3outline
            }
            TextButton {
                visible: !root.radioOn
                Layout.alignment: Qt.AlignHCenter
                text: qsTr("Activer le Wi-Fi")
                onClicked: Bridge.action("radio", {
                    kind: "wifi"
                })
            }
            TextButton {
                objectName: "wifiLocation"
                visible: root.radioOn && (root.listState === "location" || root.listState === "empty")
                Layout.alignment: Qt.AlignHCenter
                type: TextButton.Tonal
                text: qsTr("Ouvrir les réglages de localisation")
                onClicked: Bridge.action("wifiSettings", {
                    page: "location"
                })
            }
            Item {
                Layout.fillHeight: true
            }
        }
        StyledListView {
            id: list
            objectName: "wifiNetworks"
            visible: networks.count > 0
            Layout.fillWidth: true
            Layout.fillHeight: true
            spacing: Appearance.spacing.smaller
            clip: true
            reuseItems: false
            model: networks
            delegate: StyledRect {
                id: row
                required property string ssid
                required property int bars
                required property string security
                required property bool secure
                required property bool connected
                required property bool saved
                required property bool supported
                readonly property bool open: root.selected === ssid
                readonly property bool enterprise: security === "enterprise"
                // Après un échec sur un réseau mémorisé, le champ du mot de passe revient pour remplacer l'ancien mot de passe périmé.
                readonly property bool retry: root.wifi.result !== undefined && root.wifi.result.kind === "connect" &&
                    root.wifi.result.key !== "" && root.wifi.result.ssid === ssid
                width: list.width
                radius: Appearance.rounding.large
                color: open ? Colours.palette.m3secondaryContainer : Colours.palette.m3surfaceContainer
                implicitHeight: head.implicitHeight + (open ? details.implicitHeight + Appearance.spacing.normal : 0) +
                    Appearance.padding.large * 2
                clip: true
                Behavior on implicitHeight {
                    enabled: !Appearance.reducedEffects
                    Anim {
                        duration: Appearance.anim.durations.small
                    }
                }
                ColumnLayout {
                    anchors.left: parent.left
                    anchors.right: parent.right
                    anchors.top: parent.top
                    anchors.margins: Appearance.padding.large
                    spacing: Appearance.spacing.normal
                    RowLayout {
                        id: head
                        Layout.fillWidth: true
                        spacing: Appearance.spacing.normal
                        Row {
                            Layout.preferredWidth: implicitWidth
                            Layout.preferredHeight: 14
                            spacing: Appearance.spacing.small / 2
                            Repeater {
                                model: 4
                                Rectangle {
                                    required property int index
                                    width: 3
                                    height: 4 + index * 3
                                    y: 14 - height
                                    radius: 1
                                    color: index < row.bars ? Colours.palette.m3primary : Colours.palette.m3outlineVariant
                                }
                            }
                        }
                        ColumnLayout {
                            Layout.fillWidth: true
                            spacing: 2
                            StyledText {
                                text: row.ssid
                                Layout.fillWidth: true
                                elide: Text.ElideRight
                                font.pointSize: Appearance.font.size.normal
                                color: row.connected ? Colours.palette.m3primary : Colours.palette.m3onSurface
                            }
                            StyledText {
                                visible: text !== ""
                                text: row.connected ? qsTr("Connecté") :
                                    row.saved ? qsTr("Mémorisé") :
                                    row.enterprise ? qsTr("Réseau d’entreprise") :
                                    !row.supported ? qsTr("Non pris en charge") : ""
                                font.pointSize: Appearance.font.size.small
                                color: Colours.palette.m3outline
                            }
                        }
                        MaterialIcon {
                            visible: row.saved
                            text: qsTr("bookmark")
                            color: Colours.palette.m3outline
                        }
                        MaterialIcon {
                            visible: row.secure
                            text: qsTr("lock")
                            color: Colours.palette.m3outline
                        }
                        MaterialIcon {
                            visible: row.connected
                            text: qsTr("check")
                            color: Colours.palette.m3primary
                        }
                    }
                    Loader {
                        id: details
                        Layout.fillWidth: true
                        active: row.open
                        visible: row.open
                        sourceComponent: ColumnLayout {
                            id: panel
                            property bool sent: false
                            spacing: Appearance.spacing.normal
                            Connections {
                                target: root
                                function onResultSeqChanged() {
                                    panel.sent = false;
                                }
                                function onBusyChanged() {
                                    if (!root.busy)
                                        panel.sent = false;
                                }
                            }
                            Timer {
                                interval: 25000
                                running: panel.sent
                                onTriggered: panel.sent = false
                            }
                            RowLayout {
                                visible: !row.enterprise && row.supported && row.secure && (!row.saved || row.retry)
                                Layout.fillWidth: true
                                spacing: Appearance.spacing.smaller
                                StyledRect {
                                    Layout.fillWidth: true
                                    implicitHeight: 40
                                    radius: Appearance.rounding.full
                                    color: Colours.palette.m3surfaceContainerHigh
                                    StyledTextField {
                                        id: secret
                                        objectName: "wifiPassword"
                                        anchors.fill: parent
                                        anchors.leftMargin: Appearance.padding.large
                                        anchors.rightMargin: Appearance.padding.large
                                        placeholderText: qsTr("Mot de passe")
                                        echoMode: eye.pressed ? TextInput.Normal : TextInput.Password
                                        maximumLength: 64
                                        inputMethodHints: Qt.ImhSensitiveData | Qt.ImhNoPredictiveText | Qt.ImhNoAutoUppercase
                                        enabled: !root.busy
                                        Component.onCompleted: Qt.callLater(forceActiveFocus)
                                        onVisibleChanged: if (visible)
                                            Qt.callLater(forceActiveFocus)
                                        Component.onDestruction: text = ""
                                        onAccepted: connect.clicked()
                                    }
                                }
                                StyledRect {
                                    id: eye
                                    objectName: "wifiEye"
                                    readonly property bool pressed: eyeArea.pressed
                                    implicitWidth: 40
                                    implicitHeight: 40
                                    radius: Appearance.rounding.full
                                    color: eyeArea.pressed ? Colours.palette.m3secondaryContainer : Colours.palette.m3surfaceContainerHigh
                                    MaterialIcon {
                                        anchors.centerIn: parent
                                        text: qsTr("visibility")
                                        color: Colours.palette.m3onSurface
                                    }
                                    MouseArea {
                                        id: eyeArea
                                        anchors.fill: parent
                                        onCanceled: {}
                                    }
                                }
                            }
                            StyledText {
                                visible: row.enterprise || !row.supported
                                Layout.fillWidth: true
                                wrapMode: Text.Wrap
                                font.pointSize: Appearance.font.size.small
                                color: Colours.palette.m3outline
                                text: row.enterprise ?
                                    qsTr("Ce réseau demande une authentification d’entreprise (802.1X). Connectez-vous depuis Windows.") :
                                    qsTr("Ce type de réseau n’est pas pris en charge ici.")
                            }
                            RowLayout {
                                Layout.fillWidth: true
                                spacing: Appearance.spacing.smaller
                                Item {
                                    Layout.fillWidth: true
                                }
                                StyledText {
                                    visible: root.busy
                                    text: qsTr("Connexion…")
                                    font.pointSize: Appearance.font.size.small
                                    color: Colours.palette.m3outline
                                }
                                TextButton {
                                    objectName: "wifiSettings"
                                    visible: row.enterprise || !row.supported
                                    type: TextButton.Tonal
                                    text: qsTr("Ouvrir dans Windows")
                                    onClicked: Bridge.action("wifiSettings", {
                                        page: "network"
                                    })
                                }
                                TextButton {
                                    objectName: "wifiForget"
                                    visible: row.saved
                                    enabled: !root.busy && !panel.sent
                                    type: TextButton.Text
                                    text: qsTr("Oublier")
                                    onClicked: {
                                        panel.sent = true;
                                        Bridge.action("wifiForget", {
                                            ssid: row.ssid
                                        });
                                    }
                                }
                                TextButton {
                                    id: connect
                                    objectName: "wifiConnect"
                                    visible: row.supported && !row.enterprise && !row.connected
                                    enabled: !root.busy && !panel.sent
                                    text: qsTr("Se connecter")
                                    onClicked: {
                                        const typed = secret && secret.visible ? secret.text : "";
                                        if (secret && secret.visible) {
                                            const bad = root.localCheck(row.security, typed);
                                            // Un réseau mémorisé peut être retenté sans rien saisir : son profil Windows est utilisé.
                                            if (bad !== "" && !(row.saved && typed === "")) {
                                                root.notice = root.noticeText(bad);
                                                return;
                                            }
                                        }
                                        root.notice = "";
                                        panel.sent = true;
                                        Bridge.action("wifiConnect", {
                                            ssid: row.ssid,
                                            password: typed
                                        });
                                        if (secret)
                                            secret.text = "";
                                    }
                                }
                            }
                        }
                    }
                }
                StateLayer {
                    anchors.left: parent.left
                    anchors.right: parent.right
                    anchors.top: parent.top
                    height: head.implicitHeight + Appearance.padding.large * 2
                    radius: row.radius
                    onClicked: root.selected = row.open ? "" : row.ssid
                }
            }
        }
    }
}
