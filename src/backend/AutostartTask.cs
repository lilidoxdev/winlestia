// language: C# 12, file: AutostartTask.cs, target: Windows 11 / .NET 8, per-user logon task (no admin, no window)
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace PanneauxGlass;

// La clé Run est retardée par Windows (mesuré : 80 s après Explorer) ; une tâche LogonTrigger part sans délai.
internal static class AutostartTask
{
    static int state = -1; // -1 inconnu, 0 absente, 1 présente : évite de lancer schtasks à chaque instantané d'état.
    static readonly ManualResetEventSlim resolved = new(false);
    internal static bool Present
    {
        get
        {
            if (Volatile.Read(ref state) < 0)
                resolved.Wait(3000);
            return Volatile.Read(ref state) == 1;
        }
    }

    static int Run(string arguments, out string text)
    {
        try
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                try
                {
                    process.Kill();
                }
                catch { }
                text = "délai dépassé";
                return -1;
            }
            text = (output.Result + " " + error.Result).Trim();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            text = ex.Message;
            return -1;
        }
    }

    internal static void Refresh()
    {
        Volatile.Write(ref state, Run("/query /tn " + AutostartPolicy.TaskName, out _) == 0 ? 1 : 0);
        resolved.Set();
    }

    internal static bool Create()
    {
        string xml = "";
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local))
            {
                Store.Log("Tâche de démarrage: dossier LocalApplicationData introuvable.");
                return false;
            }
            xml = AutostartPolicy.PrivateTaskFile(local);
            Directory.CreateDirectory(Path.GetDirectoryName(xml)!);
            string exe = Environment.ProcessPath!;
            File.WriteAllText(
                xml,
                AutostartPolicy.TaskXml(WindowsIdentity.GetCurrent().Name, exe, Path.GetDirectoryName(exe) ?? ""),
                Encoding.Unicode
            );
            int code = Run("/create /tn " + AutostartPolicy.TaskName + " /xml \"" + xml + "\" /f", out var text);
            if (code != 0)
            {
                Store.Log("Tâche de démarrage non créée (code " + code + "): " + text);
                return false;
            }
            Volatile.Write(ref state, 1);
            Store.Log("Tâche de démarrage WinlestiaShell créée.");
            return true;
        }
        catch (Exception ex)
        {
            Store.Log("Tâche de démarrage: " + ex.Message);
            return false;
        }
        finally
        {
            try
            {
                if (xml.Length > 0)
                    File.Delete(xml);
            }
            catch { }
        }
    }

    internal static bool Delete()
    {
        Run("/delete /tn " + AutostartPolicy.TaskName + " /f", out _);
        Refresh();
        return !Present;
    }
}
