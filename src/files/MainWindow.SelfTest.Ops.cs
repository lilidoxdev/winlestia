// WINLESTIA_FILES_SELFTEST=ops : scripted file operations on a fixture folder (first command-line argument, must live under %TEMP%\WinlestiaFilesOps).
// Fixture layout expected: file001.txt..file060.txt, big\ (500 files), ext\ (x1.txt x2.txt m1.txt). Results: PASS/FAIL lines in %LOCALAPPDATA%\Winlestia\Files\selftest.log.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace WinlestiaFiles;

public sealed partial class MainWindow {
    static readonly string TestLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winlestia", "Files", "selftest.log");
    int testFails;

    static void T(string line) {
        Directory.CreateDirectory(Path.GetDirectoryName(TestLog)!);
        File.AppendAllText(TestLog, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
    }
    void Check(string name, bool ok, string detail = "") { if (!ok) testFails++; T((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? " - " + detail : "")); }

    static async Task<string> Ps(string script) {
        var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-STA", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)) }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string o = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return o.Trim();
    }

    static (int handles, int threads, uint gdi, uint user, double mb, double cpuMs) Snap(bool gc = true) {
        if (gc) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        using var p = Process.GetCurrentProcess();
        return (p.HandleCount, p.Threads.Count, GetGuiResources(p.Handle, 0), GetGuiResources(p.Handle, 1), p.PrivateMemorySize64 / 1048576.0, p.TotalProcessorTime.TotalMilliseconds);
    }

    static int SettleMs => int.TryParse(Environment.GetEnvironmentVariable("WINLESTIA_FILES_SETTLE"), out var s) && s is >= 1 and <= 120 ? s * 1000 : 30000;   // leak settle, seconds (default 30)
    async Task WaitLoaded() { for (int i = 0; i < 400 && Active.State == LoadState.Loading; i++) await Task.Delay(25); await Task.Delay(100); }
    bool OrderOk() { var cur = Active.Items.ToList(); return cur.SequenceEqual(FileSort.Sorted(cur, Active.Sort)); }
    bool DiskMatches() {
        var disk = new HashSet<string>(Directory.GetFileSystemEntries(Active.Location).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        var shown = Active.Items.Select(i => i.Name).ToList();
        return shown.Count == disk.Count && disk.SetEquals(shown);
    }
    void SelectNames(IEnumerable<string> names) => list.Select(names.Select(n => Active.Find(Path.Combine(Active.Location, n))).Where(i => i != null).Cast<FileItem>().ToList());
    static IEnumerable<string> Files(int from, int to) => Enumerable.Range(from, to - from + 1).Select(i => $"file{i:000}.txt");
    bool StatusOk() => status.Text.StartsWith($"{Active.Items.Count} élément");
    static string Recycled() => "(New-Object -ComObject Shell.Application).NameSpace(10).Items() | ?{ $_.ExtendedProperty('System.Recycle.DeletedFrom') -like '*WinlestiaFilesOps*' } | measure | % Count";

    // WINLESTIA_FILES_OPS_NOBIN=1: fixture deletes are permanent and silent, so the run adds nothing to the Recycle Bin.
    static readonly bool noBin = Environment.GetEnvironmentVariable("WINLESTIA_FILES_OPS_NOBIN") == "1";
    async Task<OpResult?> TDelete(IReadOnlyList<FileItem> items) {
        if (!noBin) return await DeleteAsync(items, false);
        var result = await FileOps.Delete(Hwnd, items.Select(i => i.FullPath).ToList(), true, WatchSet());
        ApplyResult(result, false);
        return result;
    }

    async Task OpsTest() {
        string fixture = Path.GetFullPath(Active.Location);
        string allowed = Path.Combine(Path.GetTempPath(), "WinlestiaFilesOps");
        if (!fixture.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) { T("ABORT fixture outside %TEMP%\\WinlestiaFilesOps: " + fixture); Close(); return; }
        T("START ops on " + fixture);
        FileOps.Quiet = true;
        await WaitLoaded();
        string F = fixture;
        Check("initial load", Active.Items.Count == 62 && DiskMatches() && OrderOk(), $"{Active.Items.Count} items");

        // 1. new folder (+ auto-unique name + inline rename)
        var n1 = await NewFolderAsync();
        Check("new folder created", n1 != null && n1.Name == "Nouveau dossier" && Directory.Exists(Path.Combine(F, "Nouveau dossier")) && DiskMatches() && OrderOk(), n1?.Name ?? "null");
        Check("new folder enters rename mode", renaming == n1 && renamePanel.Visibility == Visibility.Visible && renameBox.SelectionLength == "Nouveau dossier".Length);
        CancelRename();
        var n2 = await NewFolderAsync();
        Check("new folder auto-unique name", n2 != null && n2.Name == "Nouveau dossier (2)" && DiskMatches() && OrderOk(), n2?.Name ?? "null");
        renameBox.Text = "a/b";
        Check("rename invalid chars shows inline error", renameError.Visibility == Visibility.Visible && !ValidateRename());
        renameBox.Text = "Nouveau dossier";
        Check("rename duplicate shows inline error", renameError.Visibility == Visibility.Visible);
        renameBox.Text = "Cible"; await CommitRename();
        Check("rename folder (Nouveau dossier (2) -> Cible)", Directory.Exists(Path.Combine(F, "Cible")) && !Directory.Exists(Path.Combine(F, "Nouveau dossier (2)")) && DiskMatches() && OrderOk()
            && Active.Find(Path.Combine(F, "Cible")) is { } c && list.SelectedItem == c && StatusOk(), status.Text);
        BeginRename(Active.Find(Path.Combine(F, "Nouveau dossier"))!); renameBox.Text = "Sub"; await CommitRename();
        Check("rename folder (Nouveau dossier -> Sub)", Directory.Exists(Path.Combine(F, "Sub")) && DiskMatches() && OrderOk());
        SelectNames(new[] { "file060.txt" }); RenameSelected();
        Check("rename file selects name without extension", renaming != null && renameBox.SelectionStart == 0 && renameBox.SelectionLength == "file060".Length);
        renameBox.Text = "renamed.txt"; await CommitRename();
        Check("rename file", File.Exists(Path.Combine(F, "renamed.txt")) && !File.Exists(Path.Combine(F, "file060.txt")) && DiskMatches() && OrderOk() && Active.Find(Path.Combine(F, "file060.txt")) == null);
        Check("rename leaves overlay closed", renaming == null && renamePanel.Visibility == Visibility.Collapsed);

        // 2. copy 50 files to a subfolder (clipboard round trip), then a same-folder paste
        SelectNames(Files(1, 50)); await CopySelectedAsync(false);
        Check("copy puts CF_HDROP on the clipboard", Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Count == 50);
        int before = Active.Items.Count;
        var r = await PasteAsync(Path.Combine(F, "Cible"));
        Check("paste 50 copies into Cible", r != null && Directory.GetFiles(Path.Combine(F, "Cible")).Length == 50 && Active.Items.Count == before && DiskMatches(), $"{Directory.GetFiles(Path.Combine(F, "Cible")).Length} files");
        SelectNames(new[] { "file055.txt" }); await CopySelectedAsync(false);
        r = await PasteAsync();
        Check("paste in same folder renames (no overwrite)", r != null && Active.Items.Count == before + 1 && DiskMatches() && OrderOk() && File.Exists(Path.Combine(F, "file055.txt")),
            Active.Items.FirstOrDefault(i => i.Name.StartsWith("file055 - "))?.Name ?? "none");

        // 3. cut state + cut/paste 10 files into Sub
        SelectNames(Files(31, 40)); await CopySelectedAsync(true);
        Check("cut dims the items (0.5)", Files(31, 40).All(n => Active.Find(Path.Combine(F, n))!.CutOpacity == 0.5));
        SelectNames(new[] { "file041.txt" }); await CopySelectedAsync(false);
        await Task.Delay(400);
        Check("another copy clears the cut state (WM_CLIPBOARDUPDATE)", Files(31, 40).All(n => Active.Find(Path.Combine(F, n))!.CutOpacity == 1.0));
        SelectNames(Files(31, 40)); await CopySelectedAsync(true);
        before = Active.Items.Count;
        r = await PasteAsync(Path.Combine(F, "Sub"));
        Check("cut/paste 10 files into Sub", r != null && Directory.GetFiles(Path.Combine(F, "Sub")).Length == 10 && Files(31, 40).All(n => !File.Exists(Path.Combine(F, n)))
            && Active.Items.Count == before - 10 && DiskMatches() && OrderOk() && StatusOk(), $"{Active.Items.Count} items, {status.Text}");
        Check("clipboard cleared after cut-paste", !Clipboard.ContainsFileDropList() && FileItem.CutPaths.Count == 0);

        // 4. delete to the Recycle Bin (5 single path, then 10 = bulk path)
        string binBefore = noBin ? "0" : await Ps(Recycled());
        before = Active.Items.Count;
        r = await TDelete(Files(1, 5).Select(n => Active.Find(Path.Combine(F, n))!).ToList());
        Check("delete 5 to Recycle Bin", r != null && r.Removed.Count == 5 && Files(1, 5).All(n => !File.Exists(Path.Combine(F, n))) && Active.Items.Count == before - 5 && DiskMatches() && OrderOk() && StatusOk());
        before = Active.Items.Count;
        r = await TDelete(Files(6, 15).Select(n => Active.Find(Path.Combine(F, n))!).ToList());
        Check("delete 10 (bulk path)", r != null && r.Removed.Count == 10 && Active.Items.Count == before - 10 && DiskMatches() && OrderOk());
        if (noBin) T("SKIP Recycle Bin count (WINLESTIA_FILES_OPS_NOBIN=1: deletes are permanent, nothing goes to the Recycle Bin)");
        else {
            string binAfter = await Ps(Recycled());
            Check("deleted items are in the Recycle Bin", int.TryParse(binAfter, out int a) && int.TryParse(binBefore, out int b) && a - b == 15, $"{binBefore} -> {binAfter}");
        }

        // 5. clipboard interop with Explorer-style consumers/producers
        SelectNames(Files(21, 23)); await CopySelectedAsync(false);
        string outCopy = await Ps("Add-Type -AssemblyName System.Windows.Forms; $f=(Get-Clipboard -Format FileDropList | %{ $_.Name }) -join '|'; $s=[Windows.Forms.Clipboard]::GetData('Preferred DropEffect'); $b=New-Object byte[] 4; $null=$s.Read($b,0,4); \"effect=$([BitConverter]::ToInt32($b,0));$f\"");
        Check("interop out: copy seen by PowerShell (FileDropList + DropEffect=1)", outCopy == "effect=1;file021.txt|file022.txt|file023.txt", outCopy);
        SelectNames(Files(21, 23)); await CopySelectedAsync(true);
        string outCut = await Ps("Add-Type -AssemblyName System.Windows.Forms; $s=[Windows.Forms.Clipboard]::GetData('Preferred DropEffect'); $b=New-Object byte[] 4; $null=$s.Read($b,0,4); \"effect=$([BitConverter]::ToInt32($b,0))\"");
        Check("interop out: cut has DropEffect=2", outCut == "effect=2", outCut);
        await CopySelectedAsync(false);   // drop the cut state again
        string ext = Path.Combine(F, "ext");
        string setIn(int effect, string[] names) => "Add-Type -AssemblyName System.Windows.Forms; $sc=New-Object Collections.Specialized.StringCollection; " + string.Join(";", names.Select(n => $"[void]$sc.Add('{Path.Combine(ext, n)}')"))
            + $"; $d=New-Object Windows.Forms.DataObject; $d.SetFileDropList($sc); $ms=New-Object IO.MemoryStream(,[byte[]]({effect},0,0,0)); $d.SetData('Preferred DropEffect',$ms); [Windows.Forms.Clipboard]::SetDataObject($d,$true); 'ok'";
        string setPlain = $"Set-Clipboard -Path '{Path.Combine(ext, "x1.txt")}'; 'ok'";
        await Ps(setPlain);
        before = Active.Items.Count;
        r = await PasteAsync();
        Check("interop in: Set-Clipboard -Path then our paste (copy)", r != null && File.Exists(Path.Combine(F, "x1.txt")) && File.Exists(Path.Combine(ext, "x1.txt")) && Active.Items.Count == before + 1 && Active.Find(Path.Combine(F, "x1.txt")) != null && DiskMatches() && OrderOk());
        await Ps(setIn(1, new[] { "x2.txt" }));
        r = await PasteAsync();
        Check("interop in: DropEffect=1 paste", File.Exists(Path.Combine(F, "x2.txt")) && File.Exists(Path.Combine(ext, "x2.txt")) && DiskMatches());
        await Ps(setIn(2, new[] { "m1.txt" }));
        r = await PasteAsync();
        Check("interop in: DropEffect=2 paste moves", File.Exists(Path.Combine(F, "m1.txt")) && !File.Exists(Path.Combine(ext, "m1.txt")) && DiskMatches() && OrderOk() && !Clipboard.ContainsFileDropList());

        // 6. 500-file copy: UI stall while the operation runs and the list is patched
        Directory.CreateDirectory(Path.Combine(F, "Big2")); File.WriteAllText(Path.Combine(F, "Big2", "seed.txt"), "seed");
        Active.Go(Path.Combine(F, "Big2")); await WaitLoaded();
        var bigPaths = Directory.GetFiles(Path.Combine(F, "big")).ToList();
        await SetClipboardAsync(bigPaths, false);
        var stall = new StallMonitor(); stall.Reset(); 
        long t0 = Stopwatch.GetTimestamp();
        r = await PasteAsync();
        double op = Perf.Ms(t0);
        await Task.Delay(600);
        Check("paste 500 files into the open folder", r != null && Directory.GetFiles(Path.Combine(F, "Big2")).Length == 501 && Active.Items.Count == 501 && DiskMatches() && OrderOk() && list.SelectedItems.Count == 500, $"{Active.Items.Count} items, selected {list.SelectedItems.Count}");
        T($"MEASURE 500-file copy: operation {op:F0} ms, max UI stall {stall.Max:F1} ms");
        Check("500-file copy UI stall < 100 ms", stall.Max < 100, $"{stall.Max:F1} ms");
        stall.Stop();
        Active.Go(F); await WaitLoaded();

        // 7. resource cycles
        await Task.Delay(3000);
        var s0 = Snap();
        T($"MEASURE before cycles: handles {s0.handles}, threads {s0.threads}, gdi {s0.gdi}, user {s0.user}, private {s0.mb:F1} MB");
        for (int i = 0; i < 10; i++) {
            var made = await NewFolderAsync();
            if (made == null) { Check("cycle new folder " + i, false); break; }
            renameBox.Text = "cyc" + i; await CommitRename();
            string cyc = Path.Combine(F, "cyc" + i);
            SelectNames(Files(16, 20)); await CopySelectedAsync(false);
            await PasteAsync(cyc);
            await TDelete(new[] { Active.Find(cyc)! });
        }
        Check("cycles leave the list consistent", DiskMatches() && OrderOk());
        await Task.Delay(SettleMs);
        var s1 = Snap();
        T($"MEASURE after 10 cycles + 30 s: handles {s1.handles}, threads {s1.threads}, gdi {s1.gdi}, user {s1.user}, private {s1.mb:F1} MB");
        Check("no handle/thread/GDI growth", s1.handles - s0.handles <= 10 && s1.threads - s0.threads <= 2 && (int)s1.gdi - (int)s0.gdi <= 5 && (int)s1.user - (int)s0.user <= 5,
            $"handles {s1.handles - s0.handles:+#;-#;0}, threads {s1.threads - s0.threads:+#;-#;0}, gdi {(int)s1.gdi - (int)s0.gdi:+#;-#;0}, user {(int)s1.user - (int)s0.user:+#;-#;0}");

        // 8. idle CPU
        await Task.Delay(2000);
        var c0 = Snap(false); await Task.Delay(10000); var c1 = Snap(false);
        T($"MEASURE idle CPU over 10 s: {c1.cpuMs - c0.cpuMs:F0} ms");
        Check("idle CPU ~0", c1.cpuMs - c0.cpuMs < 50, $"{c1.cpuMs - c0.cpuMs:F0} ms / 10 s");

        T($"DONE fails={testFails}");
        Close();
    }

    // Screenshot helper: opens the inline editor on the 3rd file and keeps it open for 40 s.
    async Task RenameShot() {
        await WaitLoaded(); await Task.Delay(1500);
        Activate();
        var item = Active.Items.Skip(2).FirstOrDefault(i => !i.IsDir) ?? Active.Items[0];
        list.Select(new[] { item }); RenameSelected();
        T("renameshot ready");
        await Task.Delay(40000);
        Close();
    }
}
