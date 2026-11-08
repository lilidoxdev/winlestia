// language: C# 12, file: TickRules.cs, target: Windows 11 / .NET 8, décisions pures de la boucle d'horloge (poignée latérale, repli)
using System;

namespace PanneauxGlass;

// Extraites de Tick() : la boucle lit la souris et les fenêtres, ces règles ne dépendent que de nombres et de dates.
internal static class TickRules
{
    internal const int EdgeBand = 4;
    internal const int EdgeMargin = 24;
    internal const int EdgeDwellMs = 150;
    internal const int RailAwayMs = 650;
    internal const int BoardAwayMs = 1800;
    internal const double RailGap = 12;
    // Bande de 4 px sur le bord choisi, hors des 24 px de coin : sans cela le coin d'écran déclencherait la poignée par accident.
    internal static bool AtEdge(string side, int x, int y, int boundsLeft, int boundsRight, int workTop, int workBottom)
        => (side == "right" ? x >= boundsRight - EdgeBand && x <= boundsRight : x <= boundsLeft + EdgeBand && x >= boundsLeft)
            && y > workTop + EdgeMargin
            && y < workBottom - EdgeMargin;
    // La poignée n'apparaît qu'après un court maintien (>=) pour ignorer le passage de la souris vers un autre écran.
    internal static bool EdgeDwellReached(DateTime since, DateTime now) => (now - since).TotalMilliseconds >= EdgeDwellMs;
    internal static bool RailAwayExceeded(DateTime since, DateTime now) => (now - since).TotalMilliseconds > RailAwayMs;
    internal static bool BoardAwayExceeded(DateTime since, DateTime now) => (now - since).TotalMilliseconds > BoardAwayMs;
    // Le repli automatique est suspendu dès que l'utilisateur interagit, épingle ou ouvre une boîte de dialogue.
    internal static bool AutoHideSuspended(bool detailed, bool interacting, bool pinned, bool settingsOpen, bool popupOpen, bool autoHide)
        => !detailed || interacting || pinned || settingsOpen || popupOpen || !autoHide;
    // Position et hauteur de la poignée en unités WPF (pixels / échelle), centrée verticalement dans la zone de travail.
    internal static (double X, double Y, double Height) RailPlacement(string side, int boundsLeft, int boundsRight, int workTop,
        int workHeight, double scale, double railWidth, double heightRatio)
    {
        double height = workHeight / scale * heightRatio;
        double x = side == "right" ? boundsRight / scale - railWidth - RailGap : boundsLeft / scale + RailGap;
        return (x, workTop / scale + (workHeight / scale - height) / 2, height);
    }
    // Rectangle en unités WPF comparé à un point en pixels : la fenêtre est ramenée en pixels avec le DPI de la fenêtre.
    internal static bool Inside(double left, double top, double width, double height, double dpi, int x, int y)
        => x >= left * dpi && y >= top * dpi && x < (left + width) * dpi && y < (top + height) * dpi;
}
