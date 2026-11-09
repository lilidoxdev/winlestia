using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace WinlestiaFiles;

public partial class App : Application
{
    // Cède le tour au thread UI entre deux routes en file pour que l'ouverture reste fluide.
    const int RouteYieldMs = 5;

    // Espace les lancements d'Explorateur pour conserver l'ordre d'ouverture des fenêtres.
    const int LaunchSpacingMs = 150;
    // Résident = se cache au lieu de quitter quand le dernier onglet se ferme. Posé par --background (préchargé, démarre caché) ou
    // --resident.
    internal static bool Resident, Quitting, Recycling;
    // Recyclage : un résident qui a construit plus de RecycleMenus menus contextuels natifs (chacun fuit un thread de shell suspendu,
    // voir ShellMenu.Builds) quitte avec RecycleExitCode une fois caché et inactif ; le backend le relance. WINLESTIA_FILES_RECYCLE_MENUS
    // surcharge le seuil (tests).
    internal const int RecycleExitCode = 75;
    internal static readonly int RecycleMenus = int.TryParse(Environment.GetEnvironmentVariable("WINLESTIA_FILES_RECYCLE_MENUS"),
        out var rm) && rm >= 0 ? rm : 200;
    internal static int ProcessExitCode;
    SingleInstance? server;

    internal static bool IsFlag(string a) => a.Equals("--background", StringComparison.OrdinalIgnoreCase) || a.Equals("--resident",
        StringComparison.OrdinalIgnoreCase);

