// C# 12, fichier Program.cs, cible Windows 11 / .NET 8, assistant de capteurs Winlestia : modes run, install, uninstall
using System;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace WinlestiaSensors;

internal static class Program
{
    // Codes de sortie : 0 ok, 10 ok mais redémarrage requis, 20 téléchargement/signature, 21 pilote, 30 tâche/copie, 2 non élevé.
    internal const int NotElevated = 2;

    static int Main(string[] args)
    {
        string mode = args.FirstOrDefault()?.ToLowerInvariant() ?? "";
        if (mode is not ("run" or "install" or "uninstall"))
            return 1;
        if (!IsElevated())
            return NotElevated;
        try
        {
            return mode switch
            {
                "run" => Run(),
                "install" => Installer.Install(args.Skip(1).Contains("--driver", StringComparer.OrdinalIgnoreCase)),
                _ => Installer.Uninstall()
            };
        }
        catch (Exception ex)
        {
            Log.Write(mode + ": " + Log.Describe(ex));
            return mode == "run" ? 1 : 30;
        }
    }

    static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        // Le compte SYSTEM (tâche planifiée) est accepté explicitement.
        return identity.IsSystem || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    static int Run()
    {
        // Mutex protégé (SYSTEM et Administrateurs) : un utilisateur sans droits ne peut ni le créer d'avance ni le retenir.
        var security = new MutexSecurity();
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(sid, null), MutexRights.FullControl,
                AccessControlType.Allow));
        Mutex single;
        bool created;
        try { single = MutexAcl.Create(true, "Global\\Winlestia.Sensors.Single", out created, security); }
        catch (Exception ex)
        {
            Log.Write("Mutex inaccessible, arrêt: " + Log.Describe(ex));
            return 1;
        }
        using var _ = single;
        if (!created)
            return 0;
        using var server = new SensorServer();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => server.Dispose();
        Log.Write("Service démarré");
        server.Serve(CancellationToken.None).GetAwaiter().GetResult();
        return 0;
    }
}
