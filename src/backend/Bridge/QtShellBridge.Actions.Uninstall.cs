// language: C# 12, file: QtShellBridge.Actions.Uninstall.cs, target: Windows 11 / .NET 8, action du pont : désinstallation depuis le shell
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    // Ordre voulu : 1. barre des tâches rétablie et vérifiée (sinon on abandonne, le shell reste) ; 2. outil de désinstallation
    // choisi ; 3. copie dans %TEMP% puis lancement détaché ; 4. arrêt du shell.
    [BridgeAction("uninstall")]
    async Task OnUninstall(BridgeCall c)
    {
        bool keepData = !c.Args.TryGetProperty("keepData", out var flag) || flag.ValueKind != System.Text.Json.JsonValueKind.False;
        Store.Log("uninstall : demande reçue (keepData=" + keepData + ")");
        try
        {
            c.Notice = await Task.Run(() => RunUninstall(keepData));
        }
        catch (Exception ex)
        {
            Store.Log("uninstall : échec inattendu : " + ex);
            c.Notice = "La désinstallation a échoué. Winlestia reste en place.";
        }
    }

    string? RunUninstall(bool keepData)
    {
        // (a) Même chemin que --restore-taskbar : préférence ReplaceTaskbar=false, puis RestoreSaved.
        Store.Log("uninstall : restauration de la barre des tâches");
        bool replacedBefore = Store.State.ReplaceTaskbar;
        dispatcher.Invoke(() => owner.SetTaskbar(false));
        TaskbarNative.RestoreSaved();
        if (!WaitForTaskbar(TimeSpan.FromSeconds(8)))
        {
            Store.Log("uninstall : abandon, explorer.exe ou la barre des tâches n'est pas visible");
            ReenableTaskbar(replacedBefore);
            return "Désinstallation annulée : la barre des tâches Windows n’a pas pu être rétablie. Winlestia reste en place.";
        }
        Store.Log("uninstall : barre des tâches visible");

        // (b) + (c)
        var launch = PrepareUninstaller(keepData, out string? error);
        if (launch == null)
        {
            Store.Log("uninstall : abandon, " + error);
            ReenableTaskbar(replacedBefore);
            return error;
        }
        Store.Log("uninstall : lancement " + launch.FileName + " " + launch.Arguments);
        try
        {
            shellPort.StartProcess(launch);
        }
        catch (Exception ex)
        {
            Store.Log("uninstall : lancement impossible : " + ex.Message);
            ReenableTaskbar(replacedBefore);
            return "Désinstallation annulée : l’outil n’a pas pu être lancé. Winlestia reste en place.";
        }

        // (d)
        Store.Log("uninstall : arrêt du shell");
        _ = dispatcher.BeginInvoke((Action)(() => shellPort.Shutdown()));
        return null;
    }

    // Abandon : la préférence ReplaceTaskbar et l'état de la barre reviennent comme avant la demande.
    void ReenableTaskbar(bool replacedBefore)
    {
        if (!replacedBefore)
            return;
        Store.Log("uninstall : remise en place du dock (SetTaskbar(true))");
        dispatcher.Invoke(() => owner.SetTaskbar(true));
    }

    static bool WaitForTaskbar(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        do
        {
            if (Process.GetProcessesByName("explorer").Length > 0 && TaskbarNative.ExplorerBars().Any(TaskbarNative.IsWindowVisible))
                return true;
            System.Threading.Thread.Sleep(250);
        } while (DateTime.UtcNow < until);
        return false;
    }

    static ProcessStartInfo? PrepareUninstaller(bool keepData, out string? error)
    {
        error = null;
        string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs",
            "Winlestia");
        string work = Path.Combine(Path.GetTempPath(), "WinlestiaUninstall-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        string setup = Path.Combine(installDir, "Setup", "WinlestiaSetup.exe");
        string? inno = Directory.Exists(installDir)
            ? Directory.EnumerateFiles(installDir, "unins*.exe", SearchOption.TopDirectoryOnly).OrderBy(f => f).FirstOrDefault()
            : null;
        try
        {
            if (File.Exists(setup))
            {
                Directory.CreateDirectory(work);
                string copy = Path.Combine(work, "WinlestiaSetup.exe");
                File.Copy(setup, copy, true);
                Store.Log("uninstall : outil = WinlestiaSetup.exe copié dans " + work);
                return new ProcessStartInfo(copy, "--uninstall " + (keepData ? "--keep-data" : "--remove-data") + " --from-shell")
                {
                    UseShellExecute = true,
                    WorkingDirectory = work
                };
            }
            // Inno Setup ne sait pas envoyer les données à la Corbeille et se déplace seul dans %TEMP% : il ne sert que si l'on garde les
            // données.
            if (inno != null && keepData)
            {
                Store.Log("uninstall : outil = désinstalleur Inno Setup " + inno + " (lancé en place, il se copie lui-même dans %TEMP%)");
                return new ProcessStartInfo(inno) { UseShellExecute = true, WorkingDirectory = installDir };
            }
            foreach (string dir in new[] { installDir, Path.Combine(installDir, "Setup"), Store.Root,
                Path.GetFullPath(Path.Combine(Store.Root, "..")) })
            {
                string script = Path.Combine(dir, "Uninstall-Winlestia.ps1");
                if (!File.Exists(script))
                    continue;
                Directory.CreateDirectory(work);
                File.Copy(script, Path.Combine(work, "Uninstall-Winlestia.ps1"), true);
                string closer = Path.Combine(dir, "Close-Winlestia.ps1");
                if (File.Exists(closer))
                    File.Copy(closer, Path.Combine(work, "Close-Winlestia.ps1"), true);
                Store.Log("uninstall : outil = script de repli " + script + " copié dans " + work);
                string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                return new ProcessStartInfo(powershell, "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(work,
                    "Uninstall-Winlestia.ps1") + "\" " + (keepData ? "-KeepData" : "-RemoveData") + " -InstallDir \"" + installDir + "\"")
                {
                    UseShellExecute = true,
                    WorkingDirectory = work
                };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Store.Log("uninstall : préparation impossible : " + ex.Message);
            error = "Désinstallation annulée : impossible de préparer l’outil dans le dossier temporaire. Winlestia reste en place.";
            return null;
        }
        error = "Désinstallation annulée : aucun outil de désinstallation n’a été trouvé. Winlestia reste en place.";
        return null;
    }
}
