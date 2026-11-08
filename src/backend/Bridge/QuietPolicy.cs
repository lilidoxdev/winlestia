// C# 12 / QuietPolicy.cs / Décisions pures du mode silencieux plein écran : une fenêtre couvre-t-elle son moniteur,
// et de l'état forcé ou détecté, lequel l'emporte.
using System;

namespace PanneauxGlass;

internal enum QuietForce
{
    Auto, On, Off
}

internal static class QuietPolicy
{
    // Chromium en plein écran laisse la dernière ligne libre (3440x1439 sur 3440x1440) : on tolère quelques pixels.
    internal const int Slack = 2;

    internal static bool Covers(int l, int t, int r, int b, int ml, int mt, int mr, int mb, int slack = Slack) =>
        mr > ml && mb > mt && l <= ml + slack && t <= mt + slack && r >= mr - slack && b >= mb - slack;

    // La détection ne compte que si la préférence est active et si au moins un moniteur affiche une fenêtre en plein écran.
    internal static bool Detected(bool enabled, int fullscreenMonitors) => enabled && fullscreenMonitors > 0;

    // A forced state wins over detection; auto follows detection.
    internal static bool Effective(QuietForce forced, bool detected) => forced switch { QuietForce.On => true, QuietForce.Off => false, _
        => detected };

    internal static string Label(QuietForce forced) => forced switch { QuietForce.On => "on", QuietForce.Off => "off", _ => "auto" };

    internal static bool TryParse(string? text, out QuietForce forced)
    {
        switch ((text ?? "").ToLowerInvariant())
        {
            case "on":
                forced = QuietForce.On;
                return true;
            case "off":
                forced = QuietForce.Off;
                return true;
            case "auto":
                forced = QuietForce.Auto;
                return true;
        }
        forced = QuietForce.Auto;
        return false;
    }
}
