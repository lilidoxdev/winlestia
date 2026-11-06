// C#12 / Tiler075 tests / only the dedicated fixture windows can be moved.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using PanneauxGlass;
using Forms = System.Windows.Forms;
namespace Winlestia.Tests;
[Trait("Category", "Desktop")]
public class TilerIntegrationTests
{
    [Fact]
    public void AllChecks() => Sta.Run(AllChecksSta);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R rect);
    [StructLayout(LayoutKind.Sequential)]
    struct R
    {
        public int L, T, Ri, B; public Rectangle Rect => Rectangle.FromLTRB(L, T, Ri, B);
    }
    static void AllChecksSta()
    {
        var asm = typeof(Entry).Assembly;
        var type = asm.GetType("PanneauxGlass.WindowTiler");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var windows = new List<Forms.Form>();
        var original = new Dictionary<IntPtr, Rectangle>();
        var area = Forms.Screen.PrimaryScreen.WorkingArea;
        Forms.Form Add()
        {
            var form = new Forms.Form { Text = "Winlestia dedicated tiling fixture " + windows.Count, StartPosition = Forms.FormStartPosition.Manual, Location = new Point(area.Left + 120 + windows.Count * 30, area.Top + 120), Size = new Size(420, 250), MinimumSize = new Size(160, 100) };
            var requested = form.Bounds;
            windows.Add(form);
            form.Show();
            Forms.Application.DoEvents();
            original[form.Handle] = requested;
            return form;
        }
        Add();
        Add();
        Add();
        Func<IEnumerable<IntPtr>> source = () => windows.Where(w => !w.IsDisposed).Select(w => w.Handle);
        var tiler = Activator.CreateInstance(type, flags, null, new object[] { source }, null);
        void Enable(bool on) => type.GetMethod("SetEnabled", flags).Invoke(tiler, new object[] { on });
        void Tick(bool settle = true)
        {
            type.GetMethod("Tick", flags).Invoke(tiler, null);
            if (settle)
            {
                var until = Stopwatch.StartNew();
                while (until.ElapsedMilliseconds < 450)
                {
                    Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(4);
                }
            }
            Forms.Application.DoEvents();
        }
        Rectangle Visible(Forms.Form form)
        {
            var value = type.GetMethod("Visible", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { form.Handle });
            var t = value.GetType();
            return Rectangle.FromLTRB((int)t.GetField("Left").GetValue(value), (int)t.GetField("Top").GetValue(value), (int)t.GetField("Right").GetValue(value), (int)t.GetField("Bottom").GetValue(value));
        }
        bool Neat()
        {
            var rects = windows.Where(w => !w.IsDisposed && w.WindowState != Forms.FormWindowState.Minimized).Select(Visible).ToArray();
            return rects.All(r => area.Contains(r)) && !rects.SelectMany((a, i) => rects.Skip(i + 1).Select(b => a.IntersectsWith(b))).Any(x => x);
        }
        try
        {
            Enable(true);
            Tick();
            Tick();
            Assert.True(Neat(), "threeWindowsNonOverlappingInsideWorkArea");
            Add();
            Tick();
            Tick();
            Assert.True(Neat(), "newApplicationReflowsFour");
            var before = windows.Skip(1).Select(Visible).ToArray();
            windows[0].WindowState = Forms.FormWindowState.Maximized;
            Forms.Application.DoEvents();
            Tick();
            Assert.True(windows[0].WindowState == Forms.FormWindowState.Maximized && windows.Skip(1).Select(Visible).SequenceEqual(before), "manualMaximizeHeld");
            windows[0].WindowState = Forms.FormWindowState.Normal;
            Forms.Application.DoEvents();
            Tick();
            Assert.True(Neat(), "restoreReturnsToTiles");
            while (windows.Count < 10)
                Add();
            Tick();
            Tick();
            Assert.True(Neat(), "tenRealFixtureWindows");
            windows[1].WindowState = Forms.FormWindowState.Minimized;
            Forms.Application.DoEvents();
            Tick();
            Assert.True(Neat() && windows[1].WindowState == Forms.FormWindowState.Minimized, "minimizeReclaimsSpace");
            windows[1].WindowState = Forms.FormWindowState.Normal;
            Forms.Application.DoEvents();
            Tick();
            Tick();
            Assert.True(Neat(), "unminimizeRejoinsLayout");
            windows.Last().Close();
            Tick();
            Assert.True(Neat(), "closeReflowsSurvivors");
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 30; i++)
                Tick(false);
            Assert.True(watch.Elapsed.TotalMilliseconds / 30 < 25, "stableTickAverageUnder25ms");
            Enable(false);
            Forms.Application.DoEvents();
            System.Threading.Thread.Sleep(100);
            Forms.Application.DoEvents();
            Assert.True(windows.Where(w => !w.IsDisposed).All(w => { GetWindowRect(w.Handle, out var r); return r.Rect == original[w.Handle]; }), "disableRestoresOriginalGeometry");
            // Pure partition properties on a physical monitor with negative coordinates.
            var nativeRect = asm.GetType("PanneauxGlass.Native+Rect");
            var box = Activator.CreateInstance(nativeRect);
            foreach (var pair in new[] { ("Left", -1920), ("Top", -1080), ("Right", 0), ("Bottom", 0) })
                nativeRect.GetField(pair.Item1).SetValue(box, pair.Item2);
            bool layouts = true;
            for (int n = 1; n <= 16; n++)
            {
                var parts = (Array)type.GetMethod("Layout", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { box, (object)n, (object)8 });
                layouts &= parts.Length == n;
                var bounds = new List<Rectangle>();
                foreach (var p in parts)
                {
                    var t = p.GetType();
                    bounds.Add(Rectangle.FromLTRB((int)t.GetField("Left").GetValue(p), (int)t.GetField("Top").GetValue(p), (int)t.GetField("Right").GetValue(p), (int)t.GetField("Bottom").GetValue(p)));
                }
                layouts &= bounds.All(r => r.Width > 0 && r.Height > 0 && new Rectangle(-1920, -1080, 1920, 1080).Contains(r)) && !bounds.SelectMany((a, i) => bounds.Skip(i + 1).Select(b => a.IntersectsWith(b))).Any(x => x);
            }
            Assert.True(layouts, "oneToSixteenNegativeMonitorPartitions");
        }
        finally { ((IDisposable)tiler).Dispose(); foreach (var w in windows) w.Dispose(); }
    }
}
