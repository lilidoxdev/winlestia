// WINLESTIA_FILES_SELFTEST=xfer : big copy (3000 small files + 300 MB file) with native progress window + UI responsiveness, locked file (move, recycle) with the native
// error dialog dismissed through UI Automation, access-denied folder (navigation message + copy from it), cross-drive copy / move when a second fixed writable drive exists.
// Needs WINLESTIA_FILES_DATA_DIR under %TEMP% and a fixture folder under %TEMP%\WinlestiaFilesOps (harness). Everything created is removed at the end (ACLs restored first).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    static bool HasText(DependencyObject d, string text)
    {
        if (d is System.Windows.Controls.TextBlock tb && tb.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
            return true;
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(d); i < n; i++)
            if (HasText(VisualTreeHelper.GetChild(d, i), text))
                return true;
        return false;
    }

    static int RunExe(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        if (!p.WaitForExit(20000))
        {
            try
            {
                p.Kill();
            }
            catch { }
            return -1;
        }
        return p.ExitCode;
    }

    async Task<OpResult?> PasteFiles(IReadOnlyList<string> paths, bool cut, string dest, int timeoutMs, params UiaStep[] steps)
    {
        Check("clipboard set (" + Path.GetFileName(paths[0]) + ")", await SetClipboardAsync(paths, cut));
        var drv = steps.Length > 0 ? Uia.Run(MainHwnd, steps) : Task.FromResult(new List<string>());
        var paste = PasteAsync(dest);
        if (await Task.WhenAny(paste, Task.Delay(timeoutMs)) != paste)
        {
            T("TIMEOUT paste to " + dest);
            foreach (var w in Uia.Windows(MainHwnd))
                foreach (var l in Uia.Dump(w, 40))
                    T("  uia " + l);
            return null;
        }
        LogUia("paste", await drv);
        return await paste;
    }

    async Task XferTest()
    {
        string F0 = Fixture();
        string tmp = Path.GetFullPath(Path.GetTempPath());
        if (!Path.GetFullPath(FilesSettings.Dir).StartsWith(tmp, StringComparison.OrdinalIgnoreCase) || Environment.GetEnvironmentVariable("WINLESTIA_FILES_DATA_DIR") is not { Length: > 0 })
        {
            T("ABORT data dir is not a %TEMP% folder");
            Close();
            return;
        }
        T("START xfer fixture " + F0);
        Activate();
        FileOps.Quiet = false;
        await WaitLoaded();
        _ = MainHwnd;
        string X = Path.Combine(F0, "XF");
        string sid = "*" + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        string denyDir = Path.Combine(X, "denied");
        string? crossRoot = null;
        string[] errTokens = { "=Ignorer", "=Skip", "=Annuler", "=Cancel", "=OK", "=Fermer", "=Close" };     // "=" : exact Name (not the progress window's "Annuler la vignette")
        try
        {
            Directory.CreateDirectory(X);

            // ---- (a) big copy ----
            string srcA = Path.Combine(X, "bulk"), dstA = Path.Combine(X, "bulk-dst");
            Directory.CreateDirectory(srcA);
            Directory.CreateDirectory(dstA);
            await Task.Run(() =>
            {
                for (int i = 0; i < 3000; i++)
                    File.WriteAllText(Path.Combine(srcA, $"f{i:0000}.txt"), "small " + i);
                using var fs = new FileStream(Path.Combine(srcA, "big.bin"), FileMode.Create);
                fs.SetLength(300L * 1024 * 1024);
                fs.Position = 0;
                fs.Write(new byte[] { 1, 2, 3, 4 });
                fs.Position = fs.Length - 4;
                fs.Write(new byte[] { 9, 8, 7, 6 });
            });
            Active.Go(dstA);
            await WaitLoaded();
            Check("clipboard set (bulk)", await SetClipboardAsync(new[] { srcA }, false));
            var seen = new List<string>();
            bool copying = true;
            var watcher = Task.Run(() =>
            {
                while (copying)
                {
                    foreach (var w in Uia.Windows(MainHwnd))
                    {
                        string d = Uia.Describe(w);
                        if (!seen.Contains(d))
                        {
                            seen.Add(d);
                            try
                            {
                                seen.AddRange(Uia.Dump(w, 12).Skip(1).Select(x => "    " + x));
                            }
                            catch { }
                        }
                    }
                    Task.Delay(200).Wait();
                }
            });
            long worst = 0;
            int pings = 0;
            var sw = Stopwatch.StartNew();
            var paste = PasteAsync(dstA);
            while (!paste.IsCompleted && sw.Elapsed.TotalSeconds < 100)
            {
                long t0 = Stopwatch.GetTimestamp();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Normal);
                worst = Math.Max(worst, (long)Perf.Ms(t0));
                pings++;
                await Task.Delay(40);
            }
            double secs = sw.Elapsed.TotalSeconds;
            copying = false;
            await watcher;
            T($"  bulk copy took {secs:F1} s, {pings} pings, worst UI ping {worst} ms; native windows: {string.Join(" ; ", seen)}");
            var ra = await paste;
            Check("(a) copy finished without error", ra is { Error: null, Aborted: false }, ra?.Error ?? "null");
            Check("(a) native progress window seen via UIA", seen.Count > 0 || secs < 1.5, seen.Count > 0 ? seen[0] : $"none in {secs:F1} s");
            Check("(a) UI thread stays responsive (worst dispatcher ping < 200 ms)", pings > 5 && worst < 200, $"{worst} ms over {pings} pings");
            string outDir = Path.Combine(dstA, "bulk");
            var copied = Directory.Exists(outDir) ? Directory.GetFiles(outDir) : Array.Empty<string>();
            Check("(a) result complete: 3001 files", copied.Length == 3001, copied.Length.ToString());
            string bigOut = Path.Combine(outDir, "big.bin");
            bool bigOk = false;
            if (File.Exists(bigOut))
            {
                using var fs = new FileStream(bigOut, FileMode.Open, FileAccess.Read);
                var h = new byte[4];
                var tl = new byte[4];
                fs.Read(h);
                fs.Position = fs.Length - 4;
                fs.Read(tl);
                bigOk = fs.Length == 300L * 1024 * 1024 && h.SequenceEqual(new byte[] { 1, 2, 3, 4 }) && tl.SequenceEqual(new byte[] { 9, 8, 7, 6 });
            }
            Check("(a) 300 MB file intact (size, head, tail)", bigOk);
            Check("(a) list matches disk", DiskMatches());
            try
            {
                Directory.Delete(srcA, true);
                Directory.Delete(dstA, true);
            }
            catch { }

            // ---- (b) locked file ----
            string B = Path.Combine(X, "lock"), dstB = Path.Combine(X, "lock-dst");
            Directory.CreateDirectory(B);
            Directory.CreateDirectory(dstB);
            string locked = Path.Combine(B, "locked.txt");
            File.WriteAllText(locked, "LOCKED");
            File.WriteAllText(Path.Combine(B, "other.txt"), "o");
            using (var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Active.Go(B);
                await WaitLoaded();
                int undo0 = journal.UndoCount;
                var rb = await PasteFiles(new[] { locked }, true, dstB, 45000, new UiaStep("file-in-use dismiss", errTokens, WaitMs: 20000, Closes: true));
                await WaitLoaded();
                Check("(b) move of locked file returned (no hang / crash)", rb != null, rb?.Error ?? "null");
                Check("(b) move: native error dialog detected and closed", Uia.Windows(MainHwnd).Count == 0 && File.Exists(locked) && !File.Exists(Path.Combine(dstB, "locked.txt")), $"windows left {Uia.Windows(MainHwnd).Count}");
                Check("(b) move: list coherent, locked file still listed", DiskMatches() && Active.Items.Any(i => i.Name == "locked.txt"));
                Check("(b) move: nothing recorded in the undo journal", journal.UndoCount == undo0, $"{journal.UndoCount} vs {undo0}");

                var item = Active.Items.FirstOrDefault(i => i.Name == "locked.txt");
                OpResult? rd = null;
                if (item != null)
                {
                    var drv = Uia.Run(MainHwnd, new UiaStep("recycle error dismiss", errTokens, WaitMs: 20000, Closes: true));
                    var del = DeleteAsync(new[] { item }, false);
                    if (await Task.WhenAny(del, Task.Delay(45000)) == del)
                        rd = await del;
                    else
                        T("TIMEOUT delete");
                    LogUia("recycle", await drv);
                }
                await WaitLoaded();
                Check("(b) recycle of locked file returned (no hang / crash)", rd != null, rd?.Error ?? "null");
                Check("(b) recycle: file still there, list coherent", File.Exists(locked) && DiskMatches() && Active.Items.Any(i => i.Name == "locked.txt"), $"exists={File.Exists(locked)} disk={DiskMatches()} listed={Active.Items.Any(i => i.Name == "locked.txt")} items={string.Join(",", Active.Items.Select(i => i.Name))} err={rd?.Error} removed={rd?.Removed.Count}");
                Check("(b) recycle: no dialog left, nothing recorded in the journal", Uia.Windows(MainHwnd).Count == 0 && journal.UndoCount == undo0, $"windows={Uia.Windows(MainHwnd).Count} undo={journal.UndoCount} vs {undo0} next={journal.NextUndo?.Label}");
            }
            T("  OddHr after (b): " + FileOps.OddHr);
            Check("(b) after release the file is readable and unchanged", File.ReadAllText(locked) == "LOCKED");

            // ---- (c) access denied ----
            Directory.CreateDirectory(denyDir);
            string xin = Path.Combine(denyDir, "x.txt");
            File.WriteAllText(xin, "X");
            string dstC = Path.Combine(X, "denied-dst");
            Directory.CreateDirectory(dstC);
            int rc = RunExe("icacls.exe", denyDir, "/deny", sid + ":(OI)(CI)F");
            Check("(c) icacls deny applied", rc == 0, "exit " + rc);
            string crash = Path.Combine(FilesSettings.Dir, "crash.log");
            Active.Go(X);
            await WaitLoaded();
            Active.Go(denyDir);
            await WaitLoaded();
            await Task.Delay(300);
            Check("(c) navigation into denied folder: state Denied", Active.State == LoadState.Denied, Active.State.ToString());
            Check("(c) navigation: clear message shown in the app", HasText(this, "Accès refusé"));
            Check("(c) navigation: no crash.log", !File.Exists(crash));
            Active.Go(dstC);
            await WaitLoaded();
            var rcopy = await PasteFiles(new[] { xin }, false, dstC, 45000, new UiaStep("access-denied dismiss", errTokens, WaitMs: 7000, Closes: true));
            await WaitLoaded();
            Check("(c) copy from denied folder returned, handled", rcopy != null, rcopy?.Error ?? "null");
            Check("(c) copy from denied folder: nothing copied, no dialog left, no crash.log", !File.Exists(Path.Combine(dstC, "x.txt")) && Uia.Windows(MainHwnd).Count == 0 && !File.Exists(crash));
            int rr = RunExe("icacls.exe", denyDir, "/remove:d", sid, "/T", "/C");
            Check("(c) rights restored", rr == 0 && File.Exists(xin) && File.ReadAllText(xin) == "X", "exit " + rr);

            // ---- (d) cross drive ----
            var other = DriveInfo.GetDrives().FirstOrDefault(d => d.DriveType == DriveType.Fixed && d.IsReady && !string.Equals(d.Name, Path.GetPathRoot(tmp), StringComparison.OrdinalIgnoreCase) && d.AvailableFreeSpace > 1L << 30);
            if (other == null)
                T("NOT TESTED (d): no second fixed drive");
            else
            {
                string candidate = Path.Combine(other.Name, "WinlestiaFilesXfer-" + Guid.NewGuid().ToString("N")[..8]);
                try
                {
                    Directory.CreateDirectory(candidate);
                    crossRoot = candidate;
                }
                catch (Exception ex) { T("NOT TESTED (d): " + other.Name + " not writable: " + ex.Message); }
                if (crossRoot != null)
                {
                    string srcD = Path.Combine(X, "cross");
                    Directory.CreateDirectory(srcD);
                    string cp = Path.Combine(srcD, "c.bin"), mv = Path.Combine(srcD, "m.bin");
                    var data = new byte[2 * 1024 * 1024];
                    new Random(1).NextBytes(data);
                    File.WriteAllBytes(cp, data);
                    File.WriteAllBytes(mv, data);
                    Active.Go(crossRoot);
                    await WaitLoaded();
                    var r1 = await PasteFiles(new[] { cp }, false, crossRoot, 60000);
                    Check($"(d) copy to {other.Name}: ok", r1 is { Error: null } && File.Exists(Path.Combine(crossRoot, "c.bin")) && File.Exists(cp) && File.ReadAllBytes(Path.Combine(crossRoot, "c.bin")).SequenceEqual(data), r1?.Error ?? "null");
                    var r2 = await PasteFiles(new[] { mv }, true, crossRoot, 60000);
                    Check($"(d) move to {other.Name}: ok, source gone", r2 is { Error: null } && File.Exists(Path.Combine(crossRoot, "m.bin")) && !File.Exists(mv) && File.ReadAllBytes(Path.Combine(crossRoot, "m.bin")).SequenceEqual(data), r2?.Error ?? "null");
                    await WaitLoaded();
                    Check("(d) list matches disk", DiskMatches());
                }
            }
        }
        catch (Exception ex) { Check("xfer test exception", false, ex.ToString()); }
        finally
        {
            try
            {
                RunExe("icacls.exe", denyDir, "/remove:d", sid, "/T", "/C");
            }
            catch { }
            Active.Go(F0);
            await WaitLoaded();
            try
            {
                Directory.Delete(X, true);
            }
            catch (Exception ex) { T("cleanup X failed: " + ex.Message); }
            if (crossRoot != null)
            {
                try
                {
                    Directory.Delete(crossRoot, true);
                }
                catch (Exception ex) { T("cleanup cross failed: " + ex.Message); }
            }
        }
        T($"DONE fails={testFails}");
        Close();
    }
}
