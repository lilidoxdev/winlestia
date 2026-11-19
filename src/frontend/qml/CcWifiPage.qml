// Page Wi-Fi du Centre de contrôle : réseau actuel, réseaux disponibles, saisie du mot de passe, oubli, portail captif.
// Les mots de passe restent dans le champ ci-dessous et ne transitent que vers l'action « wifiConnect ».
pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.components.containers
import qs.services
import qs.config
import "CcSync.js" as CcSync

Item {
    id: root
    signal back
    readonly property var wifi: Bridge.controls.wifi ?? ({})
    readonly property string listState: wifi.state ?? "loading"
    readonly property bool radioOn: Bridge.controls.wifiOn ?? false
    readonly property bool busy: wifi.connecting ?? false
    readonly property int resultSeq: wifi.result ? wifi.result.seq : 0
    property string selected: ""
    property string notice: ""
    readonly property int total: current.count + available.count

    function barsIcon(bars) {
        return bars >= 4 ? "wifi" : bars === 3 ? "wifi_2_bar" : "wifi_1_bar";
    }
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
        id: current
    }
    ListModel {
        id: available
    }
    function rowOf(n) {
        return {
            ssid: n.ssid,
            bars: n.bars ?? 0,
            security: n.security ?? "",
            secure: !!n.secure,
            connected: !!n.connected,
            saved: !!n.saved,
            supported: n.supported !== false
        };
    }
    function syncModel() {
        const src = (listState === "ok" && radioOn) ? (wifi.networks ?? []) : [];
        const rows = src.map(rowOf);
        // Un réseau ouvert (champ de mot de passe) ne bouge pas tant qu'il est sélectionné.
        const sel = rows.find(r => r.ssid === selected);
        let inCurrent = false;
        for (let i = 0; i < current.count; i++)
            inCurrent = inCurrent || current.get(i).ssid === selected;
        if (sel && (current.count + available.count) > 0 && sel.connected === inCurrent) {
            for (const model of [current, available])
                for (let i = 0; i < model.count; i++) {
                    const row = rows.find(r => r.ssid === model.get(i).ssid);
                    if (!row)
                        continue;
                    for (const f in row)
                        if (model.get(i)[f] !== row[f])
                            model.setProperty(i, f, row[f]);
                }
            return;
        }
        CcSync.sync(current, rows.filter(r => r.connected), "ssid");
        CcSync.sync(available, rows.filter(r => !r.connected), "ssid");
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

    component Section: StyledText {
        Layout.topMargin: Appearance.spacing.smaller
        Layout.leftMargin: 4
        font.pointSize: Appearance.font.size.small
        color: Colours.palette.m3onSurfaceVariant
    }
    component NetRow: CcRow {
        id: row
        required property int index
        required property string ssid
        required property int bars
        required property string security
        required property bool secure
        required property bool connected
        required property bool saved
        required property bool supported
        property int count: 0
        readonly property bool open: root.selected === ssid
        readonly property bool enterprise: security === "enterprise"
        // Après un échec sur un réseau mémorisé, le champ du mot de passe revient pour remplacer l'ancien mot de passe périmé.
        readonly property bool retry: root.wifi.result !== undefined && root.wifi.result.kind === "connect" &&
            root.wifi.result.key !== "" && root.wifi.result.ssid === ssid
        Layout.fillWidth: true
        first: index === 0
        last: index === count - 1
        selected: open
        headerHeight: head.implicitHeight + 24
        implicitHeight: head.implicitHeight + 24 + (open ? details.implicitHeight + Appearance.spacing.normal : 0)
        clip: true
        onClicked: root.selected = row.open ? "" : row.ssid
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
            anchors.margins: 12
            spacing: Appearance.spacing.normal
            RowLayout {
                id: head
                Layout.fillWidth: true
                spacing: Appearance.spacing.normal
                Rectangle {
                    Layout.preferredWidth: 36
                    Layout.preferredHeight: 36
                    radius: 18
                    color: row.connected ? Colours.palette.m3primary : Colours.palette.m3secondaryContainer
                    MaterialIcon {
                        anchors.centerIn: parent
                        text: root.barsIcon(row.bars)
                        fill: row.connected ? 1 : 0
                        color: row.connected ? Colours.palette.m3onPrimary : Colours.palette.m3onSecondaryContainer
                    }
                }
                ColumnLayout {
                    Layout.fillWidth: true
                    spacing: 0
                    StyledText {
                        text: row.ssid
                        Layout.fillWidth: true
                        elide: Text.ElideRight
                        font.pointSize: Appearance.font.size.normal
                    }
                    StyledText {
                        Layout.fillWidth: true
                        elide: Text.ElideRight
                        font.pointSize: Appearance.font.size.small
                        color: Colours.palette.m3outline
                        text: row.connected ? (row.secure ? qsTr("Connecté • Sécurisé") : qsTr("Connecté")) : row.saved ? qsTr("Mémorisé") :
                            row.enterprise ? qsTr("Réseau d’entreprise") : !row.supported ? qsTr("Non pris en charge") : row.secure ?
                            qsTr("Sécurisé") : qsTr("Ouvert")
                    }
                }
                MaterialIcon {
                    visible: row.secure && !row.connected
                    text: "lock"
                    color: Colours.palette.m3outline
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
                                text: "visibility"
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
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: Appearance.spacing.normal
        CcHeader {
            Layout.fillWidth: true
            title: qsTr("Wi-Fi")
            hasSwitch: true
            checked: root.radioOn
            onBack: root.back()
            onSwitched: Bridge.action("radio", {
                kind: "wifi"
            })
        }
        StyledFlickable {
            id: scroll
            Layout.fillWidth: true
            Layout.fillHeight: true
            clip: true
            contentWidth: width
            contentHeight: body.implicitHeight
            boundsBehavior: Flickable.StopAtBounds
            C.ScrollBar.vertical: StyledScrollBar {
                flickable: scroll
            }
            ColumnLayout {
                id: body
                width: scroll.width
                spacing: 2
                StyledRect {
                    visible: root.notice !== ""
                    Layout.fillWidth: true
                    Layout.bottomMargin: Appearance.spacing.small
                    implicitHeight: noticeLabel.implicitHeight + 24
                    radius: 16
                    color: Colours.palette.m3surfaceContainer
                    StyledText {
                        id: noticeLabel
                        objectName: "wifiNotice"
                        anchors.fill: parent
                        anchors.margins: 12
                        text: root.notice
                        wrapMode: Text.Wrap
                        color: Colours.palette.m3error
                        font.pointSize: Appearance.font.size.small
                    }
                }
                StyledRect {
                    visible: root.wifi.limited === true && root.listState === "ok"
                    Layout.fillWidth: true
                    Layout.bottomMargin: Appearance.spacing.small
                    implicitHeight: portalRow.implicitHeight + 24
                    radius: 16
                    color: Colours.palette.m3secondaryContainer
                    RowLayout {
                        id: portalRow
                        anchors.fill: parent
                        anchors.margins: 12
                        spacing: Appearance.spacing.normal
                        StyledText {
                            Layout.fillWidth: true
                            text: qsTr("Ce réseau demande une page de connexion.")
                            wrapMode: Text.Wrap
                            font.pointSize: Appearance.font.size.small
                            color: Colours.palette.m3onSecondaryContainer
                        }
                        TextButton {
                            objectName: "wifiPortal"
                            text: qsTr("Ouvrir la page")
                            onClicked: Bridge.action("wifiPortal", {})
                        }
                    }
                }
                ColumnLayout {
                    visible: root.total === 0
                    Layout.fillWidth: true
                    Layout.topMargin: Appearance.spacing.large
                    spacing: Appearance.spacing.normal
                    MaterialIcon {
                        Layout.alignment: Qt.AlignHCenter
                        text: root.radioOn ? "wifi_find" : "wifi_off"
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
                        objectName: "wifiLocation"
                        visible: root.radioOn && (root.listState === "location" || root.listState === "empty")
                        Layout.alignment: Qt.AlignHCenter
                        type: TextButton.Tonal
                        text: qsTr("Ouvrir les réglages de localisation")
                        onClicked: Bridge.action("wifiSettings", {
                            page: "location"
                        })
                    }
                }
                Section {
                    visible: current.count > 0
                    text: qsTr("Réseau actuel")
                }
                Repeater {
                    model: current
                    NetRow {
                        count: current.count
                    }
                }
                Section {
                    visible: available.count > 0
                    text: qsTr("Réseaux disponibles")
                }
                Repeater {
                    model: available
                    NetRow {
                        count: available.count
                    }
                }
                CcRow {
                    Layout.fillWidth: true
                    Layout.topMargin: Appearance.spacing.normal
                    implicitHeight: 48
                    first: true
                    last: true
                    onClicked: Bridge.action("wifiSettings", {
                        page: "network"
                    })
                    RowLayout {
                        anchors.fill: parent
                        anchors.leftMargin: 16
                        anchors.rightMargin: 16
                        spacing: Appearance.spacing.normal
                        MaterialIcon {
                            text: "settings"
                            color: Colours.palette.m3onSurfaceVariant
                        }
                        StyledText {
                            Layout.fillWidth: true
                            text: qsTr("Paramètres Wi-Fi")
                            font.pointSize: Appearance.font.size.normal
                        }
                        MaterialIcon {
                            text: "open_in_new"
                            color: Colours.palette.m3outline
                        }
                    }
                }
            }
        }
    }
}
