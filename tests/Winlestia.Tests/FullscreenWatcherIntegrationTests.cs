// C#12 / FullscreenWatcher / DESKTOP: opens real top-level windows (normal, captionless maximised, fullscreen) on the primary monitor and
// steals focus. Never run in CI.
using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using PanneauxGlass;
namespace Winlestia.Tests;
[Trait("Category", "Desktop")]
public class FullscreenWatcherIntegrationTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte vk, byte sc, uint f, UIntPtr e);
    class Popup : Form
    {
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.Style = unchecked((int)0x80000000) | 0x00040000 | 0x10000000;
                return p;
            }
        }
    }   // WS_POPUP|WS_THICKFRAME|WS_VISIBLE, no caption
    static void Focus(Form f)
    {
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(f.Handle);
        f.Activate();
    }
    static bool Wait(Func<bool> c, int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            Application.DoEvents();
            if (c())
                return true;
            Thread.Sleep(10);
        }
        return c();
    }
    static string Current() => string.Join(",", FullscreenWatcher.Current);
    [Fact]
    public void DetectsOnlyTrueFullscreenWindows() => Sta.Run(() =>
    {
        FullscreenWatcher.IncludeOwnProcessForTest = true;
        // the running shell frontend (production ignores it too)
        FullscreenWatcher.IgnoredPids = () => System.Diagnostics.Process.GetProcessesByName("pythonw").Select(p => (uint)p.Id).ToArray();
        var screen = Screen.PrimaryScreen!;
        int fired = 0;
        Action onChanged = () => fired++;
        FullscreenWatcher.Changed += onChanged;
        FullscreenWatcher.Start();
        try
        {
            Assert.True(Wait(() => FullscreenWatcher.Current.Length == 0, 500), "initial state is empty, got [" + Current() + "]");
            var normal = new Form { StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(screen.Bounds.X + 100, screen.Bounds.Y + 100, 600, 400) };
            normal.Show();
            Focus(normal);
            Assert.False(Wait(() => FullscreenWatcher.Current.Length > 0, 500),
                "a normal window must not be counted as fullscreen, got [" + Current() + "]");
            // Electron-like maximized frameless window: work area plus ~8 px resize-border overhang, never the whole monitor
            var wa = screen.WorkingArea;
            var maxed = new Popup { StartPosition = FormStartPosition.Manual, Bounds = Rectangle.Inflate(wa, 8, 8) };
            maxed.Show();
            Focus(maxed);
            Assert.False(Wait(() => FullscreenWatcher.Current.Length > 0, 600),
                "a maximized captionless window must not be counted, got [" + Current() + "]");
            maxed.Close();
            var full = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = screen.Bounds,
                TopMost = true };
            full.Show();
            Focus(full);
            bool on = Wait(() => FullscreenWatcher.Current.Length == 1, 500);
            Assert.True(on, "fullscreen window is detected, current=[" + Current() + "]");
            Assert.True(FullscreenWatcher.Current[0] == screen.DeviceName,
                "detected device is " + screen.DeviceName + ", got " + FullscreenWatcher.Current[0]);
            full.Bounds = new Rectangle(screen.Bounds.X + 100, screen.Bounds.Y + 100, 600, 400);
            Focus(full);
            Assert.True(Wait(() => FullscreenWatcher.Current.Length == 0, 800), "cleared after restore, got [" + Current() + "]");
            full.Bounds = screen.Bounds;
            Focus(full);
            Assert.True(Wait(() => FullscreenWatcher.Current.Length == 1, 500),
                "second fullscreen entry is detected, got [" + Current() + "]");
            full.Close();
            normal.Close();
            Assert.True(Wait(() => FullscreenWatcher.Current.Length == 0, 800), "cleared after close, got [" + Current() + "]");
        }
        finally
        {
            FullscreenWatcher.Stop();
            FullscreenWatcher.Changed -= onChanged;
        }
    });
}
