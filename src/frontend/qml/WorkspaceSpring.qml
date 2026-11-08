// Animation à ressort commune au navigateur d'espace (durées réduites si « Effets allégés »).
import QtQuick
import QtQuick.Layouts
import qs.components
import qs.services
import qs.config

Anim {
    duration: Appearance.reducedEffects ? Appearance.anim.durations.small : Appearance.anim.durations.expressiveFastSpatial
    easing.bezierCurve: Appearance.reducedEffects ? Appearance.anim.curves.standard : Appearance.anim.curves.expressiveFastSpatial
}
