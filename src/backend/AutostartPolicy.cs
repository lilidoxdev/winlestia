// Pure decisions for the logon autostart (no Store, no process, no registry): linked into tests/AutostartPolicy.
using System;
using System.Security;

namespace PanneauxGlass;

static class AutostartPolicy
{
    internal const string TaskName = "WinlestiaShell";

    internal enum Step
    {
        None,
        CreateTaskThenDropRun,
    }

    // Temporary task XML lives in the per-user folder (LOCALAPPDATA, Panneaux, tmp), never in the shared TEMP; unique name per call.
    internal static string PrivateTaskFile(string localAppData) =>
        System.IO.Path.Combine(
            localAppData,
            "Panneaux",
            "tmp",
            "winlestia-task-" + Guid.NewGuid().ToString("N") + ".xml"
        );

    // Migration au lancement : une valeur Run présente (ancienne version ou case de l'installeur) devient une tâche.
    // Le chemin doit être celui d'une installation (Glass-v5), pas d'un dossier de développement.
    internal static Step Plan(bool runPresent, string processPath) =>
        runPresent && processPath.Contains("Glass-v5", StringComparison.OrdinalIgnoreCase)
            ? Step.CreateTaskThenDropRun
            : Step.None;

    // La valeur Run n'est retirée que si la tâche existe réellement.
    internal static bool DropRunAfter(bool taskCreated) => taskCreated;

    // Tâche utilisateur « à l'ouverture de session » : sans délai, sans droits élevés, sans limite de durée.
    // --qt-shell seul : jamais --show, qui ouvrirait le tableau de bord à chaque session.
    internal static string TaskXml(string user, string exePath, string workingDirectory)
    {
        string u = SecurityElement.Escape(user) ?? "",
            exe = SecurityElement.Escape("\"" + exePath + "\"") ?? "",
            dir = SecurityElement.Escape(workingDirectory) ?? "";
        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n"
            + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n"
            + "  <RegistrationInfo><Description>WinlestiaShell</Description></RegistrationInfo>\n"
            + "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>"
            + u
            + "</UserId></LogonTrigger></Triggers>\n"
            + "  <Principals><Principal id=\"Author\"><UserId>"
            + u
            + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\n"
            + "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>"
            + "false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>false</StartWhenAvailable><RunOnlyIfNetworkAvailable>"
            + "false</RunOnlyIfNetworkAvailable><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle>"
            + "<WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>4</Priority></Settings>\n"
            + "  <Actions Context=\"Author\"><Exec><Command>"
            + exe
            + "</Command><Arguments>--qt-shell</Arguments><WorkingDirectory>"
            + dir
            + "</WorkingDirectory></Exec></Actions>\n"
            + "</Task>\n";
    }
}
