using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace WinlestiaFiles;

public partial class App : Application {
    // Resident = hide instead of exit when the last tab closes. Set by --background (preloaded, starts hidden) or --resident.
    internal static bool Resident, Quitting, Recycling;
    // Recycling: a resident that has built more than RecycleMenus native context menus (each leaks a suspended shell thread, see ShellMenu.Builds)
    // exits with RecycleExitCode once hidden and idle; the backend relaunches it. WINLESTIA_FILES_RECYCLE_MENUS overrides the threshold (tests).
    internal const int RecycleExitCode = 75;
    internal static readonly int RecycleMenus = int.TryParse(Environment.GetEnvironmentVariable("WINLESTIA_FILES_RECYCLE_MENUS"), out var rm) && rm >= 0 ? rm : 200;
    internal static int ProcessExitCode;
    SingleInstance? server;

    internal static bool IsFlag(string a) => a.Equals("--background", StringComparison.OrdinalIgnoreCase) || a.Equals("--resident", StringComparison.OrdinalIgnoreCase);

    protected override void OnStartup(StartupEventArgs e) {
        Perf.Mark("OnStartup");
        base.OnStartup(e);
        bool background = e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
        Resident = background || e.Args.Any(a => a.Equals("--resident", StringComparison.OrdinalIgnoreCase));
        string arg = string.Join(" ", e.Args.Where(a => !IsFlag(a)));
        try {
            if (background) {
                var hidden = new MainWindow(null);
                MainWindow = hidden;
                server = SingleInstance.TryStart(OnRequest);
                if (server == null) { Shutdown(0); return; }     // another instance already owns the pipe
                hidden.Preload();
                Perf.Mark("preloaded (hidden)");
                return;
            }
            var route = PathRouter.Route(arg);
            if (!route.IsOwn) { PathRouter.LaunchExplorer(route.Value); Shutdown(0); return; }
            var window = new MainWindow(route.Value);
            MainWindow = window;
            Perf.Mark("ctor done, Show()");
            window.Show();
            Perf.Mark("Show() returned");
            // After the first frame's work: the first instance with a window also hosts the server, later launches forward to it.
            Dispatcher.BeginInvoke(() => { server = SingleInstance.TryStart(OnRequest); }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        } catch (Exception ex) {
            CrashLog(ex);
            if (Winlestia.Shared.FilesHandlerGuard.CountsAsCrash(ex)) RecordStartupCrash();      // access denied / missing path: explorer fallback below, not a crash
            try { PathRouter.LaunchExplorer(arg); } catch (Exception inner) { CrashLog(inner); }
            Shutdown(1);
        }
    }

    // UI thread, window hidden and idle. The pipe is closed FIRST (no new client can connect; one that is mid-handshake fails and falls back to a cold launch),
    // then the shutdown is queued behind anything already posted. If a request still got a window in meanwhile, this instance simply becomes an ordinary one.
    internal void BeginRecycle() {
        if (Recycling) return;
        Recycling = true;
        server?.Dispose(); server = null;
        Dispatcher.BeginInvoke(() => {
            if (MainWindow is { IsVisible: true }) { Resident = false; Recycling = false; return; }
            Quitting = true; ProcessExitCode = RecycleExitCode; Shutdown(RecycleExitCode);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    // Runs on a pool thread (pipe handler). Requests do not touch the UI directly: they go through a bounded, coalescing queue served by ONE consumer that
    // waits for each window change to finish, so a flood of clients (or a stuck UI) can never pile up unbounded dispatcher work or processes.
    const int MaxPending = 20;
    readonly object gate = new();
    readonly System.Collections.Generic.List<string> pending = new();
    bool draining, handedOff;
    int dropped; long lastDropLog;

    void OnRequest(SingleInstance.Request r) {
        try {
            if (r.Cmd == "quit") { Dispatcher.BeginInvoke(() => { Quitting = true; server?.Dispose(); Shutdown(0); }); return; }
            if (r.Cmd == "palette") { PaletteLoader.Refresh(Dispatcher, () => (MainWindow as MainWindow)?.RepaintChrome()); return; }   // backend: palette / wallpaper / light-dark changed
            if (r.Cmd == "ping" || r.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase))) return;
            Perf.Log("pipe request received");
            if (Recycling) {   // accepted a moment before the pipe closed: hand it to a fresh process rather than lose it (at most ONE per recycle, never a spawn loop)
                lock (gate) { if (handedOff) { dropped++; return; } handedOff = true; }
                var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = Path.IsPathRooted(r.Cwd) ? r.Cwd : Environment.CurrentDirectory };
                foreach (var a in r.Args) psi.ArgumentList.Add(a);
                System.Diagnostics.Process.Start(psi)?.Dispose();
                return;
            }
            string arg = string.Join(" ", r.Args.Where(a => !IsFlag(a)));
            string v = arg.Trim().Trim('"').Trim();
            if (v.Length > 0 && !Path.IsPathRooted(v) && Path.IsPathRooted(r.Cwd) && !v.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !v.StartsWith("::{", StringComparison.Ordinal))
                arg = Path.GetFullPath(v, r.Cwd);
            Enqueue(arg);
        } catch (Exception ex) { CrashLog(ex); }
    }

    void Enqueue(string arg) {
        lock (gate) {
            if (pending.Exists(p => string.Equals(p, arg, StringComparison.OrdinalIgnoreCase))) return;      // same path already waiting: coalesced
            if (pending.Count >= MaxPending) {
                dropped++;
                long now = Environment.TickCount64;
                if (now - lastDropLog > 5000) { LogLine($"pipe: queue full ({MaxPending} pending), {dropped} request(s) dropped"); dropped = 0; lastDropLog = now; }
                return;
            }
            pending.Add(arg);
            if (draining) return;
            draining = true;
        }
        _ = Task.Run(Drain);
    }

    async Task Drain() {
        while (true) {
            string arg;
            lock (gate) {
                if (pending.Count == 0 || Quitting) { pending.Clear(); draining = false; return; }
                arg = pending[0]; pending.RemoveAt(0);
            }
            try {
                var route = PathRouter.Route(arg);
                if (!route.IsOwn) { PathRouter.LaunchExplorer(route.Value); await Task.Delay(150).ConfigureAwait(false); continue; }
                await Dispatcher.InvokeAsync(() => {
                    try { (MainWindow as MainWindow)?.ShowAt(route.Value); } catch (Exception ex) { CrashLog(ex); }
                }, System.Windows.Threading.DispatcherPriority.Normal);
                await Task.Delay(5).ConfigureAwait(false);
            } catch (Exception ex) { CrashLog(ex); }
        }
    }

    // Startup crash counter (3 within 60 s: Files restores the folder handler itself and the backend turns the option off). Pure .NET, no WPF type.
    internal static void RecordStartupCrash() { try { Winlestia.Shared.FilesHandlerGuard.RecordStartupCrash(Winlestia.Shared.FilesHandler.DataDir, Winlestia.Shared.FilesHandlerGuard.NowMs()); } catch { } }

    // First 15 s of the process, unhandled exception: count it (real crashes only) and hand the requested folder to explorer.exe before dying. Best effort.
    internal static string RequestedArg = "";
    internal static void EarlyDeath(Exception? ex) {
        try {
            if (Program.Uptime.TotalSeconds >= 15) return;
            if (ex == null || Winlestia.Shared.FilesHandlerGuard.CountsAsCrash(ex)) RecordStartupCrash();
            if (RequestedArg.Length > 0) PathRouter.LaunchExplorer(RequestedArg);
        } catch { }
    }

    internal void StopServer() { try { server?.Dispose(); } catch { } server = null; }

    // One diagnostic line (same file and 200-line cap as CrashLog).
    internal static void LogLine(string text) => CrashLog(new InvalidOperationException(text));

    // Startup only, so synchronous is fine. Capped at 200 lines.
    static readonly object logGate = new();
    internal static void CrashLog(Exception ex) {
        try { lock (logGate) {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winlestia", "Files");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "crash.log");
            var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : new System.Collections.Generic.List<string>();
            lines.AddRange((DateTimeOffset.Now.ToString("O") + " " + (ex is InvalidOperationException && ex.StackTrace == null ? ex.Message : ex.ToString())).Split('\n').Select(l => l.TrimEnd('\r')));
            if (lines.Count > 200) lines = lines.Skip(lines.Count - 200).ToList();
            File.WriteAllLines(file, lines);
        } } catch { }
    }
}

// Own entry point (the generated Main is App.Main). No parallel WPF warm-up thread: first-use WPF init on two threads deadlocks inside WPF
// (thread A in ContentPresenter..cctor waits for WpfSharedBamlSchemaContext's lock, thread B holds that lock and waits for the same type initializer).
// It saved ~80 ms on a cold start and cost an occasional permanent hang, so all WPF work stays on the one UI thread.
// A resident instance is tried first, before any WPF type is touched: forward over the pipe and leave (<150 ms worst case).
internal static class Program {
    static readonly long born = System.Diagnostics.Stopwatch.GetTimestamp();
    internal static TimeSpan Uptime => System.Diagnostics.Stopwatch.GetElapsedTime(born);
    [STAThread]
    public static int Main(string[] args) {
        // Command line only (installer, uninstaller, tests): no window, no pipe, no WPF. --register "<exe>" | --restore
        if (args.Length > 0 && (args[0].Equals("--register", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--restore", StringComparison.OrdinalIgnoreCase))) return Winlestia.Shared.FilesHandler.RunCli(args);
        App.RequestedArg = string.Join(" ", args.Where(a => !App.IsFlag(a)));
        AppDomain.CurrentDomain.UnhandledException += (_, e) => App.EarlyDeath(e.ExceptionObject as Exception);
        if (Forwarded(args)) return 0;
        return Run();
    }

    static bool Forwarded(string[] args) {
        try { return Winlestia.Shared.FilesPipe.TrySend(Winlestia.Shared.FilesPipe.Build(args, Environment.CurrentDirectory, null)); }
        catch { return false; }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run() {
        App.Main();
        return App.ProcessExitCode;
    }
}