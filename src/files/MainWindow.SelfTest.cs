// Hidden measurement drivers, only reachable through WINLESTIA_FILES_SELFTEST (never set in normal use).
//   tabs50 : 50 open/close cycles of a tab, 30 s settle, logs handles/threads/private bytes before and after.
//   grid   : switches to the large-icons view, waits for the thumbnails, 5 end-to-end fast scroll passes, logs GDI/handles/threads/private/stall.
//   ops    : see MainWindow.SelfTest.Ops.cs (file operations on a %TEMP% fixture).
//   nav    : WINLESTIA_FILES_NAVPATHS="A;B" navigated alternately 5 times in this one process (warm navigations).
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace WinlestiaFiles;

public sealed partial class MainWindow {
    // WINLESTIA_FILES_TABLOG=<file>: test harness only; rewrites "tabs=N" every 200 ms (the resident has no other observable tab count).
    void StartTabLog() {
        string? file = Environment.GetEnvironmentVariable("WINLESTIA_FILES_TABLOG");
        if (string.IsNullOrEmpty(file)) return;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => { try { System.IO.File.WriteAllText(file, "tabs=" + tabs.Count + " visible=" + IsVisible); } catch { } };
        timer.Start();
    }

    void StartSelfTest() {
        StartTabLog();
        string? mode = Environment.GetEnvironmentVariable("WINLESTIA_FILES_SELFTEST");
        if (string.IsNullOrEmpty(mode)) return;
        ContentRendered += async (_, _) => {
            try {
                if (mode == "tabs50") await Tabs50();
                else if (mode == "nav") await WarmNav();
                else if (mode == "grid") await GridScroll();
                else if (mode == "ops") await OpsTest();
                else if (mode == "dnd") await DndTest();
                else if (mode == "recycle") await RecycleTest();
                else if (mode == "ctxleak") await CtxLeak();
                else if (mode == "ctxthreads") await CtxThreads();
                else if (mode == "renameshot") await RenameShot();
                else if (mode == "refresh") await RefreshTest();
                else if (mode == "states") await StatesShots();
                else if (mode == "palette") await PaletteTest();
                else if (mode == "themeshot") await ThemeShot();
            } catch (Exception ex) { App.CrashLog(ex); }
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flags);

    static void Sample(string label) {
        using var p = Process.GetCurrentProcess();
        Perf.Log($"selftest {label}: handles {p.HandleCount}, threads {p.Threads.Count}, private {p.PrivateMemorySize64 / 1048576.0:F1} MB, gdi {GetGuiResources(p.Handle, 0)}, user {GetGuiResources(p.Handle, 1)}, cpu {p.TotalProcessorTime.TotalMilliseconds:F0} ms");
    }

    static System.Windows.Controls.ScrollViewer? FindScroll(System.Windows.DependencyObject d) {
        if (d is System.Windows.Controls.ScrollViewer sv) return sv;
        for (int i = 0, n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i < n; i++) if (FindScroll(System.Windows.Media.VisualTreeHelper.GetChild(d, i)) is { } found) return found;
        return null;
    }

    async Task GridScroll() {
        await Task.Delay(4000);
        Sample("details settled");
        long t0 = Stopwatch.GetTimestamp();
        SetView(true);
        for (int i = 0; i < 400 && Thumbs.Pending > 0 || i < 4; i++) await Task.Delay(25);
        Perf.Log($"selftest grid visible filled {Perf.Ms(t0):F0} ms after the switch (pending {Thumbs.Pending}, shown {Thumbs.Shown})");
        await Task.Delay(1500);
        Sample("grid before scroll");
        var sv = FindScroll(list); if (sv == null) return;
        for (int pass = 1; pass <= 5; pass++) {
            Perf.Stall?.Reset();
            while (sv.VerticalOffset < sv.ScrollableHeight - 1) { sv.ScrollToVerticalOffset(sv.VerticalOffset + 110); await Task.Delay(8); }
            while (sv.VerticalOffset > 1) { sv.ScrollToVerticalOffset(sv.VerticalOffset - 110); await Task.Delay(8); }
            await Task.Delay(600);
            Perf.Log($"selftest pass {pass} max UI stall {Perf.Stall?.Max ?? 0:F1} ms");
            Sample("grid after pass " + pass);
        }
        await Task.Delay(30000);
        Sample("grid after 30 s");
        await Task.Delay(10000);
        Sample("grid after 40 s");
    }

    async Task Tabs50() {
        await Task.Delay(5000);
        Sample("baseline");
        string where = Active.Location;
        for (int i = 0; i < 50; i++) {
            var view = AddTab(where);
            await Task.Delay(40);
            CloseTab(view);
            await Task.Delay(10);
        }
        Perf.Log("selftest tabs50 cycles done");
        await Task.Delay(30000);
        Sample("after 50 cycles + 30 s");
    }

    async Task WarmNav() {
        var paths = (Environment.GetEnvironmentVariable("WINLESTIA_FILES_NAVPATHS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (paths.Length == 0) return;
        await Task.Delay(2500);
        for (int round = 0; round < 5; round++)
            foreach (var path in paths) {
                Perf.Log($"selftest warm round {round + 1}");
                var done = new TaskCompletionSource();
                void Watch(FolderTab t) { if (t.State != LoadState.Loading) done.TrySetResult(); }
                Active.Changed += Watch;
                Active.Go(path);
                await Task.WhenAny(done.Task, Task.Delay(15000));
                Active.Changed -= Watch;
                await Task.Delay(700);
            }
        Perf.Log("selftest nav done");
    }
}
