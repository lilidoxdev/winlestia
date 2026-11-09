// ExplorerAppearance : bordure et couleurs DWM des fenêtres de l'Explorateur.

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows.Threading;
using D = System.Drawing;
using G = System.Drawing.Drawing2D;

namespace PanneauxGlass;

internal sealed partial class ExplorerAppearance
{
    // Mode « actif seulement » : la seule fenêtre portant la bordure (la dernière colorée, pas de liste).
    IntPtr activeFrame;
    bool Eligible(IntPtr h, out bool explorer)
    {
        explorer = TaskbarNative.ClassName(h) == "CabinetWClass";
        if (Native.Owns(h)
            || !TaskbarNative.IsWindowVisible(h)
            || TaskbarNative.IsIconic(h)
            || (Style(h, -16).ToInt64() & 0x40000000) != 0
            || (Native.ExtendedStyle(h) & 0x80) != 0
            || string.IsNullOrWhiteSpace(Native.Title(h)))
            return false;
        if ((Style(h, -16).ToInt64() & 0x00c40000) == 0)
            return false;
        GetWindowThreadProcessId(h, out uint pid);
        return pid != ExcludedProcess;
    }
    static bool ThemedExplorer(IntPtr h) => Store.State.ExplorerTheme && TaskbarNative.ClassName(h) == "CabinetWClass";
    // Événement de premier plan (même hook, pas de nouveau minuteur) : colore la nouvelle fenêtre active, restaure la précédente.
    // Les
    // refus
    // de DWM restent silencieux.
    void ForegroundChanged(IntPtr h)
    {
        bool on = Store.State.AccentBorders && Store.State.ActiveBorderOnly;
        var plan = FramePolicy.Foreground(Store.State.AccentBorders, Store.State.ActiveBorderOnly, on && Eligible(h, out _), h,
            activeFrame);
        activeFrame = plan.tracked;
        if (plan.restore != IntPtr.Zero && !ThemedExplorer(plan.restore))
            RestoreFrame(plan.restore);
        if (plan.colour != IntPtr.Zero)
            ColourFrame(plan.colour, TaskbarNative.ClassName(plan.colour) == "CabinetWClass");
    }
    // Passe ponctuelle demandée par un changement de réglage ou de palette (thread UI, rien au repos).
    internal void RefreshFrames()
    {
        if (!disposed)
            timer.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) FrameTick(); }));
    }
    void FrameTick()
    {
        if (disposed)
            return;
        if (!Store.State.ExplorerTheme && !Store.State.AccentBorders)
        {
            activeFrame = IntPtr.Zero;
            RestoreFrames();
            return;
        }
        var alive = new HashSet<IntPtr>();
        var sig = new List<string>();
        bool activeOnly = Store.State.AccentBorders && Store.State.ActiveBorderOnly;
        if (!activeOnly || (activeFrame != IntPtr.Zero && !Native.IsWindow(activeFrame)))
            activeFrame = IntPtr.Zero;
        if (activeOnly && activeFrame == IntPtr.Zero)
        {
            var foreground = Native.GetForegroundWindow();
            if (foreground != IntPtr.Zero && Eligible(foreground, out _))
                activeFrame = foreground;
        }   // préférer la vraie fenêtre de premier plan au sommet de l'ordre Z
        TaskbarNative.EnumWindows((h, _) =>
        {
            if (!Eligible(h, out bool explorer))
                return true;
            if (!explorer && !Store.State.AccentBorders)
                return true;
            bool themed = explorer && Store.State.ExplorerTheme;
            if (activeOnly && !themed && activeFrame == IntPtr.Zero)
                activeFrame = h;
            if (FramePolicy.Carries(true, activeOnly && !themed, h, activeFrame))
            {
                alive.Add(h);
                ColourFrame(h, explorer);
            }
            if (explorer)
                sig.Add(h + "|" + Native.Title(h));
            return true;
        }, IntPtr.Zero);
        foreach (var h in frames.Keys.Where(h => !alive.Contains(h)).ToArray())
            RestoreFrame(h);
        sig.Sort(StringComparer.Ordinal);
        string now = string.Join("|;", sig);
        if (now != windowSignature)
        {
            windowSignature = now;
            if (now.Length > 0)
                RequestScan();
        }
        if (DateTime.UtcNow >= scanDue)
        {
            scanDue = DateTime.MaxValue;
            ScanVisibleFolders();
        }
    }
    internal bool ColourFrame(IntPtr h, bool explorer)
    {
        GetWindowThreadProcessId(h, out uint pid);
        if (frames.ContainsKey(h) && frameOwners[h] != pid)
        {
            frames.Remove(h);
            frameOwners.Remove(h);
            captionFrames.Remove(h);
        }
        if (!frames.ContainsKey(h))
        {
            var original = new uint[3];
            for (int i = 0; i < 3; i++)
                if (DwmGetWindowAttribute(h, 34 + i, out original[i], 4) != 0)
                    original[i] = 0xffffffff;
            frames[h] = original;
            frameOwners[h] = pid;
        }
        uint border = Colour(accent);
        bool accepted = DwmSetWindowAttribute(h, 34, ref border, 4) >= 0;
        if (explorer && Store.State.ExplorerTheme)
        {
            captionFrames.Add(h);
            uint bg = Colour(background), fg = Colour(text);
            DwmSetWindowAttribute(h, 35, ref bg, 4);
            DwmSetWindowAttribute(h, 36, ref fg, 4);
        }
        else if (captionFrames.Remove(h))
        {
            for (int i = 1; i < 3; i++)
            {
                uint original = frames[h][i];
                DwmSetWindowAttribute(h, 34 + i, ref original, 4);
            }
        }
        return accepted;
    }
    internal bool RestoreFrame(IntPtr h)
    {
        if (!frames.Remove(h, out var original))
            return true;
        bool restored = true;
        GetWindowThreadProcessId(h, out uint pid);
        bool captions = captionFrames.Remove(h);
        if (frameOwners.Remove(h, out uint owner) && pid == owner && Native.IsWindow(h))
            for (int i = 0; i < (captions ? 3 : 1); i++)
            {
                uint value = original[i];
                restored &= DwmSetWindowAttribute(h, 34 + i, ref value, 4) >= 0;
            }
        return restored;
    }
    void RestoreFrames()
    {
        foreach (var h in frames.Keys.ToArray())
            RestoreFrame(h);
    }
}
