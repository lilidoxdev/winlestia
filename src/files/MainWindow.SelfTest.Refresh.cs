// WINLESTIA_FILES_SELFTEST=refresh : auto refresh + filter + selection/scroll on a fixture folder (first command-line argument, must live under %TEMP%\WinlestiaFilesOps).
// WINLESTIA_FILES_SELFTEST=states  : empty / denied / filter screenshots (WINLESTIA_FILES_SHOTDIR). Results: PASS/FAIL lines in selftest.log. Fixture: any folder with >= 12 files.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PanneauxGlass;

namespace WinlestiaFiles;

public sealed partial class MainWindow {
    string Fixture() {
        string f = Path.GetFullPath(Active.Location);
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WinlestiaFilesOps"));
        if (!f.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("fixture must live under " + root);
        return f;
    }

    // Polls until the shown list equals the disk (and extra holds). Returns the elapsed ms, -1 on timeout.
    async Task<double> WaitMatch(Func<bool>? extra = null, int limit = 3000) {
        long t = Stopwatch.GetTimestamp();
        while (Perf.Ms(t) < limit) {
            if (DiskMatches() && OrderOk() && (extra?.Invoke() ?? true)) return Perf.Ms(t);
            await Task.Delay(10);
        }
        return -1;
    }

    string TopPathNow() {
        var sv = ListScroller(); if (sv == null) return "";
        int i = (int)sv.VerticalOffset;
        return i >= 0 && i < Active.Items.Count ? Active.Items[i].Name : "";
    }

    async Task RefreshTest() {
        await WaitLoaded();
        string F = Fixture(); T("refresh fixture " + F + " (" + Active.Items.Count + " items)");
        Activate(); list.Focus();
        Perf.Stall?.Reset();
        int resets0 = RangeObservableCollection<FileItem>.Resets;
        Check("initial load matches disk", DiskMatches() && OrderOk());
        var sel = Active.Items.Where(i => !i.IsDir).Skip(2).Take(3).Select(i => i.Name).ToList();
        SelectNames(sel);
        bool SelKept() => list.SelectedItems.Count == sel.Count && list.SelectedItems.Cast<FileItem>().All(i => sel.Contains(i.Name));
        Check("selection set", SelKept());

        // 1 file, then 20 files, from a background thread
        var t1 = await Task.Run(() => { File.WriteAllText(Path.Combine(F, "new-001.txt"), "x"); return 0; });
        double ms = await WaitMatch(() => Active.Find(Path.Combine(F, "new-001.txt")) != null);
        Check("1 created file appears", ms >= 0, $"{ms:F0} ms");
        await Task.Run(() => { for (int i = 0; i < 20; i++) File.WriteAllText(Path.Combine(F, $"batch20-{i:00}.txt"), "x"); });
        ms = await WaitMatch(() => Active.Items.Count(i => i.Name.StartsWith("batch20-")) == 20);
        Check("20 created files appear", ms >= 0, $"{ms:F0} ms");
        Check("no Reset for <= 20 changes", RangeObservableCollection<FileItem>.Resets == resets0, $"resets {RangeObservableCollection<FileItem>.Resets - resets0}");
        Check("selection preserved (1 + 20)", SelKept());

        // scroll anchor kept across a bulk change
        var sv = ListScroller();
        if (sv != null && sv.ScrollableHeight > 12) {
            sv.ScrollToVerticalOffset(10); await Task.Delay(100);
            string top = TopPathNow();
            await Task.Run(() => { for (int i = 0; i < 300; i++) File.WriteAllText(Path.Combine(F, $"bulk300-{i:000}.txt"), "x"); });
            ms = await WaitMatch(() => Active.Items.Count(i => i.Name.StartsWith("bulk300-")) == 300);
            Check("300 created files appear", ms >= 0, $"{ms:F0} ms");
            await Task.Delay(150);
            Check("scroll anchor preserved after a bulk change", TopPathNow() == top, $"top before '{top}' now '{TopPathNow()}'");
        } else {
            await Task.Run(() => { for (int i = 0; i < 300; i++) File.WriteAllText(Path.Combine(F, $"bulk300-{i:000}.txt"), "x"); });
            ms = await WaitMatch(() => Active.Items.Count(i => i.Name.StartsWith("bulk300-")) == 300);
            Check("300 created files appear", ms >= 0, $"{ms:F0} ms");
        }
        Check("selection preserved (300)", SelKept());

        // rename, delete, subfolder, size change
        string a = Path.Combine(F, "new-001.txt"), b = Path.Combine(F, "renamed-001.txt");
        await Task.Run(() => File.Move(a, b));
        ms = await WaitMatch(() => Active.Find(b) != null && Active.Find(a) == null);
        Check("rename", ms >= 0, $"{ms:F0} ms");
        await Task.Run(() => File.Delete(b));
        ms = await WaitMatch(() => Active.Find(b) == null);
        Check("delete", ms >= 0, $"{ms:F0} ms");
        string sub = Path.Combine(F, "subfolder-x");
        await Task.Run(() => Directory.CreateDirectory(sub));
        ms = await WaitMatch(() => Active.Find(sub) != null);
        Check("subfolder created (folders first)", ms >= 0 && Active.Items[0].IsDir, $"{ms:F0} ms");
        string grow = Path.Combine(F, "batch20-00.txt");
        await Task.Run(() => File.AppendAllText(grow, new string('y', 5000)));
        ms = await WaitMatch(() => Active.Find(grow)?.Size > 4000);
        Check("size change", ms >= 0, $"{ms:F0} ms");
        Check("selection preserved (end)", SelKept());
        Check("status bar consistent", StatusOk(), status.Text);
        T($"refresh: events {WatchEvents}, batches {WatchBatches}, resets {RangeObservableCollection<FileItem>.Resets - resets0}, max UI stall {Perf.Stall?.Max ?? -1:F1} ms");

        // sort change keeps the selection
        Active.SetSort(SortColumn.Size); await Task.Delay(400);
        Check("selection kept on sort change", SelKept());
        Active.SetSort(SortColumn.Name); await Task.Delay(300);

        // tab switch keeps selection; inactive tab is re-synced when shown
        var first = activeView;
        AddTab(sub); await WaitLoaded();
        await Task.Run(() => File.WriteAllText(Path.Combine(F, "while-away.txt"), "x"));
        Activate(first);
        ms = await WaitMatch(() => Active.Find(Path.Combine(F, "while-away.txt")) != null);
        Check("stale tab re-synced on activation", ms >= 0, $"{ms:F0} ms");
        Check("selection kept on tab switch", SelKept());
        CloseTab(tabs.Last());

        // external delete of the current folder -> nearest parent + toast
        string gone = Path.Combine(F, "gone-dir");
        Directory.CreateDirectory(gone); File.WriteAllText(Path.Combine(gone, "a.txt"), "x");
        Active.Go(gone); await WaitLoaded();
        await Task.Run(() => Directory.Delete(gone, true));
        long t = Stopwatch.GetTimestamp();
        while (Perf.Ms(t) < 3000 && !string.Equals(Active.Location.TrimEnd('\\'), F.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) await Task.Delay(20);
        Check("deleted current folder -> navigates to parent", string.Equals(Active.Location.TrimEnd('\\'), F.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase), $"{Perf.Ms(t):F0} ms, at {Active.Location}");
        Check("toast shown", toast.Visibility == Visibility.Visible, toastText.Text);
        await WaitLoaded();

        // filter on a big folder (fixture creates 2000 files under F\many)
        string many = Path.Combine(F, "many");
        await Task.Run(() => { Directory.CreateDirectory(many); for (int i = 0; i < 2000; i++) File.WriteAllText(Path.Combine(many, $"item{i:0000}-é.txt"), ""); });
        Active.Go(many); await WaitLoaded();
        Check("2000-file folder loaded", Active.Items.Count == 2000, Active.Items.Count.ToString());
        var pick = Active.Items.Skip(5).Take(2).Select(i => i.Name).ToList();
        list.Select(pick.Select(n => Active.Find(Path.Combine(many, n))!).ToList());
        long f0 = Stopwatch.GetTimestamp(); Active.SetFilter("ITEM01"); list.UpdateLayout(); double applyMs = Perf.Ms(f0);
        Check("filter case/diacritic-insensitive", Active.Items.Count == 100 && Active.Total == 2000 && status.Text.Contains("sur 2000"), $"{Active.Items.Count} shown, status '{status.Text}'");
        T($"filter apply {applyMs:F1} ms");
        Active.SetFilter("ITEM0150-E.TXT");
        Check("filter ignores case and diacritics", Active.Items.Count == 1 && Active.Items[0].Name == "item0150-é.txt", Active.Items.Count.ToString());
        f0 = Stopwatch.GetTimestamp(); Active.SetFilter(""); list.UpdateLayout(); double clearMs = Perf.Ms(f0);
        Check("filter cleared", Active.Items.Count == 2000, $"{clearMs:F1} ms");
        T($"filter clear {clearMs:F1} ms");
        Active.SetFilter("zzzz-none"); await Task.Delay(100);
        Check("no-result state", Active.FilterEmpty && content.Child != listHost);
        Active.SetFilter(""); await Task.Delay(100);
        Check("list back after clear", content.Child == listHost);
        Active.Go(F); await WaitLoaded();
        Check("filter cleared by navigation", Active.Filter.Length == 0);
        var resetsEnd = RangeObservableCollection<FileItem>.Resets;
        T($"refresh done: fails={testFails} resets={resetsEnd} max UI stall {Perf.Stall?.Max ?? -1:F1} ms");
        Check("refresh selftest overall", testFails == 0);
        Close();
    }

    void Shot(string path) {
        var root = (FrameworkElement)Content; int w = Math.Max(1, (int)root.ActualWidth), h = Math.Max(1, (int)root.ActualHeight);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) {
            var c = UI.T(M3Role.Surface).Color;
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); rtb.Render(dv);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path); enc.Save(fs);
        T("shot " + path);
    }

    async Task StatesShots() {
        await WaitLoaded();
        string F = Fixture();
        string dir = Environment.GetEnvironmentVariable("WINLESTIA_FILES_SHOTDIR") ?? Path.GetTempPath();
        Activate();
        string empty = Path.Combine(F, "empty-dir"); Directory.CreateDirectory(empty);
        Active.Go(empty); await WaitLoaded(); await Task.Delay(500);
        Shot(Path.Combine(dir, "files-step5-empty.png"));
        Check("empty state shown", content.Child != listHost && Active.State == LoadState.Empty);
        Active.Go(@"C:\System Volume Information"); await WaitLoaded(); await Task.Delay(500);
        Shot(Path.Combine(dir, "files-step5-denied.png"));
        Check("denied state", Active.State == LoadState.Denied, Active.State.ToString());
        Active.Go(F); await WaitLoaded(); await Task.Delay(300);
        ShowFilter("file00");
        await Task.Delay(500);
        Check("filter applied from the field", Active.Filter == "file00" && Active.Items.Count < Active.Total, $"{Active.Items.Count}/{Active.Total}");
        Shot(Path.Combine(dir, "files-step5-filter.png"));
        T($"states done: fails={testFails}");
        Close();
    }
}
