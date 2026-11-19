// language: C# 12, file: TickController.cs, target: Windows 11 / .NET 8 WPF, boucle d'horloge, poignée latérale et repli automatique
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal interface ITickController
{
    void Tick();
}

// Rappels vers le Coordinator : la boucle décide, le Coordinator ouvre et replie.
internal sealed record TickHost(Func<IEnumerable<Window>> HoverWindows, Func<IEnumerable<Window>> AllWindows, Action PointerTick,
    Action<bool> SetTaskbar, Action Open, Action Collapse, Func<bool> QtIdle);

internal sealed class TickController : ITickController
{
    readonly ShellState state;
    readonly GlassWindow rail;
    readonly TickHost host;
    Native.Point lastMouse;
    IntPtr lastForeground;
    string railScreen = "";
    internal TickController(ShellState state, GlassWindow rail, TickHost host)
    {
        this.state = state;
        this.rail = rail;
        this.host = host;
    }
    public void Tick()
    {
        try
        {
            if (Signals() || host.QtIdle())
                return;
            host.PointerTick();
            if (state.Open && TickRules.AutoHideSuspended(state.Detailed, state.Interacting, state.Pinned, state.SettingsOpen,
                state.PopupOpen, Store.State.AutoHide))
                return;
            if (!Native.GetCursorPos(out var mouse))
                return;
            IntPtr foreground = Native.GetForegroundWindow();
            bool stationary = mouse.X == lastMouse.X && mouse.Y == lastMouse.Y && foreground == lastForeground;
            lastMouse = mouse;
            lastForeground = foreground;
            if (foreground != IntPtr.Zero && !Native.Owns(foreground))
                state.LastForeign = foreground;
            if (stationary && !state.Open && !rail.IsVisible && state.EdgeSince == DateTime.MinValue)
                return;
            var screen = Forms.Screen.FromPoint(new System.Drawing.Point(mouse.X, mouse.Y));
            if (!state.Open && !state.Closing)
                TickRail(screen, mouse);
            else if (state.Open && !state.Pinned && !state.SettingsOpen && !state.PopupOpen && Store.State.AutoHide)
                TickBoards(mouse);
        }
        catch (Exception ex) { Store.Log("Tick: " + ex.Message); }
    }
    // Signaux d'un second processus : retour de la barre Windows, arrêt, bascule. Vrai quand le tour est consommé.
    bool Signals()
    {
        if (Program.TaskbarRestoreSignal.WaitOne(0))
        {
            host.SetTaskbar(false);
            return true;
        }
        if (Program.QuitSignal.WaitOne(0))
        {
            Store.Log("QuitSignal reçu : arrêt demandé par un second processus --quit (voir la ligne 'Args' correspondante)");
            Application.Current.Shutdown();
            return true;
        }
        if (Program.ShowSignal.WaitOne(0))
        {
            if (state.Open)
                host.Collapse();
            else
                host.Open();
            return true;
        }
        return false;
    }
    void TickRail(Forms.Screen screen, Native.Point mouse)
    {
        var work = screen.WorkingArea;
        bool atEdge = TickRules.AtEdge(Store.State.RailSide, mouse.X, mouse.Y, screen.Bounds.Left, screen.Bounds.Right, work.Top,
            work.Bottom);
        if ((atEdge || rail.IsVisible) && Store.State.PauseFullscreen && Native.Fullscreen(screen, host.AllWindows()))
        {
            rail.Hide();
            state.EdgeSince = DateTime.MinValue;
            return;
        }
        if (atEdge)
        {
            state.RailAway = DateTime.MinValue;
            if (state.EdgeSince == DateTime.MinValue)
                state.EdgeSince = DateTime.UtcNow;
            if (TickRules.EdgeDwellReached(state.EdgeSince, DateTime.UtcNow) && (!rail.IsVisible || railScreen != screen.DeviceName))
            {
                double scale = VisualTreeHelper.GetDpi(rail).DpiScaleX;
                var place = TickRules.RailPlacement(Store.State.RailSide, screen.Bounds.Left, screen.Bounds.Right, work.Top,
                    work.Height, scale, Store.State.RailWidth, Store.State.RailHeight);
                rail.MoveInto(place.X, place.Y, Store.State.RailWidth, place.Height, 0, false);
                railScreen = screen.DeviceName;
            }
            return;
        }
        state.EdgeSince = DateTime.MinValue;
        if (Contains(rail, mouse))
        {
            state.RailAway = DateTime.MinValue;
            return;
        }
        if (state.RailAway == DateTime.MinValue)
            state.RailAway = DateTime.UtcNow;
        if (TickRules.RailAwayExceeded(state.RailAway, DateTime.UtcNow))
            rail.Hide();
    }
    void TickBoards(Native.Point mouse)
    {
        if (host.HoverWindows().Any(w => Contains(w, mouse)))
        {
            state.AwaySince = DateTime.MinValue;
            return;
        }
        if (state.AwaySince == DateTime.MinValue)
            state.AwaySince = DateTime.UtcNow;
        if (TickRules.BoardAwayExceeded(state.AwaySince, DateTime.UtcNow))
            host.Collapse();
    }
    static bool Contains(Window window, Native.Point point)
    {
        if (!window.IsVisible)
            return false;
        var dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
        return TickRules.Inside(window.Left, window.Top, window.Width, window.Height, dpi, point.X, point.Y);
    }
}
