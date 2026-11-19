import ".."
import qs.services
import qs.config
import QtQuick
import QtQuick.Effects

RectangularShadow {
    property int level
    property real dp: [0, 1, 3, 6, 8, 12][level]

    // Les enfants (ex. contenu de Menu) doivent rester visibles : seule l'ombre elle-même est retirée.
    color: Appearance.reducedEffects ? "transparent" : Qt.alpha(Colours.palette.m3shadow, 0.7)
    blur: (dp * 5) ** 0.7
    spread: -dp * 0.3 + (dp * 0.1) ** 2
    offset.y: dp / 2

    Behavior on dp {
        Anim {}
    }
}
