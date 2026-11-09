// Spring-like number animation (overshoot curve). Reduced effects: plain standard curve, durations already scaled.
import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes
import qs.components
import qs.components.controls
import qs.services
import qs.config

NumberAnimation {
    duration: Appearance.anim.durations.expressiveFastSpatial
    easing.type: Easing.BezierSpline
    easing.bezierCurve: Appearance.reducedEffects ? Appearance.anim.curves.standard : Appearance.anim.curves.expressiveFastSpatial
}
