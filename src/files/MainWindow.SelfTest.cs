#if WINLESTIA_SELFTEST
// Pilotes de mesure cachés, accessibles uniquement via WINLESTIA_FILES_SELFTEST (jamais défini en usage normal).
//   tabs50 : 50 cycles d'ouverture/fermeture d'un onglet, 30 s de stabilisation, journalise handles/threads/octets privés avant et après.
// grid   : passe en vue grandes icônes, attend les miniatures, 5 passes de défilement rapide de bout en bout, journalise
// GDI/handles/threads/privé/blocage.
//   ops    : voir MainWindow.SelfTest.Ops.cs (opérations de fichiers sur un dossier de fixtures dans %TEMP%).
//   nav    : WINLESTIA_FILES_NAVPATHS="A;B" parcourus alternativement 5 fois dans ce seul processus (navigations à chaud).
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    // WINLESTIA_FILES_TABLOG=<fichier> : harnais de test uniquement ; réécrit "tabs=N" toutes les 200 ms (le résident n'a aucun autre
    // compteur d'onglets observable).
    void StartTabLog()
    {
        string? file = Environment.GetEnvironmentVariable("WINLESTIA_FILES_TABLOG");
        if (string.IsNullOrEmpty(file))
            return;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PauseBreveMs) };
        timer.Tick += (_, _) => { try { System.IO.File.WriteAllText(file, "tabs=" + tabs.Count + " visible=" + IsVisible); }
            catch (Exception) { /* Auto-test : l'échec de cette étape ne doit pas masquer le verdict du test. */ } };
        timer.Start();
    }

    void StartSelfTest()
    {
        StartTabLog();
        string? mode = Environment.GetEnvironmentVariable("WINLESTIA_FILES_SELFTEST");
        if (string.IsNullOrEmpty(mode))
            return;
        ContentRendered += async (_, _) =>
        {
            try
            {
                if (mode == "tabs50")
                    await Tabs50();
                else if (mode == "nav")
                    await WarmNav();
                else if (mode == "grid")
                    await GridScroll();
                else if (mode == "ops")
                    await OpsTest();
                else if (mode == "dnd")
                    await DndTest();
                else if (mode == "recycle")
                    await RecycleTest();
                else if (mode == "ctxleak")
                    await CtxLeak();
                else if (mode == "ctxthreads")
                    await CtxThreads();
                else if (mode == "renameshot")
                    await RenameShot();
                else if (mode == "refresh")
                    await RefreshTest();
                else if (mode == "states")
                    await StatesShots();
                else if (mode == "palette")
                    await PaletteTest();
                else if (mode == "themeshot")
                    await ThemeShot();
                else if (mode == "lot1")
                    await Lot1Test();
                else if (mode == "lot2")
                    await Lot2Test();
                else if (mode == "themes")
                    await ThemesTest();
                else if (mode == "conflict")
                    await ConflictTest();
                else if (mode == "xfer")
                    await XferTest();
            }
            catch (Exception ex) { App.CrashLog(ex); }
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flags);

    static void Sample(string label)
    {
        using var p = Process.GetCurrentProcess();
        Perf.Log($"selftest {label}: handles {p.HandleCount}, threads {p.Threads.Count}, private {p.PrivateMemorySize64 / 1048576.0:F1} " +
            $"MB, gdi {GetGuiResources(p.Handle, 0)}, user {GetGuiResources(p.Handle, 1)}, cpu " +
            $"{p.TotalProcessorTime.TotalMilliseconds:F0} ms");
    }

    static System.Windows.Controls.ScrollViewer? FindScroll(System.Windows.DependencyObject d)
    {
        if (d is System.Windows.Controls.ScrollViewer sv)
            return sv;
        for (int i = 0, n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i < n; i++)
            if (FindScroll(System.Windows.Media.VisualTreeHelper.GetChild(d, i)) is { } found)
                return found;
        return null;
    }

    async Task GridScroll()
    {
        await Task.Delay(PauseChargementMs);
        Sample("details settled");
        long t0 = Stopwatch.GetTimestamp();
        SetView(true);
        for (int i = 0; i < 400 && Thumbs.Pending > 0 || i < 4; i++)
            await Task.Delay(25);
        Perf.Log($"selftest grid visible filled {Perf.Ms(t0):F0} ms after the switch (pending {Thumbs.Pending}, shown {Thumbs.Shown})");
        await Task.Delay(PauseOperationMs);
        Sample("grid before scroll");
        var sv = FindScroll(list);
        if (sv == null)
            return;
        for (int pass = 1; pass <= 5; pass++)
        {
            Perf.Stall?.Reset();
            while (sv.VerticalOffset < sv.ScrollableHeight - 1)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset + 110);
                await Task.Delay(8);
            }
            while (sv.VerticalOffset > 1)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - 110);
                await Task.Delay(8);
            }
            await Task.Delay(PauseDebounceMs);
            Perf.Log($"selftest pass {pass} max UI stall {Perf.Stall?.Max ?? 0:F1} ms");
            Sample("grid after pass " + pass);
        }
        await Task.Delay(PauseTransfertMs);
        Sample("grid after 30 s");
        await Task.Delay(PauseTresLongueMs);
        Sample("grid after 40 s");
    }

    async Task Tabs50()
    {
        await Task.Delay(PauseLongueMs);
        Sample("baseline");
        string where = Active.Location;
        for (int i = 0; i < 50; i++)
        {
            var view = AddTab(where);
            await Task.Delay(40);
            CloseTab(view);
            await Task.Delay(10);
        }
        Perf.Log("selftest tabs50 cycles done");
        await Task.Delay(PauseTransfertMs);
        Sample("after 50 cycles + 30 s");
    }

    async Task WarmNav()
    {
        var paths = (Environment.GetEnvironmentVariable("WINLESTIA_FILES_NAVPATHS") ?? "").Split(';',
            StringSplitOptions.RemoveEmptyEntries);
        if (paths.Length == 0)
            return;
        await Task.Delay(PauseSondeMs);
        for (int round = 0; round < 5; round++)
            foreach (var path in paths)
            {
                Perf.Log($"selftest warm round {round + 1}");
                var done = new TaskCompletionSource();
                void Watch(FolderTab t)
                {
                    if (t.State != LoadState.Loading)
                        done.TrySetResult();
                }
                Active.Changed += Watch;
                Active.Go(path);
                await Task.WhenAny(done.Task, Task.Delay(TimeoutMoyenMs));
                Active.Changed -= Watch;
                await Task.Delay(PauseDessinMs);
            }
        Perf.Log("selftest nav done");
    }
}
#endif