    protected override void OnStartup(StartupEventArgs e)
    {
        Perf.Mark("OnStartup");
        base.OnStartup(e);
        bool background = e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
        Resident = background || e.Args.Any(a => a.Equals("--resident", StringComparison.OrdinalIgnoreCase));
        string arg = string.Join(" ", e.Args.Where(a => !IsFlag(a)));
        try
        {
            if (background)
            {
                var hidden = new MainWindow(null);
                MainWindow = hidden;
                server = SingleInstance.TryStart(OnRequest);
                if (server == null)
                {
                    Shutdown(0);
                    return;
                }     // another instance already owns the pipe
                hidden.Preload();
                Perf.Mark("preloaded (hidden)");
                return;
            }
            var route = PathRouter.Route(arg);
            if (!route.IsOwn)
            {
                PathRouter.LaunchExplorer(route.Value);
                Shutdown(0);
                return;
            }
            var window = new MainWindow(route.Value);
            MainWindow = window;
            Perf.Mark("ctor done, Show()");
            window.Show();
            Perf.Mark("Show() returned");
            // Après le travail de la première image : la première instance avec une fenêtre héberge aussi le serveur, les lancements
            // suivants lui transmettent leur demande.
            Dispatcher.BeginInvoke(() => { server = SingleInstance.TryStart(OnRequest); },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex)
        {
            CrashLog(ex);
            if (Winlestia.Shared.FilesHandlerGuard.CountsAsCrash(ex))
                RecordStartupCrash();      // access denied / missing path: explorer fallback below, not a crash
            try
            {
                PathRouter.LaunchExplorer(arg);
            }
            catch (Exception inner) { CrashLog(inner); }
            Shutdown(1);
        }
    }

    internal void ReleasePipe()
    {
        server?.Dispose();
        server = null;
    }   // instance mourante : aucun nouveau client ne doit se connecter avant le démarrage du processus de remplacement

    // Thread UI, fenêtre cachée et inactive. Le canal est fermé EN PREMIER (aucun nouveau client ne peut se connecter ; celui qui est
    // en pleine négociation échoue et retombe sur un lancement à froid),
    // puis l'arrêt est mis en file derrière tout ce qui est déjà posté. Si une demande a quand même obtenu une fenêtre entre-temps,
    // cette instance devient simplement une instance ordinaire.
    internal void BeginRecycle()
    {
        if (Recycling)
            return;
        Recycling = true;
        server?.Dispose();
        server = null;
        Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is { IsVisible: true })
            {
                Resident = false;
                Recycling = false;
                return;
            }
            Quitting = true;
            ProcessExitCode = RecycleExitCode;
            Shutdown(RecycleExitCode);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    // S'exécute sur un thread du pool (gestionnaire du canal). Les demandes ne touchent pas directement l'UI : elles passent par une file
    // bornée et fusionnante servie par UN SEUL consommateur qui
    // attend la fin de chaque changement de fenêtre, pour qu'un déluge de clients (ou une UI bloquée) ne puisse jamais empiler un
    // travail illimité de dispatcher ou de processus.
    const int MaxPending = 20;
    readonly object gate = new();
    readonly System.Collections.Generic.List<string> pending = new();
    bool draining, handedOff;
    int dropped; long lastDropLog;

    void OnRequest(SingleInstance.Request r)
    {
        try
        {
            if (r.Cmd == "quit")
            {
                Dispatcher.BeginInvoke(() => { Quitting = true; server?.Dispose(); Shutdown(0); });
                return;
            }
            if (r.Cmd == "palette")
            {
                PaletteLoader.Refresh(Dispatcher, () => (MainWindow as MainWindow)?.RepaintChrome());
                return;
            }   // backend: palette / wallpaper / light-dark changed
            if (r.Cmd == "ping" || r.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase)))
                return;
            Perf.Log("pipe request received");
            if (Recycling)
            // accepté un instant avant la fermeture du tube : le confier à un nouveau processus plutôt que le perdre (au plus UN par
            // recyclage, jamais de boucle de lancements)
            {
                lock (gate)
                {
                    if (handedOff)
                    {
                        dropped++;
                        return;
                    }
                    handedOff = true;
                }
                var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.IsPathRooted(r.Cwd) ? r.Cwd : Environment.CurrentDirectory
                };
                foreach (var a in r.Args)
                    psi.ArgumentList.Add(a);
                System.Diagnostics.Process.Start(psi)?.Dispose();
                return;
            }
            string arg = string.Join(" ", r.Args.Where(a => !IsFlag(a)));
            string v = arg.Trim().Trim('"').Trim();
            if (v.Length > 0 && !Path.IsPathRooted(v) && Path.IsPathRooted(r.Cwd) && !v.StartsWith("shell:",
                StringComparison.OrdinalIgnoreCase) && !v.StartsWith("::{", StringComparison.Ordinal))
                arg = Path.GetFullPath(v, r.Cwd);
            Enqueue(arg);
        }
        catch (Exception ex) { CrashLog(ex); }
    }

    void Enqueue(string arg)
    {
        lock (gate)
        {
            if (pending.Exists(p => string.Equals(p, arg, StringComparison.OrdinalIgnoreCase)))
                return;      // same path already waiting: coalesced
            if (pending.Count >= MaxPending)
            {
                dropped++;
                long now = Environment.TickCount64;
                if (now - lastDropLog > 5000)
                {
                    LogLine($"pipe: queue full ({MaxPending} pending), {dropped} request(s) dropped");
                    dropped = 0;
                    lastDropLog = now;
                }
                return;
            }
            pending.Add(arg);
            if (draining)
                return;
            draining = true;
        }
        _ = Task.Run(Drain);
    }

    async Task Drain()
    {
        while (true)
        {
            string arg;
            lock (gate)
            {
                if (pending.Count == 0 || Quitting)
                {
                    pending.Clear();
                    draining = false;
                    return;
                }
                arg = pending[0];
                pending.RemoveAt(0);
            }
            try
            {
                var route = PathRouter.Route(arg);
                if (!route.IsOwn)
                {
                    PathRouter.LaunchExplorer(route.Value);
                    await Task.Delay(LaunchSpacingMs).ConfigureAwait(false);
                    continue;
                }
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        (MainWindow as MainWindow)?.ShowAt(route.Value);
                    }
                    catch (Exception ex) { CrashLog(ex); }
                }, System.Windows.Threading.DispatcherPriority.Normal);
                await Task.Delay(RouteYieldMs).ConfigureAwait(false);
            }
            catch (Exception ex) { CrashLog(ex); }
        }
    }

    // Compteur de plantages au démarrage (3 en 60 s : Files restaure lui-même le gestionnaire de dossiers et le backend désactive
    // l'option). .NET pur, aucun type WPF.
    internal static void RecordStartupCrash()
    {
        try
        {
            Winlestia.Shared.FilesHandlerGuard.RecordStartupCrash(Winlestia.Shared.FilesHandler.DataDir,
                Winlestia.Shared.FilesHandlerGuard.NowMs());
        }
        catch (Exception err)
        {
            App.LogLine("App.RecordStartupCrash : " + err.GetType().Name + ": " + err.Message);
        }
    }

    // Pendant les 15 premières secondes du processus, exception non gérée : on la compte (vrais plantages seulement) et on confie le
    // dossier demandé à explorer.exe avant de mourir. Au mieux.
    internal static string RequestedArg = "";
    internal static void EarlyDeath(Exception? ex)
    {
        try
        {
            if (Program.Uptime.TotalSeconds >= 15)
                return;
            if (ex == null || Winlestia.Shared.FilesHandlerGuard.CountsAsCrash(ex))
                RecordStartupCrash();
            if (RequestedArg.Length > 0)
                PathRouter.LaunchExplorer(RequestedArg);
        }
        catch (Exception err)
        {
            App.LogLine("App.EarlyDeath : " + err.GetType().Name + ": " + err.Message);
        }
    }

    internal void StopServer()
    {
        Safe.Dispose(server);
        server = null;
    }

    // Une ligne de diagnostic (même fichier et même plafond de 200 lignes que CrashLog).
    internal static void LogLine(string text) => CrashLog(new InvalidOperationException(text));

    // Au démarrage seulement, donc le synchrone convient. Plafonné à 200 lignes.
    static readonly object logGate = new();
    internal static void CrashLog(Exception ex)
    {
        try
        {
            lock (logGate)
            {
                string dir = WinlestiaFiles.FilesSettings.Dir;
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "crash.log");
                var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : new System.Collections.Generic.List<string>();
                lines.AddRange((DateTimeOffset.Now.ToString("O") + " " + (ex is InvalidOperationException && ex.StackTrace == null
                    ? ex.Message : ex.ToString())).Split('\n').Select(l => l.TrimEnd('\r')));
                if (lines.Count > 200)
                    lines = lines.Skip(lines.Count - 200).ToList();
                File.WriteAllLines(file, lines);
            }
        }
        catch (Exception)
        {
            // Le journal ne peut pas se journaliser lui-même.
        }
    }
}

