import QtQuick
import QtQuick.Layouts
import qs.components
import qs.config
import qs.services
import ".."

// Bloc `calendar` : variant month|day, firstDay monday|sunday, nav (chevrons précédent/suivant visibles au survol, glissement de 24 px
// au changement de mois ; le mois courant revient quand la carte est réaffichée). Se met à jour à la minute (Time.date), jamais plus.
Item {
    id: block

    property var spec: null
    property var ctx: ({})
    property var host: null
    readonly property real sc: ctx && ctx.scale > 0 ? ctx.scale : 1

    readonly property string variant: spec ? String(Bridge.winjets.resolve(spec.variant, ctx) ?? "month") : "month"
    readonly property bool sundayFirst: spec ? Bridge.winjets.resolve(spec.firstDay, ctx) === "sunday" : false
    readonly property bool nav: spec ? Bridge.winjets.resolve(spec.nav, ctx) === true : false
    readonly property date today: Time.date
    // Décalage de mois choisi par l'utilisateur (0 = mois courant) et sens du dernier changement (pour le glissement).
    property int monthOffset: 0
    property int direction: 1
    readonly property date view: new Date(today.getFullYear(), today.getMonth() + monthOffset, 1)
    readonly property int offset: {
        const first = view.getDay(); // 0 = dimanche
        return sundayFirst ? first : (first + 6) % 7;
    }
    readonly property int days: new Date(view.getFullYear(), view.getMonth() + 1, 0).getDate()
    readonly property var heads: {
        const out = [];
        for (let i = 0; i < 7; i++)
            out.push(Qt.locale().dayName((sundayFirst ? i : i + 1) % 7, Locale.NarrowFormat));
        return out;
    }
    readonly property string monthTitle: Qt.locale().monthName(view.getMonth(), Locale.LongFormat) + " " + view.getFullYear()
    readonly property int dMedium: Bridge.reducedEffects ? 0 : 200
    readonly property bool motionOk: !Bridge.reducedEffects && visible && (!host || host.live)

    function step(delta: int): void {
        if (!nav || (host && host.editing))
            return;
        direction = delta;
        monthOffset += delta;
        if (motionOk)
            slide.restart();
    }

    // Réaffichage de la carte : retour au mois courant.
    onVisibleChanged: if (visible)
        monthOffset = 0
    Connections {
        target: block.host
        ignoreUnknownSignals: true

        function onLiveChanged(): void {
            if (block.host.live)
                block.monthOffset = 0;
        }
    }

    implicitWidth: (variant === "day" ? 96 : 224) * sc
    implicitHeight: (variant === "day" ? 96 : 190) * sc

    ColumnLayout {
        visible: block.variant === "day"
        anchors.fill: parent
        spacing: 0

        StyledText {
            Layout.fillWidth: true
            text: Qt.locale().dayName(block.today.getDay(), Locale.LongFormat)
            color: WinjetTheme.accent
            font.pointSize: (Math.round(12 * block.sc)) * 0.75
            font.weight: Font.DemiBold
            font.capitalization: Font.AllUppercase
            font.letterSpacing: 1.2
            elide: Text.ElideRight
        }
        StyledText {
            Layout.fillWidth: true
            Layout.fillHeight: true
            text: String(block.today.getDate())
            color: WinjetTheme.textPrimary
            font.pointSize: (Math.round(96 * block.sc)) * 0.75
            font.weight: Font.Light
            font.letterSpacing: -2.4 * block.sc
            fontSizeMode: Text.Fit
            minimumPixelSize: 16 * block.sc
            verticalAlignment: Text.AlignVCenter
        }
        StyledText {
            Layout.fillWidth: true
            text: Qt.locale().monthName(block.today.getMonth(), Locale.LongFormat) + " " + block.today.getFullYear()
            color: Qt.alpha(WinjetTheme.textPrimary, 0.6)
            font.pointSize: (Math.round(12 * block.sc)) * 0.75
            elide: Text.ElideRight
        }
    }

    ColumnLayout {
        visible: block.variant !== "day"
        anchors.fill: parent
        spacing: Math.round(6 * block.sc)

        RowLayout {
            Layout.fillWidth: true
            spacing: 0

            StyledText {
                Layout.fillWidth: true
                text: block.monthTitle
                color: WinjetTheme.textPrimary
                font.pointSize: (Math.round(20 * block.sc)) * 0.75
                font.weight: Font.Bold
                font.capitalization: Font.Capitalize
                elide: Text.ElideRight
                transform: Translate {
                    id: titleShift
                }
            }

            Repeater {
                model: block.nav ? [-1, 1] : []

                Item {
                    id: chevron

                    required property int modelData

                    Layout.preferredWidth: 28 * block.sc
                    Layout.preferredHeight: 28 * block.sc
                    // Visibles au survol de la carte seulement ; un fondu de 160 ms.
                    opacity: hov.hovered || chevArea.containsMouse ? 1 : 0
                    visible: opacity > 0

                    Behavior on opacity {
                        enabled: !Bridge.reducedEffects
                        NumberAnimation {
                            duration: WinjetTheme.motionFast || 160
                            easing.type: Easing.OutCubic
                        }
                    }

                    Rectangle {
                        anchors.fill: parent
                        radius: width / 2
                        color: Qt.alpha(WinjetTheme.textPrimary, chevArea.containsMouse ? 0.12 : 0)
                    }
                    MaterialIcon {
                        anchors.centerIn: parent
                        text: chevron.modelData < 0 ? "chevron_left" : "chevron_right"
                        color: WinjetTheme.textPrimary
                        font.pointSize: Math.max(8, 14 * block.sc)
                    }
                    MouseArea {
                        id: chevArea

                        anchors.fill: parent
                        hoverEnabled: true
                        cursorShape: Qt.PointingHandCursor
                        onClicked: block.step(chevron.modelData)
                    }
                }
            }
        }

        Item {
            id: holder

            Layout.fillWidth: true
            Layout.fillHeight: true
            clip: true

            GridLayout {
                id: monthGrid

                anchors.fill: parent
                columns: 7
                rowSpacing: 0
                columnSpacing: 0
                transform: Translate {
                    id: gridShift
                }

                Repeater {
                    model: block.heads

                    StyledText {
                        required property string modelData
                        required property int index

                        Layout.fillWidth: true
                        horizontalAlignment: Text.AlignHCenter
                        text: modelData
                        color: {
                            const weekend = block.sundayFirst ? (index === 0 || index === 6) : index >= 5;
                            return Qt.alpha(WinjetTheme.textPrimary, weekend ? 0.3 : 0.5);
                        }
                        font.pointSize: (Math.round(11 * block.sc)) * 0.75
                        font.weight: Font.DemiBold
                    }
                }

                Repeater {
                    model: 42

                    Item {
                        id: cell

                        required property int index
                        readonly property int day: index - block.offset + 1
                        readonly property bool inMonth: day >= 1 && day <= block.days
                        readonly property bool isToday: inMonth && block.monthOffset === 0 && day === block.today.getDate()
                        readonly property int col: index % 7
                        readonly property bool weekend: block.sundayFirst ? (col === 0 || col === 6) : col >= 5

                        Layout.fillWidth: true
                        Layout.fillHeight: true
                        Layout.preferredHeight: 1

                        Rectangle {
                            anchors.centerIn: parent
                            width: Math.min(parent.width, parent.height)
                            height: width
                            radius: width / 2
                            color: WinjetTheme.accent
                            visible: cell.isToday
                        }
                        StyledText {
                            anchors.centerIn: parent
                            visible: cell.inMonth
                            text: String(cell.day)
                            color: cell.isToday ? Colours.palette.m3onPrimary : Qt.alpha(WinjetTheme.textPrimary, cell.weekend ? 0.45 : 1)
                            font.pointSize: (Math.round(13 * block.sc)) * 0.75
                            font.weight: cell.isToday ? Font.Bold : Font.DemiBold
                        }
                    }
                }
            }
        }
    }

    HoverHandler {
        id: hov
    }

    // Changement de mois : glissement de 24 px (dans le sens du déplacement) et fondu, 200 ms.
    ParallelAnimation {
        id: slide

        NumberAnimation {
            target: gridShift
            property: "x"
            from: 24 * block.direction * block.sc
            to: 0
            duration: block.dMedium
            easing.type: Easing.OutCubic
        }
        NumberAnimation {
            target: monthGrid
            property: "opacity"
            from: 0
            to: 1
            duration: block.dMedium
            easing.type: Easing.OutCubic
        }
        NumberAnimation {
            target: titleShift
            property: "x"
            from: 24 * block.direction * block.sc
            to: 0
            duration: block.dMedium
            easing.type: Easing.OutCubic
        }
    }

    ActionArea {
        spec: block.spec
        host: block.host
    }
}
