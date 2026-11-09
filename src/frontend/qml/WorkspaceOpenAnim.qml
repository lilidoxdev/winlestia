// Animation d'ouverture : échelle + fondu avec léger dépassement, régions en cascade.
// Extrait de WorkspaceBrowser.qml (pur découpage) : les ids du parent sont reçus en propriétés requises.
import QtQuick
import QtQuick.Layouts
import qs.components
import qs.components.controls
import qs.services
import qs.config

ParallelAnimation {
    id: openAnim
    required property var browser
    required property var grid
    NumberAnimation {
        target: browser
        property: "pPanel"
        from: 0
        to: 1
        duration: Appearance.anim.durations.expressiveFastSpatial + 80
        easing.type: Easing.BezierSpline
        easing.bezierCurve: Appearance.anim.curves.expressiveFastSpatial
    }
    SequentialAnimation {
        PauseAnimation {
            duration: browser.fx ? 30 : 0
        }
        NumberAnimation {
            target: browser
            property: "pSide"
            from: 0
            to: 1
            duration: Appearance.anim.durations.normal
            easing.type: Easing.BezierSpline
            easing.bezierCurve: Appearance.anim.curves.emphasizedDecel
        }
    }
    SequentialAnimation {
        PauseAnimation {
            duration: browser.fx ? 80 : 0
        }
        NumberAnimation {
            target: browser
            property: "pHead"
            from: 0
            to: 1
            duration: Appearance.anim.durations.normal
            easing.type: Easing.BezierSpline
            easing.bezierCurve: Appearance.anim.curves.emphasizedDecel
        }
    }
    SequentialAnimation {
        PauseAnimation {
            duration: browser.fx ? 130 : 0
        }
        NumberAnimation {
            target: browser
            property: "pBody"
            from: 0
            to: 1
            duration: Appearance.anim.durations.normal
            easing.type: Easing.BezierSpline
            easing.bezierCurve: Appearance.anim.curves.emphasizedDecel
        }
    }
    NumberAnimation {
        target: grid
        property: "reveal"
        from: browser.fx ? 0 : 1
        to: 1
        duration: browser.fx ? 900 : 1
    }
}
