// État réel des icônes du Bureau, lu sur la fenêtre d'Explorer (SysListView32 sous SHELLDLL_DefView).
// HideIcons dans le registre ne suffit pas : Explorer peut garder les icônes affichées malgré FWF_NOICONS (vu le 2026-11-06).
// Repli : la commande native « Afficher les icônes du Bureau » (WM_COMMAND 0x7402), qui bascule l'état réel.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace PanneauxGlass;

internal static class DesktopView
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);

    static IntPtr DefView()
    {
        foreach (var cls in new[] { "Progman", "WorkerW" })
        {
            IntPtr w = IntPtr.Zero;
            while ((w = FindWindowEx(IntPtr.Zero, w, cls, null)) != IntPtr.Zero)
            {
                IntPtr dv = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero)
                    return dv;
            }
        }
        return IntPtr.Zero;
    }

    // null : Bureau introuvable (Explorer absent ou en redémarrage).
    internal static bool? IconsVisible()
    {
        IntPtr dv = DefView();
        if (dv == IntPtr.Zero)
            return null;
        IntPtr lv = FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
        return lv != IntPtr.Zero && IsWindowVisible(lv);
    }

    // Aligne l'état réel sur `hidden`. Renvoie true si l'état final correspond (ou si le Bureau est introuvable).
    internal static bool Enforce(bool hidden)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var visible = IconsVisible();
            if (visible == null)
                return true;
            if (visible.Value != hidden)
                return true;
            SendMessageTimeout(DefView(), 0x0111, (IntPtr)0x7402, IntPtr.Zero, 0x0002, 1000, out _);
            Thread.Sleep(250);
        }
        var final = IconsVisible();
        return final == null || final.Value != hidden;
    }
}
