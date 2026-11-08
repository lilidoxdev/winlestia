// Sur option (Preferences.FilesPreload, false par défaut) : un lancement différé, à basse priorité, d'un Winlestia.Files.exe
// --background résident et caché.
// Pas de boucle : si le processus meurt, il est relancé au plus une fois par session du backend. Code de sortie 75 = recyclage volontaire
// (fuite du thread de menu) : relancé après DelayMs, sans limite mais au plus une fois par minute. À la sortie, un « quit » passe par
// le canal, rien n'est tué.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Winlestia.Shared;

namespace PanneauxGlass;

internal static class FilesPreload
{
    // Évite une boucle de relance serrée si le processus plante dès le démarrage.
    const int CrashRelaunchDelayMs = 5000;
    // Durée de construction de la fenêtre cachée du résident avant de repasser en priorité normale.
    const int NormalPriorityDelayMs = 8000;
    const int DelayMs = 4000;
    static readonly FilesPreloadPolicy policy = new();
    static Timer? recycleTimer, paletteTimer;
    static volatile bool quitting;
    static Process? child;

    internal static void Start()
    {
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DelayMs).ConfigureAwait(false);
                Launch();
            }
            catch (Exception ex) { Store.Log("FilesPreload: " + ex.Message); }
        });
    }

    static void Launch(bool recycle = false)
    {
        if (!policy.MayLaunch(recycle, Store.State.FilesPreload, quitting))
            return;
        if (FilesPipe.TrySend(FilesPipe.Build(Array.Empty<string>(), "", "ping"), 150))
            return;   // already resident
        try
        {
            if (child != null && !child.HasExited)
                return;
        }
        catch (Exception err) when (err is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Store.Log("FilesPreload.Launch : " + err.GetType().Name + ": " + err.Message);
        }   // jamais deux résidents à la fois, quel que soit le chemin qui nous a menés ici
        string exe = Path.Combine(AppContext.BaseDirectory, "Winlestia.Files.exe");
        if (!File.Exists(exe))
            return;
        if (!recycle)
            policy.Starts++;
        var p = Process.Start(new ProcessStartInfo(exe, "--background") { UseShellExecute = false, CreateNoWindow = true });
        if (p == null)
            return;
        child = p;
        Store.Log("Files préchargé · t+" + (int)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds + " ms");
        try
        {
            p.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception err) when (err is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Store.Log("FilesPreload.Launch : " + err.GetType().Name + ": " + err.Message);
        }
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => OnExited(p);
        // BelowNormal seulement pendant qu'il construit sa fenêtre cachée ; le résident repasse ensuite en Normal pour qu'une ouverture
        // ultérieure ne soit pas affamée.
        Task.Run(async () => { await Task.Delay(NormalPriorityDelayMs).ConfigureAwait(false); try {
            if (!p.HasExited) p.PriorityClass = ProcessPriorityClass.Normal; } catch (InvalidOperationException) {
            /* Processus déjà terminé : plus de priorité à rétablir. */ } });
    }

    static void OnExited(Process p)
    {
        int code = -1;
        try
        {
            code = p.ExitCode;
        }
        catch (Exception err) when (err is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Store.Log("FilesPreload.OnExited : " + err.GetType().Name + ": " + err.Message);
        }
        switch (policy.OnExit(code, Environment.TickCount64, quitting, Store.State.FilesPreload))
        {
            case PreloadAction.Crash:
                Task.Run(async () => { await Task.Delay(CrashRelaunchDelayMs).ConfigureAwait(false); try { Launch(); }
                    catch (Exception err) { Store.Log("FilesPreload.OnExited : " + err.GetType().Name + ": " + err.Message); } });
                break;
            case PreloadAction.Recycle:
                Store.Log("FilesPreload: resident recycled (exit 75), relaunch in " + DelayMs / 1000 + " s");
                lock (policy)
                {
                    recycleTimer?.Dispose();
                    recycleTimer = new Timer(_ => { try { Launch(true); } catch (Exception ex) { Store.Log("FilesPreload: "
                        + ex.Message); }
                        }, null, DelayMs, Timeout.Infinite);
                }
                break;
            case PreloadAction.StopTooFast:
                Store.Log("FilesPreload: resident recycled twice within a minute, no relaunch");
                break;
        }
    }

    // Palette / fond d'écran / clair-sombre modifiés (Theme.Use) : demande à un Winlestia Fichiers en cours (résident ou fenêtré) de
    // relire les préférences. N'en lance jamais un.
    // Anti-rebond de 600 ms (le fichier de préférences est sauvé juste après le changement de thème) ; une sonde du canal sur le pool
    // de threads, sans sondage périodique.
    internal static void NotifyPalette()
    {
        if (quitting)
            return;
        lock (policy)
        {
            paletteTimer?.Dispose();
            paletteTimer = new Timer(_ => { try { FilesPipe.TrySend(FilesPipe.Build(Array.Empty<string>(), "", "palette"), 150); }
                catch (Exception err) { Store.Log("FilesPreload.NotifyPalette : " + err.GetType().Name + ": " + err.Message); } }, null,
                600, Timeout.Infinite);
        }
    }

    internal static void Quit()
    {
        quitting = true;
        lock (policy)
        {
            paletteTimer?.Dispose();
            paletteTimer = null;
        }
        lock (policy)
        {
            recycleTimer?.Dispose();
            recycleTimer = null;
        }
        try
        {
            if (child != null)
                FilesPipe.TrySend(FilesPipe.Build(Array.Empty<string>(), "", "quit"), 150);
        }
        catch (Exception err)
        {
            Store.Log("FilesPreload.Quit : " + err.GetType().Name + ": " + err.Message);
        }
    }
}
