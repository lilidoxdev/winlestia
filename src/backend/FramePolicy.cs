// C#12 / FramePolicy.cs: pure decisions for the accent border (all windows, or only the active one).
using System;

namespace PanneauxGlass;

internal static class FramePolicy
{
    // Does an ordinary window (not a themed Explorer window) carry the accent border?
    internal static bool Carries(bool accentBorders, bool activeOnly, IntPtr window, IntPtr active) =>
        accentBorders && (!activeOnly || window == active);

    // Foreground change: window to colour and previous window to restore (Zero = none), plus the window tracked from now on.
    // An ineligible foreground (shell panel, tool or hidden window) leaves everything as it was.
    internal static (IntPtr colour, IntPtr restore, IntPtr tracked) Foreground(
        bool accentBorders,
        bool activeOnly,
        bool eligible,
        IntPtr foreground,
        IntPtr last
    )
    {
        if (!accentBorders || !activeOnly)
            return (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (!eligible || foreground == IntPtr.Zero || foreground == last)
            return (IntPtr.Zero, IntPtr.Zero, last);
        return (foreground, last, foreground);
    }
}
