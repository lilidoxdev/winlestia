// C# 12 / FramePolicy.cs : décisions pures pour la bordure d'accent (toutes les fenêtres, ou seulement l'active).
using System;
namespace PanneauxGlass;

internal static class FramePolicy
{
    internal static bool Carries(bool accentBorders, bool activeOnly, IntPtr window, IntPtr active) => accentBorders && (!activeOnly
        || window == active);
    // Changement de premier plan : fenêtre à colorer, précédente à restaurer (Zero = aucune), puis fenêtre suivie désormais.
    // Un premier plan inéligible (panneau du shell, outil, fenêtre masquée) laisse tout en l'état.
    internal static (IntPtr colour, IntPtr restore, IntPtr tracked) Foreground(bool accentBorders, bool activeOnly, bool eligible,
        IntPtr foreground, IntPtr last)
    {
        if (!accentBorders || !activeOnly)
            return (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (!eligible || foreground == IntPtr.Zero || foreground == last)
            return (IntPtr.Zero, IntPtr.Zero, last);
        return (foreground, last, foreground);
    }
}
