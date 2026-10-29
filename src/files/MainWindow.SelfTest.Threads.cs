// WINLESTIA_FILES_SELFTEST=ctxthreads : which phase of a shell context-menu build leaves OS threads behind, and who owns them (start address -> module+offset, thread description).
// Variants a (SHBindToParent) b (+GetUIObjectOf) c (+QueryContextMenu) d (+populate submenus), each on the UI STA and on an MTA pool thread, same folder vs fresh folder. Fixture under %TEMP%\WinlestiaFilesOps only.
// WINLESTIA_FILES_CYCLES (default 20), WINLESTIA_FILES_SETTLE (seconds per variant, default 8).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WinlestiaFiles;

public sealed partial class MainWindow {
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationThread(IntPtr h, int cls, out IntPtr info, int len, IntPtr ret);
    [DllImport("kernel32.dll")] static extern int GetThreadDescription(IntPtr h, out IntPtr desc);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

    [DllImport("shell32.dll")] static extern int SHSetInstanceExplorer(IntPtr unk);
    [DllImport("ole32.dll")] static extern int CoFreeUnusedLibrariesEx(uint reserved, uint zero);
    [DllImport("ole32.dll")] static extern int CoFreeUnusedLibraries();
    static Dictionary<int, string> ThreadOwners() {
        var map = new Dictionary<int, string>();
        using var p = Process.GetCurrentProcess();
        var mods = new List<(long b, long e, string n)>();
        try { foreach (ProcessModule m in p.Modules) mods.Add((m.BaseAddress.ToInt64(), m.BaseAddress.ToInt64() + m.ModuleMemorySize, m.ModuleName)); } catch { }
        foreach (ProcessThread t in p.Threads) {
            string s = "?";
            var h = OpenThread(0x0040 | 0x0800, false, (uint)t.Id);
            if (h != IntPtr.Zero) {
                try {
                    string where = "?";
                    if (NtQueryInformationThread(h, 9, out var a, IntPtr.Size, IntPtr.Zero) == 0) {
                        long v = a.ToInt64(); var m = mods.FirstOrDefault(x => v >= x.b && v < x.e);
                        where = m.n != null ? $"{m.n}+0x{v - m.b:X}" : $"0x{v:X}(no module: jit/unknown)";
                    }
                    string d = "";
                    if (GetThreadDescription(h, out var dp) >= 0 && dp != IntPtr.Zero) { d = Marshal.PtrToStringUni(dp) ?? ""; LocalFree(dp); }
                    string st = ""; try { st = t.ThreadState + (t.ThreadState == System.Diagnostics.ThreadState.Wait ? "/" + t.WaitReason : ""); } catch { }
                    s = $"{where} desc='{d}' {st}";
                } finally { CloseHandle(h); }
            }
            map[t.Id] = s;
        }
        return map;
    }

    async Task CtxThreads() {
        string F = Path.GetFullPath(Active.Location);
        if (!F.StartsWith(Path.Combine(Path.GetTempPath(), "WinlestiaFilesOps"), StringComparison.OrdinalIgnoreCase)) { Close(); return; }
        await WaitLoaded(); await Task.Delay(800);
        int cycles = int.TryParse(Environment.GetEnvironmentVariable("WINLESTIA_FILES_CYCLES"), out var cy) ? cy : 20;
        int settle = int.TryParse(Environment.GetEnvironmentVariable("WINLESTIA_FILES_SETTLE"), out var se) ? se : 8;
        string only = Environment.GetEnvironmentVariable("WINLESTIA_FILES_VARIANTS") ?? "";
        int serial = 0;
        string Target(bool fresh) {
            if (!fresh) return Path.Combine(F, "ctxtest.txt");
            string d = Path.Combine(F, "fresh" + (++serial)); Directory.CreateDirectory(d);
            string f = Path.Combine(d, "x.txt"); File.WriteAllText(f, "x"); return f;
        }
        Action<string> Cycle(char v) => path => {
            using var s = ShellItems.Of(new[] { path }); if (s == null) return;
            if (v == 'a') return;
            if (v == 'b') { var p = s.Ui(ShellItems.IidContextMenu, Hwnd); if (p != IntPtr.Zero) Marshal.Release(p); return; }
            using var m = ShellMenu.Create(Hwnd, s, false);
            if (v == 'd') m?.Enumerate(true);
            if (v == 'e') { m?.Dispose(); CoFreeUnusedLibrariesEx(0, 0); }
        };
        IntPtr instRef = IntPtr.Zero;
        if (Environment.GetEnvironmentVariable("WINLESTIA_FILES_INSTREF") == "1") { instRef = Marshal.GetIUnknownForObject(new object()); T("SHSetInstanceExplorer hr=" + SHSetInstanceExplorer(instRef).ToString("X")); }
        var baseline = ThreadOwners();
        T($"CTXTHREADS start threads={baseline.Count} cycles={cycles} settle={settle}s");
        foreach (bool mta in new[] { false, true })
        foreach (bool fresh in new[] { false, true })
        foreach (char v in (Environment.GetEnvironmentVariable("WINLESTIA_FILES_LETTERS") ?? "abcd")) {
            if (mta && Environment.GetEnvironmentVariable("WINLESTIA_FILES_NOMTA") == "1") continue;
            string name = $"{v}-{(mta ? "MTA" : "STA")}-{(fresh ? "fresh" : "same")}";
            if (only.Length > 0 && !only.Split(',').Contains(name)) continue;
            var before = ThreadOwners(); var cyc = Cycle(v);
            for (int i = 0; i < cycles; i++) {
                string path = Target(fresh);
                if (mta) await Task.Run(() => cyc(path)); else cyc(path);
                await Task.Delay(30);
            }
            var after = ThreadOwners();
            await Task.Delay(settle * 1000);
            var settled = ThreadOwners();
            T($"VARIANT {name}: threads {before.Count} -> {after.Count} (after cycles) -> {settled.Count} (+{settle} s)");
            foreach (var g in settled.Where(k => !before.ContainsKey(k.Key)).GroupBy(k => k.Value).OrderByDescending(g => g.Count()))
                T($"   new x{g.Count()}: {g.Key}");
        }
        var end = ThreadOwners();
        T($"CTXTHREADS end threads={end.Count}; vs baseline new: {end.Count(k => !baseline.ContainsKey(k.Key))}");
        foreach (var g in end.Where(k => !baseline.ContainsKey(k.Key)).GroupBy(k => k.Value).OrderByDescending(g => g.Count())) T($"   total new x{g.Count()}: {g.Key}");
        T("   all-thread owners at end:");
        foreach (var g in end.GroupBy(k => k.Value.Split(" desc=")[0]).OrderByDescending(g => g.Count()).Take(15)) T($"   all x{g.Count()}: {g.Key}");
        T("DONE ctxthreads");
        Close();
    }
}
