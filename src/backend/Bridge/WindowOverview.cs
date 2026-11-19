// language: C# 12, file: WindowOverview.cs, target: Windows 11 / .NET 8, liste des fenêtres de la Vue d'ensemble : lecture Win32 et
// projection pure
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
namespace PanneauxGlass;

// Une fenêtre telle que lue sur le système, avant filtrage. Left/Top/Right/Bottom : rectangle normal (restauré si réduite).
internal sealed record OverviewRaw(long Hwnd, string Title, string Path, uint Pid, int Left, int Top, int Right, int Bottom, bool Minimized,
    bool Cloaked, bool Primary);

// Ce que le frontend reçoit (clés minuscules, comme le reste du pont).
internal sealed record OverviewItem(
    [property: JsonPropertyName("hwnd")] long Hwnd,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("app")] string App,
    [property: JsonPropertyName("iconId")] string IconId,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("w")] int W,
    [property: JsonPropertyName("h")] int H,
    [property: JsonPropertyName("minimized")] bool Minimized,
    [property: JsonPropertyName("primary")] bool Primary);

internal static class WindowOverview
{
    internal const int MaxWindows = 60;

    // Filtrage, ordre et projection purs. L'entrée est déjà en ordre Z (le plus récent d'abord, comme EnumWindows) : l'ordre est
    // conservé. Écartées : fenêtres masquées (autre bureau virtuel), processus du shell, titres « Panneaux · » / Caelestia,
    // autres écrans (le fond ne couvre que le principal), rectangles dégénérés, doublons de poignée. `resolve` donne (nom d'application, id
    // d'entrée du dock, id d'icône).
    internal static OverviewItem[] Project(IEnumerable<OverviewRaw> windows, ISet<uint> shellPids,
        Func<OverviewRaw, (string App, string Id, string IconId)> resolve)
    {
        var seen = new HashSet<long>();
        var result = new List<OverviewItem>();
        foreach (var w in windows)
        {
            if (!w.Primary || w.Cloaked || shellPids.Contains(w.Pid) || string.IsNullOrWhiteSpace(w.Title)
                || w.Title.StartsWith("Panneaux ·", StringComparison.Ordinal) || w.Title.Contains("Caelestia", StringComparison.Ordinal)
                || w.Right - w.Left <= 1 || w.Bottom - w.Top <= 1 || !seen.Add(w.Hwnd))
                continue;
            var (app, id, iconId) = resolve(w);
            if (string.IsNullOrWhiteSpace(app))
                app = Path.GetFileNameWithoutExtension(w.Path);
            result.Add(new OverviewItem(w.Hwnd, id, w.Title, app, iconId, w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top, w.Minimized,
                w.Primary));
            if (result.Count >= MaxWindows)
                break;
        }
        return result.ToArray();
    }

    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr window, uint message, IntPtr wp, IntPtr lp);

    // Retourne un message d'erreur, ou null. La fenêtre doit exister encore.
    internal static string? Act(long hwnd, string action)
    {
        var handle = new IntPtr(hwnd);
        if (!IsWindow(handle))
            return null;
        if (action == "activate")
        {
            if (TaskbarNative.IsIconic(handle))
                TaskbarNative.ShowWindow(handle, 9);
            TaskbarNative.SetForegroundWindow(handle);
        }
        else if (action == "close" && !PostMessageW(handle, 0x10, IntPtr.Zero, IntPtr.Zero))
            return "Windows a refusé la fermeture de cette fenêtre.";
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WindowPlacementInfo
    {
        public uint Length, Flags, ShowCmd; public Native.Point MinPosition, MaxPosition; public Native.Rect NormalPosition;
    }
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacementInfo placement);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Native.Point point, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);

    // Mêmes fenêtres que TaskbarNative.Applications() (qui écarte déjà les masquées et celles du backend), avec rectangle, état réduit et
    // moniteur. Hors thread UI.
    internal static List<OverviewRaw> Read()
    {
        var result = new List<OverviewRaw>();
        IntPtr primary = MonitorFromPoint(default, 1);
        foreach (var app in TaskbarNative.Applications())
        {
            try
            {
                IntPtr h = app.Handle;
                bool minimized = TaskbarNative.IsIconic(h);
                var rect = default(Native.Rect);
                if (minimized)
                {
                    var placement = new WindowPlacementInfo { Length = (uint)Marshal.SizeOf<WindowPlacementInfo>() };
                    if (GetWindowPlacement(h, ref placement))
                        rect = placement.NormalPosition;
                }
                else
                    TaskbarNative.GetWindowRect(h, out rect);
                GetWindowThreadProcessId(h, out uint pid);
                bool cloaked = DwmGetWindowAttribute(h, 14, out int c, 4) == 0 && c != 0;
                bool onPrimary = MonitorFromWindow(h, 2) == primary;
                result.Add(new OverviewRaw(h.ToInt64(), app.Title, app.Path, pid, rect.Left, rect.Top, rect.Right, rect.Bottom, minimized,
                    cloaked, onPrimary));
            }
            catch (Exception)
            {
                // Fenêtre fermée pendant la lecture : ignorée.
            }
        }
        return result;
    }
}
