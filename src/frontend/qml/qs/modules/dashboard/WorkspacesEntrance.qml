// Entrée en cascade. À placer comme enfant de l'élément animé et à lier opacity/scale/transform à alpha/k/dy.
// Rejoue depuis 0 chaque fois que `shown` passe à vrai ; s'arrête quand masqué. Rien ne tourne tant que l'onglet n'est pas affiché.
import QtQuick
import QtQuick.Layouts
import QtQuick.Shapes
import qs.components
import qs.components.controls
import qs.services
import qs.config

Item {
    id: ent
    required property var host
    property int order: 0
    property real reveal: 0
    readonly property real alpha: Math.min(1, reveal * 1.8)
    readonly property real dy: host.fx ? (1 - reveal) * 22 : 0
    readonly property real k: host.fx ? 0.9 + 0.1 * reveal : 1
    visible: false
    function play(): void {
        seq.stop();
        reveal = 0;
        if (host.shown)
            seq.start();
    }
    Component.onCompleted: play()
    Connections {
        target: root
        function onShownChanged(): void {
            if (host.shown)
                ent.play();
            else
                seq.stop();
        }
    }
    SequentialAnimation {
        id: seq
        PauseAnimation {
            duration: host.fx ? ent.order * 35 : 0
        }
        NumberAnimation {
            target: ent
            property: "reveal"
            from: 0
            to: 1
            duration: Appearance.anim.durations.expressiveDefaultSpatial
            easing.type: Easing.BezierSpline
            easing.bezierCurve: host.fx ? Appearance.anim.curves.expressiveDefaultSpatial : Appearance.anim.curves.standard
        }
    }
}
