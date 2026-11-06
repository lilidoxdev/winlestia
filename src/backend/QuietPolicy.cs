// C# 12 / QuietPolicy.cs / Pure decisions of the fullscreen quiet mode: does a window cover its monitor, and which of forced / detected wins.
using System;

namespace PanneauxGlass;

internal enum QuietForce
{
    Auto, On, Off
}

internal static class QuietPolicy
{
    // Chromium fullscreen leaves the bottom row free (3440x1439 on a 3440x1440 monitor): tolerate a few px.
    internal const int Slack = 2;

    // Does the window rectangle (l,t,r,b) cover the monitor rectangle (ml,mt,mr,mb)?
    internal static bool Covers(int l, int t, int r, int b, int ml, int mt, int mr, int mb, int slack = Slack) =>
        mr > ml && mb > mt && l <= ml + slack && t <= mt + slack && r >= mr - slack && b >= mb - slack;

    // Detection counts only when the preference is on and at least one monitor shows a fullscreen window.
    internal static bool Detected(bool enabled, int fullscreenMonitors) => enabled && fullscreenMonitors > 0;

    // A forced state wins over detection; auto follows detection.
    internal static bool Effective(QuietForce forced, bool detected) => forced switch { QuietForce.On => true, QuietForce.Off => false, _ => detected };

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
