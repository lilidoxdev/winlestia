// C# 12 / TilingLatencyVerification.cs / Windows .NET 8. Latence affichage-placement de WindowTiler sur des fenêtres de test privées
// uniquement.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Forms = System.Windows.Forms;
namespace PanneauxGlass;

internal static class TilingLatencyVerification
{
    // Cède le processeur entre deux pompages de messages sans fausser la latence mesurée.
    const int PumpPauseMs = 1;

    sealed class ToolForm : Forms.Form
    {
        protected override Forms.CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= 0x80;
                return p;
            }
        }
    }   // WS_EX_TOOLWINDOW : invisible pour le tiler de l'application en cours
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr Style(IntPtr h, int index);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int Bounds(IntPtr h, int attribute,
        out Native.Rect value,
        int size);
    static void Pump(int ms)
    {
        var end = Stopwatch.GetTimestamp() + Stopwatch.Frequency * ms / 1000;
        while (Stopwatch.GetTimestamp() < end)
        {
            Forms.Application.DoEvents();
            Thread.Sleep(PumpPauseMs);
        }
    }
    static Native.Rect Visible(IntPtr h)
    {
        TaskbarNative.GetWindowRect(h, out var r);
        return Bounds(h, 9, out var f, Marshal.SizeOf<Native.Rect>()) == 0 ? f : r;
    }
    static bool Near(Native.Rect a, Native.Rect b) => Math.Abs(a.Left - b.Left) <= 2 && Math.Abs(a.Top - b.Top) <= 2
        && Math.Abs(a.Right - b.Right) <= 2 && Math.Abs(a.Bottom - b.Bottom) <= 2;
    static Dictionary<long, Native.Rect> Snapshot(HashSet<IntPtr> own)
    {
        var map = new Dictionary<long, Native.Rect>();
        TaskbarNative.EnumWindows((h, _) => { if (!own.Contains(h) && TaskbarNative.IsWindowVisible(h) && (Style(h,
            -16).ToInt64() & 0x40000) != 0 && (Style(h, -20).ToInt64() & 0x80) == 0 && Native.Title(h).Length > 0
            && TaskbarNative.GetWindowRect(h, out var r)) map[h.ToInt64()] = r; return true; }, IntPtr.Zero);
        return map;
    }
    internal static int Run()
    {
        var forms = new List<Forms.Form>();
        var handles = new List<IntPtr>();
        var own = new HashSet<IntPtr>();
        var screen = Forms.Screen.PrimaryScreen!;
        var work = screen.WorkingArea;
        int gap = Math.Max(4, (int)Math.Round(8 * TaskbarNative.Scale(screen)));
        var area = new Native.Rect { Left = work.Left + gap, Top = work.Top + gap, Right = work.Right - gap, Bottom = work.Bottom - gap };
        var samples = new Dictionary<int, List<double>> { [1] = new(), [2] = new(), [4] = new() };
        bool ok = true;
        var before = Snapshot(own);
        WindowTiler? tiler = null;
        try
        {
            tiler = new WindowTiler(() => handles.ToArray());
            tiler.SetEnabled(true);
            for (int run = 0; run < 4; run++)
            {
                for (int n = 1; n <= 4; n++)
                {
                    var form = new ToolForm { Text = "Tiling latency test " + run + "-" + n,
                        StartPosition = Forms.FormStartPosition.Manual,
                        Bounds = new(work.Left + 60 + n * 20, work.Top + 60 + n * 20, 420, 300) };
                    var slot = WindowTiler.Layout(area, n, gap)[n - 1];
                    forms.Add(form);
                    long start = Stopwatch.GetTimestamp();
                    form.Show();
                    var h = form.Handle;
                    handles.Add(h);
                    own.Add(h);
                    double ms = 3000;
                    while ((ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds) < 3000)
                    {
                        Forms.Application.DoEvents();
                        if (Near(Visible(h), slot))
                            break;
                        Thread.Sleep(PumpPauseMs);
                    }
                    if (ms >= 3000)
                    {
                        var v = Visible(h);
                        Console.WriteLine(
                            $"  timeout run {run} windows {n} at {v.Left},{v.Top},{v.Right},{v.Bottom} want " +
                            $"{slot.Left},{slot.Top},{slot.Right},{slot.Bottom}");
                    }
                    if (samples.TryGetValue(n, out var list))
                        list.Add(ms);
                    Pump(n == 4 ? 600 : 400);
                }
                tiler.SetEnabled(false);
                foreach (var f in forms)
                    f.Close();
                forms.Clear();
                handles.Clear();
                own.Clear();
                Pump(100);
                tiler.SetEnabled(true);
            }
            // fenêtre affichée sans titre, titrée 100 ms plus tard : doit être placée dans les 150 ms suivant le changement de titre
            {
                var form = new ToolForm { Text = "", StartPosition = Forms.FormStartPosition.Manual, Bounds = new(work.Left + 80, work.Top
                    + 80, 420, 300) };
                forms.Add(form);
                form.Show();
                var h = form.Handle;
                handles.Add(h);
                own.Add(h);
                Pump(100);
                var slot = WindowTiler.Layout(area, 1, gap)[0];
                long start = Stopwatch.GetTimestamp();
                form.Text = "Tiling latency titled";
                double ms = 3000;
                while ((ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds) < 3000)
                {
                    Forms.Application.DoEvents();
                    if (Near(Visible(h), slot))
                        break;
                    Thread.Sleep(PumpPauseMs);
                }
                bool pass = ms < 150;
                ok &= pass;
                Console.WriteLine($"late title: placed {ms:F0} ms after title change {(pass ? "PASS" : "FAIL")}");
            }
            foreach (var pair in samples.OrderBy(p => p.Key))
            {
                var s = pair.Value.OrderBy(x => x).ToArray();
                double med = s[s.Length / 2], p95 = s[Math.Min(s.Length - 1, (int)Math.Ceiling(s.Length * 0.95) - 1)];
                bool pass = p95 < 150;
                ok &= pass;
                Console.WriteLine($"windows={pair.Key} n={s.Length} median={med:F0} ms p95={p95:F0} ms {(pass ? "PASS" : "FAIL")}");
            }
            tiler.SetEnabled(false);
        }
        finally { foreach (var f in forms) f.Close(); tiler?.Dispose(); }
        var after = Snapshot(own);
        var moved = before.Where(p => !after.TryGetValue(p.Key, out var r) || r.Left != p.Value.Left || r.Top != p.Value.Top
            || r.Right != p.Value.Right || r.Bottom != p.Value.Bottom).ToArray();
        bool untouched = moved.Length == 0;
        foreach (var m in moved)
            Console.WriteLine(
                $"  changed hwnd {m.Key:X} class={TaskbarNative.ClassName(new IntPtr(m.Key))} title={Native.Title(new IntPtr(m.Key))}");
        Console.WriteLine("user windows untouched: " + (untouched ? "PASS" : "FAIL"));
        ok &= untouched;
        Console.WriteLine(ok ? "ALL PASS" : "FAIL");
        return ok ? 0 : 1;
    }
}