// Point d'entrée propre (le Main généré est App.Main). Pas de thread de préchauffage WPF parallèle : l'initialisation WPF au premier
// usage sur deux threads se bloque dans WPF
// (le thread A dans ContentPresenter..cctor attend le verrou de WpfSharedBamlSchemaContext, le thread B tient ce verrou et attend le même
// initialiseur de type). Il gagnait ~80 ms à froid et coûtait un blocage définitif occasionnel : tout le travail WPF reste donc sur
// l'unique thread UI. Une instance résidente est essayée d'abord, avant de toucher à un type WPF : transmission par le canal puis
// sortie (<150 ms au pire).
internal static class Program
{
    static readonly long born = System.Diagnostics.Stopwatch.GetTimestamp();
    internal static TimeSpan Uptime => System.Diagnostics.Stopwatch.GetElapsedTime(born);
    [System.Runtime.InteropServices.DllImport("shell32.dll",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    static extern int GetCurrentProcessExplicitAppUserModelID([System.Runtime
        .InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string appId);
    internal const string AppId = "Winlestia.Files";
    internal static string? CurrentAppId()
    {
        try
        {
            return GetCurrentProcessExplicitAppUserModelID(out var id) == 0 ? id : null;
        }
        catch { return null; }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);
        }
        catch (Exception)
        {
            // Identité de barre des tâches facultative.
        }
        // Ligne de commande seule (installeur, désinstalleur, tests) : ni fenêtre, ni tube, ni WPF. --register "<exe>" | --restore
        if (args.Length > 0 && (args[0].Equals("--register", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--restore",
            StringComparison.OrdinalIgnoreCase)))
            return Winlestia.Shared.FilesHandler.RunCli(args);
        App.RequestedArg = string.Join(" ", args.Where(a => !App.IsFlag(a)));
        AppDomain.CurrentDomain.UnhandledException += (_, e) => App.EarlyDeath(e.ExceptionObject as Exception);
        if (Forwarded(args))
            return 0;
        return Run();
    }

    static bool Forwarded(string[] args)
    {
        try
        {
            return Winlestia.Shared.FilesPipe.TrySend(Winlestia.Shared.FilesPipe.Build(args, Environment.CurrentDirectory, null));
        }
        catch { return false; }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run()
    {
        App.Main();
        return App.ProcessExitCode;
    }
}