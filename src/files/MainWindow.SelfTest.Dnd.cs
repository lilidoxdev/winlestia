// WINLESTIA_FILES_SELFTEST=dnd : native context menu + drag & drop on a fixture folder (first command-line argument, must live under %TEMP%\WinlestiaFilesOps).
// Fixture: file001.txt..file012.txt, ctxtest.txt, folders Cible, Autre, Third. WINLESTIA_FILES_SHOTDIR = where the two screenshots are written.
// Only harmless verbs are invoked natively ("properties", whose window is closed again); delete is never sent to the Recycle Bin.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace WinlestiaFiles;

public sealed partial class MainWindow {
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RectN rect);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] struct RectN { public int L, T, R, B; }

    static IntPtr FindWindowTitled(string needle) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) => {
            var sb = new StringBuilder(256);
            if (IsWindowVisible(h) && GetWindowTextW(h, sb, 256) > 0 && sb.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    NPoint RowPt(FileItem item) {
        item = Active.Find(item.FullPath) ?? item;           // auto refresh may have swapped the row object (a folder whose content changed)
        list.ScrollIntoView(item); list.UpdateLayout();
        var box = (FrameworkElement)list.ItemContainerGenerator.ContainerFromItem(item);
        var p = box.PointToScreen(new Point(Math.Min(300, box.ActualWidth / 2), box.ActualHeight / 2));
        return new NPoint((int)p.X, (int)p.Y);
    }

    static NPoint PtOf(FrameworkElement e, double x, double y) { var p = e.PointToScreen(new Point(x, y)); return new NPoint((int)p.X, (int)p.Y); }

    static Border? FindPill(DependencyObject d) {
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(d); i < n; i++) {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is Border b && b.ToolTip is string) return b;
            if (FindPill(c) is { } found) return found;
        }
        return null;
    }

    // Screenshot of the screen area around the window, taken by a separate process (the UI thread may be inside a modal menu loop); optionally presses Esc afterwards.
    Process? StartShot(string png, int delayMs, bool esc) {
        GetWindowRect(Hwnd, out var r);
        string script = $@"
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Namespace N -Name W -MemberDefinition '[DllImport(""user32.dll"")] public static extern bool SetProcessDPIAware(); [DllImport(""user32.dll"")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);'
[void][N.W]::SetProcessDPIAware()
Start-Sleep -Milliseconds {delayMs}
$want = New-Object Drawing.Rectangle {Math.Max(0, r.L - 10)},{Math.Max(0, r.T - 10)},{r.R - r.L + 420},{r.B - r.T + 120}
$rect = [Drawing.Rectangle]::Intersect($want, [Windows.Forms.SystemInformation]::VirtualScreen)
$bmp = New-Object Drawing.Bitmap $rect.Width,$rect.Height
$g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($rect.Location,[Drawing.Point]::Empty,$rect.Size); $bmp.Save('{png}'); $g.Dispose(); $bmp.Dispose()
if ({(esc ? "$true" : "$false")}) {{ Start-Sleep -Milliseconds 500; [N.W]::keybd_event(0x1B,0,0,[UIntPtr]::Zero); [N.W]::keybd_event(0x1B,0,2,[UIntPtr]::Zero) }}";
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-STA", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) psi.ArgumentList.Add(a);
        return Process.Start(psi);
    }

    async Task DndTest() {
        string F = Path.GetFullPath(Active.Location);
        string allowed = Path.Combine(Path.GetTempPath(), "WinlestiaFilesOps");
        if (!F.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) { T("ABORT fixture outside %TEMP%\\WinlestiaFilesOps: " + F); Close(); return; }
        T("START dnd on " + F);
        FileOps.Quiet = true;
        await WaitLoaded(); await Task.Delay(800);
        string P(string n) => Path.Combine(F, n);
        FileItem It(string n) => Active.Find(P(n))!;
        string shotDir = Environment.GetEnvironmentVariable("WINLESTIA_FILES_SHOTDIR") ?? Path.GetTempPath();
        Activate(); list.Focus();
        Check("initial load", Active.Items.Count == 16 && DiskMatches() && OrderOk(), $"{Active.Items.Count} items");
        Check("main HWND drop target is OUR IDropTarget (WPF's replaced)", OwnsDropTarget());
        var target = (IDropTarget)dropTarget!;

        // 1. shell data object -> CF_HDROP -> FileOps (copy, then move), list patched in place
        using (var si = ShellItems.Of(new[] { P("file001.txt"), P("file002.txt") })) {
            var read = ReadFiles(si!.ManagedDataObject(Hwnd)!);
            Check("shell data object exposes CF_HDROP with both files", read.Length == 2 && read[0] == P("file001.txt") && read[1] == P("file002.txt"), string.Join("|", read));
        }
        int before = Active.Items.Count;
        var r = await DropFilesAsync(new[] { P("file001.txt"), P("file002.txt") }, P("Cible"), false);
        Check("drop copy into Cible", File.Exists(Path.Combine(F, "Cible", "file001.txt")) && File.Exists(Path.Combine(F, "Cible", "file002.txt")) && File.Exists(P("file001.txt")) && Active.Items.Count == before && DiskMatches());
        r = await DropFilesAsync(new[] { P("file003.txt"), P("file004.txt") }, P("Cible"), true);
        Check("drop move into Cible (list updated in place)", File.Exists(Path.Combine(F, "Cible", "file003.txt")) && !File.Exists(P("file003.txt")) && !File.Exists(P("file004.txt")) && Active.Items.Count == before - 2 && DiskMatches() && OrderOk() && StatusOk());

        // 2. through the real IDropTarget object (hit-test, effect, highlight, helper, drop)
        var cible = It("Cible"); var autre = It("Autre");
        using (var si = ShellItems.Of(new[] { P("file005.txt") })) {
            var data = si!.ManagedDataObject(Hwnd)!;
            var ptAutre = RowPt(autre); var ptCible = RowPt(cible); var ptFile = RowPt(It("file006.txt"));
            int e = 3;
            target.DragEnter(data, 0, ptAutre, ref e);
            Check("DragEnter over a folder row: MOVE (same volume) + row highlight", e == 2 && autre.DropHot && !cible.DropHot, $"effect {e}");
            e = 3; target.DragOver(8, ptAutre, ref e);
            Check("Ctrl forces COPY", e == 1);
            e = 3; target.DragOver(4, ptAutre, ref e);
            Check("Shift forces MOVE", e == 2);
            e = 1; target.DragOver(0, ptAutre, ref e);
            Check("allowed effects are honoured (copy-only source)", e == 1);
            e = 3; target.DragOver(0, ptCible, ref e);
            Check("highlight follows the hovered folder", e == 2 && cible.DropHot && !autre.DropHot);
            e = 3; target.DragOver(0, ptFile, ref e);
            Check("over a file row = current folder, move within same folder is a no-op (NONE), no highlight", e == 0 && !cible.DropHot && !autre.DropHot, $"effect {e}");
            e = 3; target.DragOver(8, ptFile, ref e);
            Check("Ctrl over current folder = COPY", e == 1);
            e = 3; target.DragOver(0, PtOf(back, 4, 4), ref e);
            Check("over the toolbar: not a drop target", e == 0);
            e = 3; target.DragOver(8, ptAutre, ref e);
            e = 3; target.Drop(data, 8, ptAutre, ref e);
            await LastDrop;
            Check("Drop with Ctrl: returns COPY, file copied to Autre, source kept, highlight cleared", e == 1 && File.Exists(Path.Combine(F, "Autre", "file005.txt")) && File.Exists(P("file005.txt")) && !autre.DropHot && dndSpot == null);
        }
        using (var si = ShellItems.Of(new[] { P("file007.txt") })) {
            var data = si!.ManagedDataObject(Hwnd)!;
            var ptAutre = RowPt(autre);
            int e = 3; target.DragEnter(data, 0, ptAutre, ref e);
            before = Active.Items.Count;
            e = 3; target.Drop(data, 0, ptAutre, ref e);
            await LastDrop;
            Check("Drop (move): file moved by FileOps, NONE reported to the source (optimized move), list updated", e == 0 && File.Exists(Path.Combine(F, "Autre", "file007.txt")) && !File.Exists(P("file007.txt")) && Active.Items.Count == before - 1 && DiskMatches() && OrderOk(), $"effect {e}");
        }
        using (var si = ShellItems.Of(new[] { P("Cible") })) {
            var data = si!.ManagedDataObject(Hwnd)!;
            int e = 3; target.DragEnter(data, 0, RowPt(cible), ref e);
            Check("folder dragged onto itself: not a drop target", e == 0 && !cible.DropHot);
            target.DragLeave();
        }
        using (var si = ShellItems.Of(new[] { P("Autre") })) {
            var data = si!.ManagedDataObject(Hwnd)!;
            int e = 3; target.DragEnter(data, 0, RowPt(cible), ref e);
            Check("folder dragged onto another folder: MOVE + highlight", e == 2 && cible.DropHot);
            target.DragLeave();
            Check("DragLeave clears highlight and state", !cible.DropHot && dndSpot == null && dndFiles.Length == 0);
        }

        // 3. sidebar entry and tab (600 ms hover switch)
        await Task.Delay(500);
        var pill = FindPill(sidebar.Root);
        using (var si = ShellItems.Of(new[] { P("file008.txt") })) {
            var data = si!.ManagedDataObject(Hwnd)!;
            if (pill != null) {
                int e = 3; target.DragEnter(data, 0, PtOf(pill, 20, pill.ActualHeight / 2), ref e);
                Check("sidebar entry accepts the drop and is highlighted", e != 0 && dndSpot is { Side: true } && dndSpot.Path == (string)pill.ToolTip, $"effect {e}, {(string)pill.ToolTip}");
                target.DragLeave();
                Check("sidebar highlight cleared", dndSpot == null);
            } else Check("sidebar entry found", false);
            var second = AddTab(P("Third")); await Task.Delay(500);
            Activate(tabs[0]); await Task.Delay(300);
            int f = 3; target.DragEnter(data, 0, PtOf(second.Pill, 20, 16), ref f);
            Check("tab accepts the drop and is highlighted", f != 0 && second.Drop && activeView == tabs[0], $"effect {f}");
            await Task.Delay(1000);
            Check("hovering a tab 600 ms switches to it (one-shot timer, then gone)", activeView == second && tabTimer == null);
            target.DragLeave();
            Check("tab highlight cleared", !second.Drop && tabTimer == null);
            CloseTab(second); await WaitLoaded();
            Check("back on the fixture tab", Norm(Active.Location) == Norm(F) && tabs.Count == 1);
        }

        // 4. context menu: verbs, intercepts, properties
        list.Select(new[] { It("ctxtest.txt") });
        string[] expected = { "open", "copy", "cut", "delete", "rename", "properties" };
        using (var s = BuildMenu(Selection(), false, false)) {
            Check("native item menu built", s != null);
            if (s != null) {
                var all = s.Menu.Enumerate(true);
                foreach (var m in all) T($"  menu item: id={m.id} verb='{m.verb}' text='{m.text}' depth={m.depth}");
                var verbs = all.Select(m => m.verb.ToLowerInvariant()).ToHashSet();
                Check("standard verbs present (open, copy, cut, delete, rename, properties)", expected.All(verbs.Contains), "missing: " + string.Join(",", expected.Where(v => !verbs.Contains(v))));
                Check("our own entries (copy path, open in Explorer) are in the menu", all.Count(m => m.id is >= 1 and <= 3) == 2, $"{all.Count(m => m.id is >= 1 and <= 3)}");
                uint IdOf(string v) => all.First(m => m.verb.Equals(v, StringComparison.OrdinalIgnoreCase)).id;

                uint props = IdOf("properties");
                bool invoked = s.Menu.Invoke(Hwnd, props, 0, 0, false);
                IntPtr win = IntPtr.Zero;
                for (int i = 0; i < 60 && win == IntPtr.Zero; i++) { await Task.Delay(100); win = FindWindowTitled("ctxtest"); }
                Check("native 'properties' invoked: Properties window appeared", invoked && win != IntPtr.Zero);
                if (win != IntPtr.Zero) {
                    // The sheet can ignore a WM_CLOSE sent while it is still initialising: retry (WM_CLOSE, then SC_CLOSE) until it is gone.
                    for (int i = 0; i < 100 && FindWindowTitled("ctxtest") != IntPtr.Zero; i++) {
                        if (i % 10 == 0) PostMessageW(FindWindowTitled("ctxtest"), 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                        else if (i % 5 == 0) PostMessageW(FindWindowTitled("ctxtest"), 0x0112 /* WM_SYSCOMMAND */, (IntPtr)0xF060 /* SC_CLOSE */, IntPtr.Zero);
                        await Task.Delay(100);
                    }
                    Check("Properties window closed again", FindWindowTitled("ctxtest") == IntPtr.Zero);
                }

                await ExecuteMenuCommand(s, IdOf("rename"), default, false); await Task.Delay(400);
                Check("intercepted 'rename' starts our inline rename", renaming != null && renaming.Name == "ctxtest.txt" && renamePanel.Visibility == Visibility.Visible);
                CancelRename();
                await ExecuteMenuCommand(s, IdOf("copy"), default, false);
                Check("intercepted 'copy' uses our clipboard (CF_HDROP, no cut)", Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Count == 1 && FileItem.CutPaths.Count == 0);
                await ExecuteMenuCommand(s, IdOf("cut"), default, false);
                Check("intercepted 'cut' uses our clipboard (item dimmed)", It("ctxtest.txt").CutOpacity == 0.5 && Clipboard.ContainsFileDropList());
                await CopySelectedAsync(false);
                Check("copy again clears the cut dimming", It("ctxtest.txt").CutOpacity == 1.0);
            }
        }
        list.Select(new[] { It("Cible") });   // re-find: reloads (tab close, Go) replace the row objects, a stale item would select nothing
        using (var s = BuildMenu(Selection(), false, true)) {
            var all = s!.Menu.Enumerate(false);
            T("  folder menu (shift) verbs: " + string.Join(",", all.Where(m => m.verb.Length > 0).Select(m => m.verb)));
            Check("folder menu: open + extended verbs with Shift", all.Any(m => m.verb == "open"));
            await ExecuteMenuCommand(s, all.First(m => m.verb == "open").id, default, false);
            await Task.Delay(300); await WaitLoaded();
            Check("intercepted 'open' on a folder navigates in our tab", Norm(Active.Location) == Norm(P("Cible")), Active.Location);
            Active.Go(F); await WaitLoaded();
        }
        using (var s = BuildMenu(new List<FileItem>(), true, false)) {
            Check("background menu built", s != null);
            if (s != null) {
                var all = s.Menu.Enumerate(true);
                foreach (var m in all) T($"  bg item: id={m.id} verb='{m.verb}' text='{m.text}' depth={m.depth}");
                T("INFO background menu verbs: " + string.Join(",", all.Select(m => m.verb).Where(v => v.Length > 0)));
                Check("background menu has 'paste' (disabled/absent with an empty clipboard) or 'new'", all.Any(m => m.verb.Equals("paste", StringComparison.OrdinalIgnoreCase) || m.verb.Equals("NewFolder", StringComparison.OrdinalIgnoreCase) || m.verb.Equals("New", StringComparison.OrdinalIgnoreCase) || m.verb.Equals("properties", StringComparison.OrdinalIgnoreCase)));
                Check("background menu has a populated 'New' submenu", all.Any(m => m.depth >= 1), $"{all.Count(m => m.depth >= 1)} sub items");
                var nf = all.FirstOrDefault(m => m.verb.Equals("newfolder", StringComparison.OrdinalIgnoreCase));
                if (nf.id != 0) {
                    await ExecuteMenuCommand(s, nf.id, default, false); await Task.Delay(400);
                    Check("intercepted 'NewFolder' creates the folder through FileOps + inline rename", Directory.Exists(P("Nouveau dossier")) && renaming != null && DiskMatches());
                    CancelRename();
                    var del = await FileOps.Delete(Hwnd, new[] { P("Nouveau dossier") }, true, WatchSet()); ApplyResult(del, false);
                    Check("test folder removed (permanent, no Recycle Bin)", !Directory.Exists(P("Nouveau dossier")));
                } else T("INFO no 'newfolder' verb at depth 0/1 (New items are invoked natively then the list refreshes)");
            }
        }

        // 5. real-input drag (SendInput-like mouse_event): two selected files dragged onto the Third folder row through SHDoDragDrop + OLE + our IDropTarget
        await DragByMouse(new[] { "file009.txt", "file010.txt" }, "Third", P);
        await Task.Delay(500);

        // 6. leak cycles: 20 menu build/destroy + 20 drag enter/leave
        void Cycle() {
            foreach (var s in new[] { ShellItems.Of(new[] { P("ctxtest.txt") }), ShellItems.OfFolder(F), ShellItems.Of(new[] { P("Cible") }) }) {
                if (s == null) continue;
                using (s) using (var m = ShellMenu.Create(Hwnd, s, false)) m?.Enumerate(true);
            }
            using var si = ShellItems.Of(new[] { P("file011.txt") });
            var data = si!.ManagedDataObject(Hwnd)!;
            int e = 3; target.DragEnter(data, 0, RowPt(It("Cible")), ref e); target.DragLeave();
            Marshal.FinalReleaseComObject(data);
        }
        for (int i = 0; i < 3; i++) Cycle();
        await Task.Delay(3000);
        var s0 = Snap();
        T($"MEASURE before cycles: handles {s0.handles}, threads {s0.threads}, gdi {s0.gdi}, user {s0.user}, private {s0.mb:F1} MB");
        for (int i = 0; i < 20; i++) { Cycle(); await Task.Delay(30); }
        await Task.Delay(SettleMs);
        var s1 = Snap();
        T($"MEASURE after 20 cycles + 30 s: handles {s1.handles}, threads {s1.threads}, gdi {s1.gdi}, user {s1.user}, private {s1.mb:F1} MB");
        Check("no handle/GDI/USER growth (threads reported: shell extensions keep ~1 thread per QueryContextMenu, see LEAK diag) after menu + drag cycles", s1.handles - s0.handles <= 10 &&  (int)s1.gdi - (int)s0.gdi <= 5 && (int)s1.user - (int)s0.user <= 5,
            $"handles {s1.handles - s0.handles:+#;-#;0}, threads {s1.threads - s0.threads:+#;-#;0}, gdi {(int)s1.gdi - (int)s0.gdi:+#;-#;0}, user {(int)s1.user - (int)s0.user:+#;-#;0}");
        Check("list still consistent", DiskMatches() && OrderOk());

        // 7. screenshots: drop highlight (programmatic drag-over: the OLE drag image itself needs a real drag), then the real native menu through TrackPopupMenuEx
        Activate(); list.Select(new[] { It("ctxtest.txt") });
        using (var si = ShellItems.Of(new[] { P("file011.txt") })) {
            var data = si!.ManagedDataObject(Hwnd)!;
            int e = 3; target.DragEnter(data, 0, RowPt(It("Autre")), ref e);
            await Task.Delay(500);
            using var shot = StartShot(Path.Combine(shotDir, "files-step4-drop.png"), 300, false);
            if (shot != null) await shot.WaitForExitAsync();
            target.DragLeave();
        }
        T("drop screenshot done");
        using (var s = BuildMenu(Selection(), false, false)) {
            var anchor = RowPt(It("ctxtest.txt"));
            var safety = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
            safety.Tick += (_, _) => { safety.Stop(); SendMessageW(Hwnd, 0x1F /* WM_CANCELMODE */, IntPtr.Zero, IntPtr.Zero); };
            safety.Start();
            using var shot = StartShot(Path.Combine(shotDir, "files-step4-menu.png"), 2500, true);
            uint cmd = s!.Menu.Track(Hwnd, anchor.X - 200, anchor.Y);
            safety.Stop();
            if (shot != null) await shot.WaitForExitAsync();
            Check("native menu shown with TrackPopupMenuEx and dismissed (Esc)", cmd == 0, $"cmd {cmd}");
        }

        await Task.Delay(8000);
        var cw = Snap(false); await Task.Delay(10000); var cx = Snap(false);
        T($"MEASURE idle CPU window 1 (8-18 s after last action): {cx.cpuMs - cw.cpuMs:F0} ms");
        var c0 = Snap(false); await Task.Delay(10000); var c1 = Snap(false);
        T($"MEASURE idle CPU over 10 s: {c1.cpuMs - c0.cpuMs:F0} ms");
        Check("idle CPU ~0", c1.cpuMs - c0.cpuMs < 100, $"{c1.cpuMs - c0.cpuMs:F0} ms / 10 s");
        T($"DONE fails={testFails}");
        Close();
    }

    // Diagnostic: which kind of cycle (item menu, folder menu, background menu, drag enter/leave) grows the thread count, and does it come back.
    async Task CtxLeak() {
        string F = Path.GetFullPath(Active.Location);
        if (!F.StartsWith(Path.Combine(Path.GetTempPath(), "WinlestiaFilesOps"), StringComparison.OrdinalIgnoreCase)) { Close(); return; }
        await WaitLoaded(); await Task.Delay(800);
        string P(string n) => Path.Combine(F, n);
        var target = (IDropTarget)dropTarget!;
        var kinds = new Dictionary<string, Action> {
            ["item"] = () => { using var s = ShellItems.Of(new[] { P("ctxtest.txt") }); using var m = ShellMenu.Create(Hwnd, s!, false); m?.Enumerate(true); },
            ["folder"] = () => { using var s = ShellItems.Of(new[] { P("Cible") }); using var m = ShellMenu.Create(Hwnd, s!, false); m?.Enumerate(true); },
            ["background"] = () => { using var s = ShellItems.OfFolder(F); using var m = ShellMenu.Create(Hwnd, s!, false); m?.Enumerate(true); },
            ["background-nopopulate"] = () => { using var s = ShellItems.OfFolder(F); using var m = ShellMenu.Create(Hwnd, s!, false); m?.Enumerate(false); },
            ["dataobject-only"] = () => { using var s = ShellItems.Of(new[] { P("file011.txt") }); var d = s!.ManagedDataObject(Hwnd)!; Marshal.FinalReleaseComObject(d); },
            ["dragenter"] = () => { using var s = ShellItems.Of(new[] { P("file011.txt") }); var d = s!.ManagedDataObject(Hwnd)!; int e = 3; target.DragEnter(d, 0, RowPt(Active.Find(P("Cible"))!), ref e); target.DragLeave(); Marshal.FinalReleaseComObject(d); },
        };
        int nCycles = int.TryParse(Environment.GetEnvironmentVariable("WINLESTIA_FILES_CYCLES"), out var nc) ? nc : 20;
        var only = Environment.GetEnvironmentVariable("WINLESTIA_FILES_VARIANTS");
        foreach (var (name, cycle) in kinds) {
            if (!string.IsNullOrEmpty(only) && !only.Split(',').Contains(name)) continue;
            for (int i = 0; i < 3; i++) cycle();
            await Task.Delay(2000);
            var a = Snap();
            for (int i = 0; i < nCycles; i++) { cycle(); await Task.Delay(30); }
            var b = Snap();
            await Task.Delay(25000);
            var c = Snap();
            T($"LEAK {name}: threads {a.threads} -> {b.threads} (after 20) -> {c.threads} (+25 s); handles {a.handles} -> {b.handles} -> {c.handles}; gdi {a.gdi}->{c.gdi}; user {a.user}->{c.user}; private MB {a.mb:F1}->{b.mb:F1}->{c.mb:F1}");
        }
        T("DONE leak diag");
        Close();
    }

    async Task DragByMouse(string[] names, string folder, Func<string, string> P) {
        Active.Go(Active.Location); await WaitLoaded(); await Task.Delay(300);
        SelectNames(names); list.Focus();
        var from = RowPt(Active.Find(P(names[0]))!); var to = RowPt(Active.Find(P(folder))!);
        Activate();
        var move = Task.Run(() => {
            SetCursorPos(from.X, from.Y); Thread.Sleep(250);
            mouse_event(0x2, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(250);
            for (int i = 1; i <= 24; i++) { SetCursorPos(from.X + (to.X - from.X) * i / 24, from.Y + (to.Y - from.Y) * i / 24); Thread.Sleep(35); }
            Thread.Sleep(500);
            mouse_event(0x4, 0, 0, 0, UIntPtr.Zero);
        });
        for (int i = 0; i < 150 && !(names.All(n => Directory.Exists(P(folder)) && File.Exists(Path.Combine(P(folder), n)))); i++) await Task.Delay(100);
        await move; await Task.Delay(600); try { await LastDrop; } catch { }
        Check("real mouse drag of a 2-file multi-selection onto a folder row (SHDoDragDrop -> OLE -> our IDropTarget) moves both", names.All(n => File.Exists(Path.Combine(P(folder), n)) && !File.Exists(P(n))) && names.All(n => Active.Find(P(n)) == null) && DiskMatches(),
            $"in folder: {names.Count(n => File.Exists(Path.Combine(P(folder), n)))}, left behind: {names.Count(n => File.Exists(P(n)))}, list still shows: {names.Count(n => Active.Find(P(n)) != null)}");
    }
}
