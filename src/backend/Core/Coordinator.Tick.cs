// language: C# 12, file: Coordinator.Tick.cs, target: Windows 11 / .NET 8 WPF, boucle d'horloge, repli automatique et clics extérieurs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    void Tick()
    {
        try
        {
            if (Program.TaskbarRestoreSignal.WaitOne(0))
            {
                SetTaskbar(false);
                return;
            }
            if (Program.QuitSignal.WaitOne(0))
            {
                Store.Log("QuitSignal reçu : arrêt demandé par un second processus --quit (voir la ligne 'Args' correspondante)");
                System.Windows.Application.Current.Shutdown();
                return;
            }
            if (Program.ShowSignal.WaitOne(0))
            {
                if (open)
                    Collapse();
                else
                    Open();
                return;
            }
            if (QtShell != null && !open)
                return;
            taskbar?.PointerTick();
            if (open && TickRules.AutoHideSuspended(detailed, interacting, Pinned, settingsOpen, PopupOpen, Store.State.AutoHide))
                return;
            if (!Native.GetCursorPos(out var mouse))
                return;
            IntPtr foreground = Native.GetForegroundWindow();
            bool stationary = mouse.X == lastMouse.X && mouse.Y == lastMouse.Y && foreground == lastForeground;
            lastMouse = mouse;
            lastForeground = foreground;
            if (foreground != IntPtr.Zero && !Native.Owns(foreground))
                lastForeign = foreground;
            if (stationary && !open && !rail.IsVisible && edgeSince == DateTime.MinValue)
                return;
            var screen = Forms.Screen.FromPoint(new System.Drawing.Point(mouse.X, mouse.Y));
            var work = screen.WorkingArea;
            if (!open && !closing)
            {
                bool atEdge = TickRules.AtEdge(Store.State.RailSide, mouse.X, mouse.Y, screen.Bounds.Left, screen.Bounds.Right, work.Top,
                    work.Bottom);
                if ((atEdge || rail.IsVisible) && Store.State.PauseFullscreen && Native.Fullscreen(screen, Windows))
                {
                    rail.Hide();
                    edgeSince = DateTime.MinValue;
                    return;
                }
                if (atEdge)
                {
                    railAway = DateTime.MinValue;
                    if (edgeSince == DateTime.MinValue)
                        edgeSince = DateTime.UtcNow;
                    if (TickRules.EdgeDwellReached(edgeSince, DateTime.UtcNow) && (!rail.IsVisible || railScreen != screen.DeviceName))
                    {
                        double scale = VisualTreeHelper.GetDpi(rail).DpiScaleX;
                        var place = TickRules.RailPlacement(Store.State.RailSide, screen.Bounds.Left, screen.Bounds.Right, work.Top,
                            work.Height, scale, Store.State.RailWidth, Store.State.RailHeight);
                        rail.MoveInto(place.X,
                                      place.Y,
                                      Store.State.RailWidth,
                                      place.Height,
                                      0,
                                      false);
                        railScreen = screen.DeviceName;
                    }
                }
                else
                {
                    edgeSince = DateTime.MinValue;
                    if (!Contains(rail, mouse))
                    {
                        if (railAway == DateTime.MinValue)
                            railAway = DateTime.UtcNow;
                        if (TickRules.RailAwayExceeded(railAway, DateTime.UtcNow))
                            rail.Hide();
                    }
                    else
                        railAway = DateTime.MinValue;
                }
            }
            else if (open && !Pinned && !settingsOpen && !PopupOpen && Store.State.AutoHide)
            {
                bool inside = Boards.Any(b => Contains(b, mouse)) || Contains(toolbar, mouse) || websites.Windows.Any(w => Contains(w,
                    mouse));
                if (inside)
                    awaySince = DateTime.MinValue;
                else
                {
                    if (awaySince == DateTime.MinValue)
                        awaySince = DateTime.UtcNow;
                    if (TickRules.BoardAwayExceeded(awaySince, DateTime.UtcNow))
                        Collapse();
                }
            }
        }
        catch (Exception ex) { Store.Log("Tick: " + ex.Message); }
    }
    static bool Contains(Window window, Native.Point point)
    {
        if (!window.IsVisible)
            return false;
        var dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
        return TickRules.Inside(window.Left, window.Top, window.Width, window.Height, dpi, point.X, point.Y);
    }
}
