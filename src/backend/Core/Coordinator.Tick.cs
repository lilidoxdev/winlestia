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
            if (open && (!detailed || interacting || Pinned || settingsOpen || PopupOpen || !Store.State.AutoHide))
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
                bool atEdge =
                    (Store.State.RailSide == "right"
                        ? mouse.X >= screen.Bounds.Right - 4 && mouse.X <= screen.Bounds.Right
                        : mouse.X <= screen.Bounds.Left + 4 && mouse.X >= screen.Bounds.Left)
                        && mouse.Y > work.Top + 24
                        && mouse.Y < work.Bottom - 24;
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
                    if ((DateTime.UtcNow - edgeSince).TotalMilliseconds >= 150 && (!rail.IsVisible || railScreen != screen.DeviceName))
                    {
                        double scale = VisualTreeHelper.GetDpi(rail).DpiScaleX;
                        double height = work.Height / scale * Store.State.RailHeight;
                        rail.MoveInto((Store.State.RailSide == "right"
                            ? screen.Bounds.Right / scale - Store.State.RailWidth - 12
                            : screen.Bounds.Left / scale + 12),
                                      work.Top / scale + (work.Height / scale - height) / 2,
                                      Store.State.RailWidth,
                                      height,
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
                        if ((DateTime.UtcNow - railAway).TotalMilliseconds > 650)
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
                    if ((DateTime.UtcNow - awaySince).TotalMilliseconds > 1800)
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
        return point.X >= window.Left * dpi
            && point.Y >= window.Top * dpi
            && point.X < (window.Left + window.Width) * dpi
            && point.Y < (window.Top + window.Height) * dpi;
    }
}
