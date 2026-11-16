import QtQuick
import qs.services
import qs.config

// Marque Winlestia : trois facettes en « W » qui se chevauchent + trois satellites. Apparition échelonnée, puis respiration et flottement lents.
Item {
    id: root

    readonly property real stagger: 60
    readonly property bool still: Appearance.reducedEffects
    readonly property bool live: visible && !still
    readonly property real satelliteBob: height * 0.035
    // Facettes : position / taille relatives, forme, couleur, opacité finale.
    readonly property var facets: [
        {
            "x": 0.0,
            "y": 0.12,
            "w": 0.46,
            "h": 0.62,
            "colour": Colours.palette.m3primary,
            "alpha": 0.95
        },
        {
            "x": 0.38,
            "y": 0.12,
            "w": 0.46,
            "h": 0.62,
            "colour": Colours.palette.m3secondary,
            "alpha": 0.85
        },
        {
            "x": 0.19,
            "y": 0.3,
            "w": 0.46,
            "h": 0.62,
            "colour": Colours.palette.m3tertiary,
            "alpha": 0.8
        },
        {
            "x": 0.3,
            "y": 0.0,
            "w": 0.24,
            "h": 0.22,
            "colour": Colours.palette.m3primaryContainer,
            "alpha": 0.9
        }
    ]
    // Satellites : centre relatif, diamètre relatif, durée du flottement.
    readonly property var satellites: [
        {
            "x": 0.9,
            "y": 0.2,
            "d": 0.06,
            "period": 2600
        },
        {
            "x": 0.95,
            "y": 0.46,
            "d": 0.08,
            "period": 3100
        },
        {
            "x": 0.9,
            "y": 0.74,
            "d": 0.1,
            "period": 3700
        }
    ]

    implicitWidth: 120
    implicitHeight: 120

    // Le côté du carré de dessin ; le contenu y est centré.
    Item {
        id: art

        readonly property real side: Math.min(root.width, root.height)

        anchors.centerIn: parent
        width: side
        height: side

        SequentialAnimation on scale {
            running: root.live
            loops: Animation.Infinite

            NumberAnimation {
                to: 1.03
                duration: Appearance.anim.durations.extraLarge * 2
                easing.type: Easing.InOutSine
            }
            NumberAnimation {
                to: 1
                duration: Appearance.anim.durations.extraLarge * 2
                easing.type: Easing.InOutSine
            }
        }

        Repeater {
            model: root.facets

            LockShape {
                id: facet

                required property var modelData
                required property int index

                x: modelData.x * art.side
                y: modelData.y * art.side
                width: modelData.w * art.side
                height: modelData.h * art.side
                preset: "gem"
                fillColour: Colours.solid(modelData.colour)
                scale: root.still ? 1 : 0
                opacity: root.still ? modelData.alpha : 0

                SequentialAnimation {
                    running: !root.still

                    PauseAnimation {
                        duration: facet.index * root.stagger
                    }
                    ParallelAnimation {
                        NumberAnimation {
                            target: facet
                            property: "scale"
                            to: 1
                            duration: Appearance.anim.durations.expressiveDefaultSpatial
                            easing.bezierCurve: Appearance.anim.curves.expressiveDefaultSpatial
                        }
                        NumberAnimation {
                            target: facet
                            property: "opacity"
                            to: facet.modelData.alpha
                            duration: Appearance.anim.durations.expressiveEffects
                            easing.type: Easing.OutCubic
                        }
                    }
                }
            }
        }

        Repeater {
            model: root.satellites

            Rectangle {
                id: sat

                required property var modelData
                required property int index

                width: modelData.d * art.side
                height: width
                radius: width / 2
                x: modelData.x * art.side - width / 2
                y: modelData.y * art.side - height / 2
                color: Colours.solid(Colours.palette.m3primary)
                scale: root.still ? 1 : 0

                transform: Translate {
                    id: bob
                }

                SequentialAnimation {
                    running: !root.still

                    PauseAnimation {
                        duration: (root.facets.length + sat.index) * root.stagger
                    }
                    NumberAnimation {
                        target: sat
                        property: "scale"
                        to: 1
                        duration: Appearance.anim.durations.expressiveFastSpatial
                        easing.bezierCurve: Appearance.anim.curves.expressiveFastSpatial
                    }
                }

                SequentialAnimation {
                    running: root.live
                    loops: Animation.Infinite

                    PauseAnimation {
                        duration: sat.index * 500
                    }
                    NumberAnimation {
                        target: bob
                        property: "y"
                        from: root.satelliteBob
                        to: -root.satelliteBob
                        duration: sat.modelData.period
                        easing.type: Easing.InOutSine
                    }
                    NumberAnimation {
                        target: bob
                        property: "y"
                        to: root.satelliteBob
                        duration: sat.modelData.period
                        easing.type: Easing.InOutSine
                    }
                }
            }
        }
    }
}
