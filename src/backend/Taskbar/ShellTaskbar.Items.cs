// language: C# 12, file: ShellTaskbar.Items.cs, target: Windows 11 / .NET 8 WPF, native bottom taskbar
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;



internal sealed partial class ShellDockWindow : Window
{
    static Button Button(string label, Action action)
    {
        var button = UI.Button("", action);
        button.Width = 40;
        button.Height = 42;
        button.Padding = new Thickness(7);
        button.Margin = new Thickness(2, 0, 2, 0);
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        return button;
    }
    static Button Icon(string name, string label, Action action)
    {
        var button = Button(label, action);
        button.Content = ShellIcons.View(name, 19);
        return button;
    }
    internal bool IsOn(Forms.Screen value) => value.DeviceName == screen.DeviceName;
    void Clock()
    {
        time.Text = DateTime.Now.ToString("HH:mm");
        date.Text = DateTime.Now.ToString("dd MMM");
    }
    internal void SetApps(IReadOnlyList<TaskbarNative.AppWindow> windows)
    {
        if (themeVersion != Theme.Version)
            Build();
        apps.Children.Clear();
        var represented = new HashSet<IntPtr>();
        foreach (var entry in manager.Pins().Take(24))
        {
            string target = TaskbarNative.LaunchPath(entry.Target);
            var matching = windows.FirstOrDefault(w => !represented.Contains(w.Handle) && !string.IsNullOrEmpty(w.Path)
                && string.Equals(w.Path, target, StringComparison.OrdinalIgnoreCase));
            if (matching != null)
                represented.Add(matching.Handle);
            AddApp(entry, matching, true);
        }
        if (apps.Children.Count > 0 && windows.Any(w => !represented.Contains(w.Handle)))
            apps.Children.Add(new Border
            {
                Width = 1,
                Height = 23,
                Background = UI.Alpha(Theme.Current.Dim, 50),
                Margin = new Thickness(9,
                0, 9, 0)
            });
        foreach (var window in windows.Where(w => !represented.Contains(w.Handle)))
            AddApp(new Entry
            {
                Id = string.IsNullOrWhiteSpace(window.RealPath) ? "window:" + window.Handle.ToInt64() : "running:"
                + window.RealPath.ToLowerInvariant(),
                Name = window.Title,
                Target = window.RealPath,
                IconPath = window.RealPath
            }, window,
                false);
        SizePill();
    }
    void SizePill()
    {
        double width = screen.Bounds.Width / TaskbarNative.Scale(screen);
        pill.Width = Math.Min(Math.Min(1440, width - 24), Math.Max(740, 420 + apps.Children.Count * 44));
    }
    void AddApp(Entry entry, TaskbarNative.AppWindow? window, bool pinned)
    {
        var button = Button(entry.Name, () =>
        {
            owner.CollapseImmediately();
            if (window != null)
                TaskbarNative.Activate(window);
            else
                owner.Launch(entry);
        });
        var stack = new Grid();
        var image = new Image { Width = 28, Height = 28, Stretch = Stretch.Uniform };
        stack.Children.Add(image);
        owner.Icons.Load(entry, source => UI.SetIcon(image, source), "taskbar");
        if (window != null)
        {
            bool active = Native.GetForegroundWindow() == window.Handle;
            stack.Children.Add(new Border
            {
                Height = 3,
                Width = active ? 16 : 4,
                CornerRadius = new CornerRadius(2),
                Background = UI.Accent,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -3)
            });
            button.Background = active ? UI.Alpha(Theme.Current.Accent, 24) : Brushes.Transparent;
        }
        button.Content = stack;
        button.MouseRightButtonUp += (_, e) => { owner.TaskbarAppMenu(this, button, entry, pinned); e.Handled = true; };
        apps.Children.Add(button);
    }
    void PlacePhysical()
    {
        double scale = TaskbarNative.Scale(screen);
        var b = screen.Bounds;
        Left = b.Left / scale;
        Top = b.Bottom / scale - HostHeight;
        Width = b.Width / scale;
        Height = HostHeight;
        if (handle != IntPtr.Zero)
            TaskbarNative.SetWindowPos(handle, new IntPtr(-1), b.Left, b.Bottom - (int)Math.Round(HostHeight * scale), b.Width,
                (int)Math.Round(HostHeight * scale), 0x0010);
    }
    internal void Register()
    {
        if (handle == IntPtr.Zero || registered)
            return;
        message = TaskbarNative.RegisterWindowMessage("PanneauxAppbar-" + handle);
        var data = TaskbarNative.Data(handle);
        data.Callback = message;
        if (TaskbarNative.SHAppBarMessage(0, ref data) == UIntPtr.Zero)
            throw new InvalidOperationException("Windows a refusé la réservation du bord.");
        registered = true;
        Position();
    }
    void Position()
    {
        if (!registered || placing || closed)
            return;
        placing = true;
        try
        {
            var b = screen.Bounds;
            var data = TaskbarNative.Data(handle);
            data.Edge = 3;
            data.Rect = new Native.Rect { Left = b.Left, Top = b.Bottom - 2, Right = b.Right, Bottom = b.Bottom };
            TaskbarNative.SHAppBarMessage(2, ref data);
            data.Rect.Top = data.Rect.Bottom - 2;
            if (data.Rect.Left != last.Left || data.Rect.Top != last.Top || data.Rect.Bottom != last.Bottom
                || data.Rect.Right != last.Right)
            {
                TaskbarNative.SHAppBarMessage(3, ref data);
                last = data.Rect;
            }
            PlacePhysical();
        }
        finally { placing = false; }
    }
    internal void Fullscreen()
    {
        var foreground = Native.GetForegroundWindow();
        covered = false;
        if (!Native.Owns(foreground) && TaskbarNative.ClassName(foreground) is not ("Progman" or "WorkerW" or "Shell_TrayWnd")
            && TaskbarNative.GetWindowRect(foreground, out var rect))
        {
            var b = screen.Bounds;
            covered = rect.Left <= b.Left && rect.Top <= b.Top && rect.Right >= b.Right && rect.Bottom >= b.Bottom;
        }
        if (covered && DateTime.UtcNow > forcedUntil)
            Conceal(true);
    }
    internal void PointerTick(Native.Point mouse)
    {
        if (closed)
            return;
        Fullscreen();
        var b = screen.Bounds;
        bool edge = mouse.X >= b.Left && mouse.X < b.Right && mouse.Y >= b.Bottom - 3 && mouse.Y < b.Bottom;
        if (covered && DateTime.UtcNow > forcedUntil)
            return;
        if (edge)
        {
            away = DateTime.MinValue;
            Reveal(false);
            return;
        }
        bool inside = false;
        if (IsVisible && pill.IsVisible)
        {
            var point = pill.PointToScreen(new Point(0, 0));
            double scale = TaskbarNative.Scale(screen);
            inside = mouse.X >= point.X - 8 && mouse.X <= point.X + pill.ActualWidth * scale + 8 && mouse.Y >= point.Y - 10
                && mouse.Y < b.Bottom;
        }
        if (inside)
        {
            away = DateTime.MinValue;
            return;
        }
        if (!IsVisible || DateTime.UtcNow < forcedUntil)
            return;
        if (away == DateTime.MinValue)
            away = DateTime.UtcNow;
        if ((DateTime.UtcNow - away).TotalMilliseconds > 450)
            Conceal(false);
    }
    internal void Reveal(bool keyboard)
    {
        if (closed)
            return;
        hiding = false;
        if (keyboard)
            forcedUntil = DateTime.UtcNow.AddSeconds(3);
        if (IsVisible && slide.Y == 0 && pill.Opacity >= .99)
            return;
        int version = ++motionVersion;
        double start = IsVisible ? slide.Y : HostHeight;
        slide.BeginAnimation(TranslateTransform.YProperty, null);
        pill.BeginAnimation(UIElement.OpacityProperty, null);
        slide.Y = 0;
        pill.Opacity = 1;
        if (!IsVisible)
            Show();
        Topmost = true;
        PlacePhysical();
        if (Motion.On)
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation(start, 0, Motion.Duration(MotionKind.FastSpatial))
            {
                EasingFunction = Motion.Ease,
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            };
            animation.Completed += (_, _) => { if (version == motionVersion) slide.BeginAnimation(TranslateTransform.YProperty, null); };
            slide.BeginAnimation(TranslateTransform.YProperty, animation);
            pill.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(.3, 1,
                Motion.Duration(MotionKind.FastEffects))
            {
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            });
        }
    }
    internal void Conceal(bool immediate = false)
    {
        if (!IsVisible || closed || hiding && !immediate)
            return;
        hiding = true;
        int version = ++motionVersion;
        double from = slide.Y;
        slide.BeginAnimation(TranslateTransform.YProperty, null);
        pill.BeginAnimation(UIElement.OpacityProperty, null);
        if (immediate || !Motion.On)
        {
            Hide();
            slide.Y = 0;
            return;
        }
        var animation = new System.Windows.Media.Animation.DoubleAnimation(from, HostHeight, Motion.Duration(MotionKind.FastEffects))
        {
            EasingFunction = Motion.Curve(MotionKind.FastEffects),
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (version == motionVersion)
            {
                Hide();
                slide.BeginAnimation(TranslateTransform.YProperty,
            null);
                slide.Y = 0;
            }
        };
        slide.BeginAnimation(TranslateTransform.YProperty, animation);
    }
    IntPtr Hook(IntPtr w, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if ((uint)msg == message && message != 0)
        {
            if (wp.ToInt32() == 1)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)Position);
            else if (wp.ToInt32() == 2)
                Fullscreen();
        }
        if ((uint)msg == explorerRestart)
        {
            registered = false;
            last = default;
            Dispatcher.BeginInvoke((Action)(() =>
            {
                TaskbarNative.HideExplorer();
                if (owner.QtShell == null)
                    Register();
                manager.Refresh();
            }));
        }
        return IntPtr.Zero;
    }
}
