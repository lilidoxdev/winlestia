// language: C# 12, file: LayoutRules.cs, target: Windows 11 / .NET 8 WPF, calculs purs de disposition de l'espace
using System;
using System.Windows;

namespace PanneauxGlass;

// Résultat du calcul : zone des panneaux, barre d'outils centrée, largeur de la bande de sites et quatre cibles par défaut.
internal readonly record struct WorkspaceLayout(Rect Panel, double ToolbarX, double ToolbarY, double ToolbarWidth, double SideWidth,
    Rect[] Targets);

// Extraites d'Arrange : le Coordinator lit l'écran et les fenêtres, ces règles ne dépendent que de nombres.
internal static class LayoutRules
{
    // En dessous de cette largeur (unités WPF), les commandes et l'état du PC ne tiennent pas côte à côte.
    internal const double FitsBothWidth = 1700;
    internal const double ToolbarHeight = 64;
    internal static bool FitsBoth(double workWidthPixels, double dpi) => workWidthPixels / dpi >= FitsBothWidth;
    // La marge demandée est plafonnée à 4,5 % de la largeur pour ne pas écraser les panneaux sur un petit écran.
    internal static WorkspaceLayout Detailed(Rect work, double margin, double gap, double topRatio, double leftRatio)
    {
        double sx = work.X, sy = work.Y, sw = work.Width, sh = work.Height;
        margin = Math.Min(margin, sw * .045);
        double full = sw - margin * 2, top = sy + 108, bottom = sy + sh - margin;
        var panel = new Rect(sx + margin, top, full, Math.Max(310, bottom - top));
        double usable = bottom - top - gap, h1 = usable * topRatio, h2 = usable - h1;
        double left = (full - gap) * leftRatio, right = full - gap - left;
        double toolbarWidth = Math.Min(1130, Math.Max(780, full * .56)), side = Math.Max(110, (full - toolbarWidth - 32) / 2);
        var targets = new[]
        {
            new Rect(sx + margin, top, left, h1),
            new Rect(sx + margin + left + gap, top, right, h1),
            new Rect(sx + margin, top + h1 + gap, left, h2),
            new Rect(sx + margin + left + gap, top + h1 + gap, right, h2)
        };
        return new WorkspaceLayout(panel, sx + (sw - toolbarWidth) / 2, sy + 24, toolbarWidth, side, targets);
    }
}
