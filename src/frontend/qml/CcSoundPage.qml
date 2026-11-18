// Page Son du Centre de contrôle : sortie (volume + périphériques), entrée (niveau + périphériques) et volume par application.
pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as C
import qs.components
import qs.components.controls
import qs.components.containers
import qs.services
import qs.config

Item {
    id: root
    signal back
    readonly property bool muted: Bridge.controls.muted ?? false

    function volumeIcon(level, mute) {
        return mute || level <= 0 ? "volume_off" : level < 34 ? "volume_mute" : level < 67 ? "volume_down" : "volume_up";
    }
    function outputIcon(name) {
        const n = (name || "").toLowerCase();
        if (/casque|headphone|headset|écouteur|ecouteur|airpods|buds|bluetooth/.test(n))
            return "headphones";
        if (/\btv\b|hdmi|moniteur|monitor|display|écran|ecran|nvidia|displayport/.test(n))
            return "tv";
        return "speaker";
    }

    component Section: StyledText {
        Layout.topMargin: Appearance.spacing.smaller
        Layout.leftMargin: 4
        font.pointSize: Appearance.font.size.small
        color: Colours.palette.m3onSurfaceVariant
    }
    component DeviceRow: CcRow {
        id: row
        required property int index
        required property var modelData
        property bool microphone: false
        property int count: 0
        readonly property bool chosen: !!modelData.selected
        Layout.fillWidth: true
        first: index === 0
        last: index === count - 1
        selected: chosen
        headerHeight: 56
        implicitHeight: 56 + (chosen && microphone ? 44 + 12 : 0)
        clip: true
        onClicked: Bridge.action("deviceSelect", {
            id: modelData.id
        })
        Behavior on implicitHeight {
            enabled: !Appearance.reducedEffects
            Anim {
                duration: Appearance.anim.durations.small
            }
        }
        Rectangle {
            id: chip
            x: 12
            y: 10
            width: 36
            height: 36
            radius: 18
            color: row.chosen ? Colours.palette.m3primary : Colours.palette.m3secondaryContainer
            MaterialIcon {
                anchors.centerIn: parent
                text: row.microphone ? "mic" : root.outputIcon(row.modelData.name)
                fill: row.chosen ? 1 : 0
                color: row.chosen ? Colours.palette.m3onPrimary : Colours.palette.m3onSecondaryContainer
            }
        }
        StyledText {
            anchors.left: chip.right
            anchors.leftMargin: Appearance.spacing.normal
            anchors.right: tick.left
            anchors.rightMargin: Appearance.spacing.normal
            y: 28 - height / 2
            text: row.modelData.name
            elide: Text.ElideRight
            font.pointSize: Appearance.font.size.normal
        }
        MaterialIcon {
            id: tick
            objectName: "soundTick"
            anchors.right: parent.right
            anchors.rightMargin: 16
            y: 28 - height / 2
            text: "check"
            color: Colours.palette.m3primary
            opacity: row.chosen ? 1 : 0
            Behavior on opacity {
                Anim {
                    duration: Appearance.anim.durations.small
                }
            }
        }
        CcSlider {
            visible: row.chosen && row.microphone
            x: 12
            y: 56
            width: row.width - 24
            height: 44
            icon: row.modelData.muted ? "mic_off" : "mic"
            source: row.modelData.volume ?? 0
            onMoved: Bridge.action("deviceVolume", {
                id: row.modelData.id,
                value: value
            })
            onIconClicked: Bridge.action("deviceMute", {
                id: row.modelData.id,
                muted: !row.modelData.muted
            })
        }
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: Appearance.spacing.normal
        CcHeader {
            Layout.fillWidth: true
            title: qsTr("Son")
            onBack: root.back()
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
                Section {
                    text: qsTr("Sortie audio")
                }
                CcSlider {
                    objectName: "soundMaster"
                    Layout.fillWidth: true
                    Layout.bottomMargin: Appearance.spacing.small
                    icon: root.volumeIcon(value, root.muted)
                    source: Bridge.controls.volume ?? 0
                    onMoved: Bridge.action("volume", {
                        value: value
                    })
                    onIconClicked: Bridge.action("masterMute", {
                        muted: !root.muted
                    })
                }
                Repeater {
                    id: outRep
                    model: Bridge.audioOutputs
                    DeviceRow {
                        count: outRep.count
                    }
                }
                StyledText {
                    visible: outRep.count === 0
                    Layout.fillWidth: true
                    Layout.leftMargin: 4
                    text: Bridge.audioReady ? qsTr("Aucune sortie audio détectée") : qsTr("Recherche des périphériques…")
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3outline
                }
                Section {
                    Layout.topMargin: Appearance.spacing.normal
                    text: qsTr("Entrée audio")
                }
                Repeater {
                    id: inRep
                    model: Bridge.audioInputs
                    DeviceRow {
                        microphone: true
                        count: inRep.count
                    }
                }
                StyledText {
                    visible: inRep.count === 0
                    Layout.fillWidth: true
                    Layout.leftMargin: 4
                    text: Bridge.audioReady ? qsTr("Aucun microphone connecté") : qsTr("Recherche des périphériques…")
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3outline
                }
                Section {
                    Layout.topMargin: Appearance.spacing.normal
                    text: qsTr("Applications")
                }
                Repeater {
                    id: appRep
                    model: Bridge.audioApplications
                    CcRow {
                        id: app
                        required property int index
                        required property var modelData
                        Layout.fillWidth: true
                        interactive: false
                        first: index === 0
                        last: index === appRep.count - 1
                        implicitHeight: 96
                        ColumnLayout {
                            anchors.fill: parent
                            anchors.margins: 12
                            spacing: Appearance.spacing.smaller
                            RowLayout {
                                Layout.fillWidth: true
                                spacing: Appearance.spacing.smaller
                                Image {
                                    Layout.preferredWidth: 24
                                    Layout.preferredHeight: 24
                                    sourceSize.width: Math.ceil(24 * Screen.devicePixelRatio)
                                    sourceSize.height: Math.ceil(24 * Screen.devicePixelRatio)
                                    source: Bridge.iconUrl(app.modelData.id, Bridge.iconRevision)
                                    asynchronous: true
                                    fillMode: Image.PreserveAspectFit
                                    visible: status === Image.Ready
                                }
                                MaterialIcon {
                                    text: app.modelData.pid === 0 ? "notifications" : "music_note"
                                    visible: !Bridge.iconUrl(app.modelData.id, Bridge.iconRevision)
                                    color: Colours.palette.m3primary
                                }
                                StyledText {
                                    text: app.modelData.name
                                    Layout.fillWidth: true
                                    elide: Text.ElideRight
                                    font.pointSize: Appearance.font.size.normal
                                }
                            }
                            CcSlider {
                                Layout.fillWidth: true
                                icon: app.modelData.muted ? "volume_off" : "volume_up"
                                source: app.modelData.volume ?? 0
                                onMoved: Bridge.action("sessionVolume", {
                                    id: app.modelData.id,
                                    value: value
                                })
                                onIconClicked: Bridge.action("sessionMute", {
                                    id: app.modelData.id,
                                    muted: !app.modelData.muted
                                })
                            }
                        }
                    }
                }
                StyledText {
                    visible: appRep.count === 0
                    Layout.fillWidth: true
                    Layout.leftMargin: 4
                    text: Bridge.audioReady ? qsTr("Aucune application ne joue de son") : qsTr("Recherche des sessions audio…")
                    font.pointSize: Appearance.font.size.small
                    color: Colours.palette.m3outline
                }
            }
        }
    }
}
