// WINLESTIA_FILES_SELFTEST=recycle (resident --background instance, opened once through the pipe on a fixture under %TEMP%\WinlestiaFilesOps):
// builds WINLESTIA_FILES_RECYCLE_BUILDS native menus (item + background), optionally (WINLESTIA_FILES_RECYCLE_OP=1) starts a copy of <fixture>\bigfile.bin
// into <fixture>\dest, then hides the window like a user closing it. Marks go to selftest.log; the harness checks the exit code and the pipe.
using System;
using System.IO;
using System.Threading.Tasks;

namespace WinlestiaFiles;

public sealed partial class MainWindow {
    async Task RecycleTest() {
        string F = Path.GetFullPath(Active.Location);
        if (!F.StartsWith(Path.Combine(Path.GetTempPath(), "WinlestiaFilesOps"), StringComparison.OrdinalIgnoreCase)) { T("ABORT fixture outside %TEMP%/WinlestiaFilesOps: " + F); return; }
        FileOps.Quiet = true;
        await WaitLoaded();
        int n = int.TryParse(Environment.GetEnvironmentVariable("WINLESTIA_FILES_RECYCLE_BUILDS"), out var b) ? b : 0;
        for (int i = 0; i < n; i++) {
            using var s = i % 2 == 0 ? ShellItems.OfFolder(F) : ShellItems.Of(new[] { Path.Combine(F, "bigfile.bin") });
            using var m = s == null ? null : ShellMenu.Create(Hwnd, s, false);
        }
        T($"RECYCLE builds={ShellMenu.Builds} threshold={App.RecycleMenus} pid={Environment.ProcessId}");
        if (Environment.GetEnvironmentVariable("WINLESTIA_FILES_RECYCLE_OP") == "1") {
            Directory.CreateDirectory(Path.Combine(F, "dest"));
            var op = FileOps.Copy(Hwnd, new[] { Path.Combine(F, "bigfile.bin") }, Path.Combine(F, "dest"), false, WatchSet());
            _ = op.ContinueWith(_ => T("RECYCLE opdone"));
        }
        T($"RECYCLE hide active={FileOps.Active}");
        Close();
    }
}
