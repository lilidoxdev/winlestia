// C# 12, fichier Installer.cs, cible Windows 11 / .NET 8, installation élevée : pilote PawnIO, copie dans Program Files, tâche planifiée
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WinlestiaSensors;

internal static class Installer
{
    const string TaskName = "WinlestiaSensors";
    const string ExeName = "Winlestia.Sensors.exe";
    const string DriverUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
    const long MaxDownload = 50L * 1024 * 1024;

    static string TargetDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Winlestia", "Sensors");

    internal static bool DriverInstalled()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                if (key != null)
                    return true;
            }
            catch (Exception) { }
        }
        return false;
    }

    internal static int Install(bool driver)
    {
        // Aucune écriture de journal avant la copie dans Program Files (dossier encore non protégé).
        Log.Suspended = true;
        bool reboot = false;
        if (driver && !DriverInstalled())
        {
            int code = InstallDriver(out reboot);
            if (code != 0)
                return code;
        }
        try
        {
            StopRunning();
            CopySelf();
            Log.Suspended = false;
            Log.Write("Installation" + (driver ? " avec pilote" : ""));
            if (!CreateTask())
                return 30;
            int run = Schtasks("/Run /TN " + TaskName, out _);
            if (run != 0)
            {
                Log.Write("Tâche non lancée (code " + run + ")");
                return 30;
            }
        }
        catch (Exception ex)
        {
            Log.Write("Copie/tâche: " + Log.Describe(ex));
            return 30;
        }
        Log.Write("Installation terminée" + (reboot ? " (redémarrage requis)" : ""));
        return reboot ? 10 : 0;
    }

    internal static int Uninstall()
    {
        Log.Write("Désinstallation");
        try
        {
            StopRunning();
            Schtasks("/Delete /TN " + TaskName + " /F", out _);
            string target = TargetDir;
            if (!Directory.Exists(target))
                return 0;
            // Chemins normalisés avec antislash final : « ...\Sensors2 » ne doit pas passer pour « ...\Sensors ».
            string self = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\') + "\\";
            if (self.StartsWith(Path.GetFullPath(target).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                // Notre propre dossier est verrouillé tant qu'on tourne : suppression différée par un cmd détaché.
                var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                info.ArgumentList.Add("/c");
                info.ArgumentList.Add("ping -n 4 127.0.0.1 >nul & rd /s /q \"" + target + "\"");
                Process.Start(info)?.Dispose();
            }
            else
                Directory.Delete(target, true);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write("Désinstallation: " + Log.Describe(ex));
            return 30;
        }
    }

    // 20 = téléchargement ou signature, 21 = échec de l'installeur du pilote.
    static int InstallDriver(out bool reboot)
    {
        reboot = false;
        string? dir = null;
        try
        {
            dir = CreateAdminOnlyDirectory(Path.Combine(TargetDir, "download-" + Guid.NewGuid().ToString("N")));
            string file = Path.Combine(dir, "PawnIO_setup.exe");
            try { Download(DriverUrl, file).GetAwaiter().GetResult(); }
            catch (Exception ex) { Log.Write("Téléchargement: " + ex.GetType().Name); return 20; }
            int trust = VerifySignature(file);
            if (trust != 0)
            {
                Log.Write("Signature du pilote refusée (0x" + trust.ToString("X8") + ")");
                return 20;
            }
            var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
            info.ArgumentList.Add("-install");
            info.ArgumentList.Add("-silent");
            using var p = Process.Start(info);
            if (p == null)
                return 21;
            if (!p.WaitForExit(300000))
            {
                // Arrêt de tout l'arbre et attente avant de supprimer le dossier (sinon fichiers verrouillés).
                try { p.Kill(entireProcessTree: true); p.WaitForExit(15000); } catch (Exception) { }
                Log.Write("Installeur du pilote sans réponse");
                return 21;
            }
            Log.Write("Installeur du pilote : code " + p.ExitCode);
            if (p.ExitCode is not (0 or 3010))
                return 21;
            reboot = p.ExitCode == 3010;
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write("Pilote: " + Log.Describe(ex));
            return 21;
        }
        finally
        {
            try { if (dir != null) Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    // Dossier neuf, Administrateurs et SYSTEM seulement, héritage coupé : jamais d'exécutable pris dans un emplacement modifiable.
    static string CreateAdminOnlyDirectory(string path)
    {
        if (Directory.Exists(path))
            throw new IOException("dossier déjà présent");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).Create(security);
        return path;
    }

    static async Task Download(string url, string file)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        // Délai total de 2 minutes pour l'ensemble (en-têtes et corps).
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = cts.Token;
        var uri = new Uri(url);
        for (int hop = 0; hop < 6; hop++)
        {
            if (!HostAllowed(uri))
                throw new InvalidOperationException("adresse refusée");
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
            {
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxDownload)
                throw new InvalidOperationException("fichier trop gros");
            try
            {
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long total = 0;
                int n;
                while ((n = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += n;
                    if (total > MaxDownload)
                        throw new InvalidOperationException("fichier trop gros");
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            catch (Exception)
            {
                // Suppression du fichier partiel en cas d'échec.
                try { File.Delete(file); } catch (Exception) { }
                throw;
            }
            return;
        }
        throw new InvalidOperationException("trop de redirections");
    }

    // HTTPS seulement, et hôte GitHub connu à chaque étape de redirection.
    internal static bool HostAllowed(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;
        string host = uri.IdnHost.ToLowerInvariant();
        return host is "github.com" or "objects.githubusercontent.com" or "release-assets.githubusercontent.com"
            || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }

    // Authenticode via WinVerifyTrust, révocation vérifiée sur toute la chaîne. 0 = valide.
    // TODO : ni le sujet du certificat ni l'empreinte SHA-256 de l'installeur PawnIO ne sont épinglés (valeurs vérifiées non disponibles) ;
    // toute signature Authenticode valide est donc acceptée. À épingler dès que les valeurs de référence sont connues.
    static int VerifySignature(string file)
    {
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
        IntPtr path = Marshal.StringToCoTaskMemUni(file);
        var fileInfo = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = path };
        IntPtr filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        IntPtr dataPtr = IntPtr.Zero;
        var data = new WinTrustData();
        bool opened = false;
        try
        {
            Marshal.StructureToPtr(fileInfo, filePtr, false);
            data.cbStruct = (uint)Marshal.SizeOf<WinTrustData>();
            data.dwUIChoice = 2;               // WTD_UI_NONE
            data.fdwRevocationChecks = 1;      // WTD_REVOKE_WHOLECHAIN
            data.dwUnionChoice = 1;            // WTD_CHOICE_FILE
            data.pFile = filePtr;
            data.dwStateAction = 1;            // WTD_STATEACTION_VERIFY
            data.dwProvFlags = 0x40;           // WTD_REVOCATION_CHECK_CHAIN
            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(data, dataPtr, false);
            opened = true;
            return WinVerifyTrust(new IntPtr(-1), ref action, dataPtr);
        }
        finally
        {
            if (opened)
            {
                data = Marshal.PtrToStructure<WinTrustData>(dataPtr);
                data.dwStateAction = 2;        // WTD_STATEACTION_CLOSE
                Marshal.StructureToPtr(data, dataPtr, false);
                WinVerifyTrust(new IntPtr(-1), ref action, dataPtr);
            }
            if (dataPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(dataPtr);
            Marshal.FreeHGlobal(filePtr);
            Marshal.FreeCoTaskMem(path);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinTrustFileInfo
    {
        internal uint cbStruct; internal IntPtr pcwszFilePath; internal IntPtr hFile; internal IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinTrustData
    {
        internal uint cbStruct; internal IntPtr pPolicyCallbackData; internal IntPtr pSIPClientData; internal uint dwUIChoice;
        internal uint fdwRevocationChecks; internal uint dwUnionChoice; internal IntPtr pFile; internal uint dwStateAction;
        internal IntPtr hWVTStateData; internal IntPtr pwszURLReference; internal uint dwProvFlags; internal uint dwUIContext;
        internal IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, IntPtr data);

    // Arrête la tâche et toute instance en cours dont l'exécutable est dans le dossier cible (pas la nôtre).
    static void StopRunning()
    {
        Schtasks("/End /TN " + TaskName, out _);
        string target = Path.GetFullPath(TargetDir).TrimEnd('\\') + "\\";
        foreach (var p in Process.GetProcessesByName("Winlestia.Sensors"))
        {
            using (p)
            {
                try
                {
                    if (p.Id == Environment.ProcessId)
                        continue;
                    if (p.MainModule?.FileName?.StartsWith(target, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                }
                catch (Exception) { }
            }
        }
    }

    // Copie du dossier de l'assistant (exe, dll, json) dans Program Files, réservé aux administrateurs.
    static void CopySelf()
    {
        string source = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
        string target = Path.GetFullPath(TargetDir).TrimEnd('\\');
        var src = new DirectoryInfo(source);
        if (IsReparse(src))
            throw new IOException("source = point d'analyse");
        ProtectTarget(target);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            return;
        // Liste blanche : fichiers de premier niveau (exe, dll, json) et le dossier « runtimes » standard ; jamais de lien suivi.
        CopyAllowed(src, target, new[] { ".exe", ".dll", ".json" });
        var runtimes = new DirectoryInfo(Path.Combine(source, "runtimes"));
        if (runtimes.Exists && !IsReparse(runtimes))
            CopyTree(runtimes, Path.Combine(target, "runtimes"));
    }

    static bool IsReparse(FileSystemInfo info) => info.Attributes.HasFlag(FileAttributes.ReparsePoint);

    static void CopyAllowed(DirectoryInfo from, string to, string[] extensions)
    {
        foreach (var file in from.EnumerateFiles())
        {
            if (IsReparse(file) || Array.IndexOf(extensions, file.Extension.ToLowerInvariant()) < 0)
                continue;
            File.Copy(file.FullName, Path.Combine(to, file.Name), true);
        }
    }

    // « runtimes » : bibliothèques natives seulement (dll), sous-dossiers réels uniquement.
    static void CopyTree(DirectoryInfo from, string to)
    {
        Directory.CreateDirectory(to);
        CopyAllowed(from, to, new[] { ".dll" });
        foreach (var sub in from.EnumerateDirectories())
            if (!IsReparse(sub))
                CopyTree(sub, Path.Combine(to, sub.Name));
    }

    // Destination : jamais un point d'analyse (remplacé), ACL protégée explicite : SYSTEM et Administrateurs tout, Utilisateurs lecture/exécution.
    static void ProtectTarget(string target)
    {
        var dir = new DirectoryInfo(target);
        if (dir.Exists && IsReparse(dir))
        {
            dir.Delete(false); // supprime le lien seul, jamais sa cible
            dir.Refresh();
        }
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        if (dir.Exists)
            dir.SetAccessControl(security);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            dir.Create(security);
        }
    }

    static bool CreateTask()
    {
        string exe = Path.Combine(TargetDir, ExeName);
        // Tâche en SYSTEM (S-1-5-18) au démarrage, relançable à la demande (/Run) et redémarrée 3 fois en cas d'échec ;
        // le XML évite l'arrêt sur batterie du /SC simple.
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n"
            + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"
            + "<Triggers><BootTrigger><Enabled>true</Enabled></BootTrigger></Triggers>"
            + "<Principals><Principal id=\"Author\"><UserId>S-1-5-18</UserId><LogonType>ServiceAccount</LogonType>"
            + "<RunLevel>HighestAvailable</RunLevel></Principal></Principals>"
            + "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>"
            + "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>"
            + "<AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable>"
            + "<RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>"
            + "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Hidden>true</Hidden><Enabled>true</Enabled></Settings>"
            + "<Actions Context=\"Author\"><Exec><Command>" + Esc(exe) + "</Command><Arguments>run</Arguments></Exec></Actions></Task>";
        string file = Path.Combine(TargetDir, "task.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            int code = Schtasks("/Create /F /TN " + TaskName + " /XML \"" + file + "\"", out string text);
            if (code != 0)
                Log.Write("Tâche non créée (code " + code + ")");
            return code == 0;
        }
        finally { try { File.Delete(file); } catch (Exception) { } }
    }

    static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    static int Schtasks(string arguments, out string text)
    {
        try
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(info)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30000))
            {
                try { p.Kill(); } catch (Exception) { }
                text = "délai dépassé";
                return -1;
            }
            text = (output.Result + " " + error.Result).Trim();
            return p.ExitCode;
        }
        catch (Exception ex) { text = Log.Describe(ex); return -1; }
    }
}
