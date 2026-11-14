// C# 12 / SpectrumPolicy.cs / Décision pure : la capture du spectre audio sert-elle une surface visible (page Média du tableau
// de bord ou Pilule) et le lecteur joue-t-il.
namespace PanneauxGlass;

internal static class SpectrumPolicy
{
    // Une surface consomme le spectre : la page Média du tableau de bord ouvert, ou la Pilule qui le demande.
    internal static bool Surface(bool dashboardActive, int page, bool pill) => (dashboardActive && page == 1) || pill;

    // La capture ne tourne que si une surface est visible ET que le lecteur joue ; sinon la cible 0 détruit la capture.
    internal static bool Wanted(bool dashboardActive, int page, bool pill, bool playing) =>
        playing && Surface(dashboardActive, page, pill);
}
